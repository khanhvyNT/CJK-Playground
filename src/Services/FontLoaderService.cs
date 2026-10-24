using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public class LoadedFontInfo
    {
        public string FontPath { get; set; }
        public string FamilyName { get; set; }
        public bool IsCustomFont { get; set; }
        public long FileSize { get; set; }
        public FontFamily FontFamily { get; set; }
        public ushort UnitsPerEm { get; set; }
        public short Ascender { get; set; }
        public short Descender { get; set; }
        public int NumGlyphs { get; set; }
        public int SupportedCharsCount { get; set; }

        public LoadedFontInfo(string familyName, FontFamily family, string path = null, bool isCustom = false)
        {
            FamilyName = familyName;
            FontFamily = family;
            FontPath = path;
            IsCustomFont = isCustom;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                FileSize = new FileInfo(path).Length;
            }
            UnitsPerEm = 1000;
            Ascender = 800;
            Descender = -200;
        }
    }

    public class FontLoaderService : IDisposable
    {
        private PrivateFontCollection _privateFonts = new PrivateFontCollection();
        public LoadedFontInfo CurrentFont { get; private set; }
        public OpenTypeBinaryReader OpenTypeReader { get; private set; }
        public bool AutoCompositeEnabled { get; set; }

        // Fallback readers for automatic composite engine
        private OpenTypeBinaryReader _extBReader;
        private OpenTypeBinaryReader _bmpReader;
        private OpenTypeBinaryReader _kanaReader;
        private OpenTypeBinaryReader _hangulReader;
        private readonly object _syncLock = new object();

        public FontLoaderService()
        {
            AutoCompositeEnabled = true;
            SelectDefaultFont();
        }

        public LoadedFontInfo LoadFontFromFile(string filePath, bool isCustom = true)
        {
            if (!File.Exists(filePath))
            {
                bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                throw new FileNotFoundException((isVi ? "Không tìm thấy file font: " : "Font file not found: ") + filePath);
            }

            // 1. Parse OpenType binary tables directly
            if (OpenTypeReader != null)
            {
                OpenTypeReader.Dispose();
                OpenTypeReader = null;
            }
            OpenTypeReader = new OpenTypeBinaryReader(filePath);

            // 2. Load into GDI+ for standard raster fallback
            _privateFonts.Dispose();
            _privateFonts = new PrivateFontCollection();
            _privateFonts.AddFontFile(filePath);

            FontFamily family = _privateFonts.Families.Length > 0 ? _privateFonts.Families[0] : FontFamily.GenericSerif;
            string familyName = !string.IsNullOrEmpty(OpenTypeReader.Header.FamilyName)
                ? OpenTypeReader.Header.FamilyName
                : family.Name;

            CurrentFont = new LoadedFontInfo(familyName, family, filePath, isCustom)
            {
                UnitsPerEm = OpenTypeReader.Header.UnitsPerEm,
                Ascender = OpenTypeReader.Header.Ascender,
                Descender = OpenTypeReader.Header.Descender,
                NumGlyphs = OpenTypeReader.Header.NumGlyphs,
                SupportedCharsCount = OpenTypeReader.SupportedCharacterCount
            };

            return CurrentFont;
        }

        public List<string> GetSystemCjkFonts()
        {
            var list = new List<string>();
            var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SimSun", "SimSun-ExtB", "MingLiU", "MingLiU-ExtB", "MS Mincho", "MS Gothic",
                "Yu Gothic", "Meiryo", "Malgun Gothic", "Batang", "Gulim",
                "Noto Serif CJK SC", "Noto Sans CJK SC", "BabelStone Han", "Nom Na Tong",
                "KaiTi", "FangSong", "Microsoft YaHei", "Microsoft JhengHei"
            };

            using (var installed = new InstalledFontCollection())
            {
                foreach (var fam in installed.Families)
                {
                    if (targetNames.Contains(fam.Name))
                    {
                        list.Add(fam.Name);
                    }
                }
            }
            return list;
        }

        public LoadedFontInfo SelectSystemFont(string familyName)
        {
            string candidatePath = ResolveSystemFontPath(familyName);

            if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
            {
                try
                {
                    return LoadFontFromFile(candidatePath, false);
                }
                catch
                {
                    // Fall back to GDI+ FontFamily below
                }
            }

            var fam = new FontFamily(familyName);
            CurrentFont = new LoadedFontInfo(familyName, fam, null, false);
            return CurrentFont;
        }

        public static string ResolveSystemFontPath(string familyName)
        {
            string fontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "SimSun", "simsun.ttc" },
                { "SimSun-ExtB", "simsunb.ttf" },
                { "MingLiU", "mingliu.ttc" },
                { "MingLiU-ExtB", "mingliub.ttc" },
                { "MS Mincho", "msmincho.ttc" },
                { "MS Gothic", "msgothic.ttc" },
                { "Meiryo", "meiryo.ttc" },
                { "Malgun Gothic", "malgun.ttf" },
                { "KaiTi", "simkai.ttf" },
                { "FangSong", "simfang.ttf" }
            };

            string file;
            if (map.TryGetValue(familyName, out file))
            {
                string p = Path.Combine(fontsDir, file);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        #region Automatic Composite Font Engine
        /// <summary>
        /// Automatically switches active font to best system font matching the Unicode block.
        /// </summary>
        public bool AutoSelectFontForBlock(UnicodeBlock block, bool force = false)
        {
            if (block == null) return false;
            return AutoSelectFontForRange(block.StartCp, block.EndCp, force);
        }

        public bool AutoSelectFontForRange(int startCp, int endCp, bool force = false)
        {
            if (!AutoCompositeEnabled && !force) return false;
            if (CurrentFont != null && CurrentFont.IsCustomFont && !force) return false;

            string targetFont = null;

            // 1. Plane 2 (SIP) - CJK Extension B..F / Chữ Nôm
            if (startCp >= 0x20000 && startCp <= 0x2FA1F)
            {
                string simsunb = ResolveSystemFontPath("SimSun-ExtB");
                if (simsunb != null) targetFont = "SimSun-ExtB";
                else targetFont = "MingLiU-ExtB";
            }
            // 2. Korean Hangul
            else if (startCp >= 0xAC00 && endCp <= 0xD7AF)
            {
                targetFont = "Malgun Gothic";
            }
            // 3. Japanese Kana
            else if ((startCp >= 0x3040 && endCp <= 0x30FF) || (startCp >= 0x31F0 && endCp <= 0x31FF))
            {
                targetFont = "MS Gothic";
            }
            // 4. BMP CJK Ideographs, Ext A, Radicals, Strokes
            else if (startCp >= 0x2E80 && startCp <= 0x9FFF)
            {
                targetFont = "SimSun";
            }
            // 5. Latin, European, Greek, Cyrillic
            else if (startCp < 0x2E80)
            {
                targetFont = "Segoe UI";
            }
            else
            {
                targetFont = "SimSun";
            }

            if (!string.IsNullOrEmpty(targetFont))
            {
                if (CurrentFont == null || !string.Equals(CurrentFont.FamilyName, targetFont, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        SelectSystemFont(targetFont);
                        return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        /// <summary>
        /// Dynamically creates a Font instance specifically capable of rendering the given code point.
        /// Prevents missing tofu/boxes in mixed lists (e.g. BMP alongside Plane 2 Chữ Nôm).
        /// </summary>
        public Font GetFontForCodePoint(int codePoint, float emSize, FontStyle style = FontStyle.Regular)
        {
            // If custom font is explicitly loaded by user and natively supports this character, prioritize it
            if (CurrentFont != null && CurrentFont.IsCustomFont && OpenTypeReader != null && OpenTypeReader.IsCharacterSupported(codePoint))
            {
                try { return new Font(CurrentFont.FontFamily, emSize, style); } catch { }
            }

            // Plane 2 (SIP) / Chữ Nôm Ext B..F -> SimSun-ExtB or MingLiU-ExtB
            if (codePoint >= 0x20000 && codePoint <= 0x2FA1F)
            {
                try { return new Font("SimSun-ExtB", emSize, style); } catch { }
                try { return new Font("MingLiU-ExtB", emSize, style); } catch { }
            }
            // Korean Hangul
            else if (codePoint >= 0xAC00 && codePoint <= 0xD7AF)
            {
                try { return new Font("Malgun Gothic", emSize, style); } catch { }
            }
            // Japanese Kana
            else if ((codePoint >= 0x3040 && codePoint <= 0x30FF) || (codePoint >= 0x31F0 && codePoint <= 0x31FF))
            {
                try { return new Font("MS Gothic", emSize, style); } catch { }
            }
            // BMP CJK & Radicals & Ideographic Description Characters (U+2FF0..U+2FFF)
            else if ((codePoint >= 0x3400 && codePoint <= 0x9FFF) ||
                     (codePoint >= 0x2E80 && codePoint <= 0x2FFF) ||
                     (codePoint >= 0xF900 && codePoint <= 0xFAFF))
            {
                try { return new Font("SimSun", emSize, style); } catch { }
            }

            // Fallback to CurrentFont or Segoe UI
            return CreateFontInstance(emSize, style);
        }

        private OpenTypeBinaryReader GetExtBReader()
        {
            lock (_syncLock)
            {
                if (_extBReader == null)
                {
                    string p = ResolveSystemFontPath("SimSun-ExtB");
                    if (p != null && File.Exists(p))
                    {
                        try { _extBReader = new OpenTypeBinaryReader(p); } catch { }
                    }
                    if (_extBReader == null)
                    {
                        string p2 = ResolveSystemFontPath("MingLiU-ExtB");
                        if (p2 != null && File.Exists(p2))
                        {
                            try { _extBReader = new OpenTypeBinaryReader(p2); } catch { }
                        }
                    }
                }
                return _extBReader;
            }
        }

        private OpenTypeBinaryReader GetBmpReader()
        {
            lock (_syncLock)
            {
                if (_bmpReader == null)
                {
                    string p = ResolveSystemFontPath("SimSun");
                    if (p != null && File.Exists(p))
                    {
                        try { _bmpReader = new OpenTypeBinaryReader(p); } catch { }
                    }
                }
                return _bmpReader;
            }
        }

        private OpenTypeBinaryReader GetFallbackReaderForCodePoint(int codePoint)
        {
            if (codePoint >= 0x20000 && codePoint <= 0x2FA1F)
            {
                return GetExtBReader();
            }
            if ((codePoint >= 0x3400 && codePoint <= 0x9FFF) ||
                (codePoint >= 0x2E80 && codePoint <= 0x2FDF))
            {
                return GetBmpReader();
            }
            return null;
        }

        private static OpenTypeBinaryReader s_extBReader;
        private static OpenTypeBinaryReader s_bmpReader;
        private static readonly object s_readerLock = new object();

        public static bool IsCodePointSupportedSystem(int codePoint)
        {
            if (codePoint <= 0) return false;
            lock (s_readerLock)
            {
                if (codePoint >= 0x20000)
                {
                    if (s_extBReader == null)
                    {
                        string p = ResolveSystemFontPath("SimSun-ExtB");
                        if (p != null && File.Exists(p))
                        {
                            try { s_extBReader = new OpenTypeBinaryReader(p); } catch { }
                        }
                        if (s_extBReader == null)
                        {
                            string p2 = ResolveSystemFontPath("MingLiU-ExtB");
                            if (p2 != null && File.Exists(p2))
                            {
                                try { s_extBReader = new OpenTypeBinaryReader(p2); } catch { }
                            }
                        }
                    }
                    if (s_extBReader != null)
                        return s_extBReader.IsCharacterSupported(codePoint);
                    return false;
                }
                else if ((codePoint >= 0x3400 && codePoint <= 0x9FFF) ||
                         (codePoint >= 0x2E80 && codePoint <= 0x2FDF))
                {
                    if (s_bmpReader == null)
                    {
                        string p = ResolveSystemFontPath("SimSun");
                        if (p != null && File.Exists(p))
                        {
                            try { s_bmpReader = new OpenTypeBinaryReader(p); } catch { }
                        }
                    }
                    if (s_bmpReader != null)
                        return s_bmpReader.IsCharacterSupported(codePoint);
                    return true;
                }
                return true;
            }
        }
        #endregion

        public Font CreateFontInstance(float emSize, FontStyle style = FontStyle.Regular)
        {
            if (CurrentFont != null && CurrentFont.FontFamily != null)
            {
                try
                {
                    return new Font(CurrentFont.FontFamily, emSize, style);
                }
                catch
                {
                    // Fallback to generic sans-serif
                }
            }
            return new Font(FontFamily.GenericSansSerif, emSize, style);
        }

        public bool IsCharacterSupported(int codePoint)
        {
            if (OpenTypeReader != null && OpenTypeReader.IsCharacterSupported(codePoint))
            {
                return true;
            }

            if (AutoCompositeEnabled)
            {
                var fb = GetFallbackReaderForCodePoint(codePoint);
                if (fb != null && fb.IsCharacterSupported(codePoint))
                {
                    return true;
                }
            }
            return false;
        }

        public bool GetGlyphInfo(int codePoint, out int glyphId, out int advanceWidth)
        {
            glyphId = -1;
            advanceWidth = -1;

            // 1. Primary OpenType reader
            if (OpenTypeReader != null)
            {
                ushort gid = OpenTypeReader.GetGlyphIndex(codePoint);
                if (gid > 0)
                {
                    glyphId = gid;
                    ushort adv;
                    short lsb;
                    OpenTypeReader.GetHorizontalMetrics(gid, out adv, out lsb);
                    advanceWidth = adv;
                    return true;
                }
            }

            // 2. Automatic Composite Fallback reader
            if (AutoCompositeEnabled)
            {
                var fb = GetFallbackReaderForCodePoint(codePoint);
                if (fb != null)
                {
                    ushort gid = fb.GetGlyphIndex(codePoint);
                    if (gid > 0)
                    {
                        glyphId = gid;
                        ushort adv;
                        short lsb;
                        fb.GetHorizontalMetrics(gid, out adv, out lsb);
                        advanceWidth = adv;
                        return true;
                    }
                }
            }

            return false;
        }

        public Bitmap RenderGlyphBitmap(int codePoint, string charText, int size = 512)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                // 1. Try Primary OpenType vector path
                RawGlyphOutline raw = null;
                int unitsPerEm = 1000;
                int ascender = 800;

                if (OpenTypeReader != null && CurrentFont != null && CurrentFont.UnitsPerEm > 0)
                {
                    raw = OpenTypeReader.GetGlyphOutline(codePoint);
                    unitsPerEm = CurrentFont.UnitsPerEm;
                    ascender = CurrentFont.Ascender;
                }

                // 1b. If not found in primary, try Composite Fallback reader
                if ((raw == null || raw.Contours.Count == 0) && AutoCompositeEnabled)
                {
                    var fb = GetFallbackReaderForCodePoint(codePoint);
                    if (fb != null && fb.Header != null && fb.Header.UnitsPerEm > 0)
                    {
                        raw = fb.GetGlyphOutline(codePoint);
                        unitsPerEm = fb.Header.UnitsPerEm;
                        ascender = fb.Header.Ascender;
                    }
                }

                if (raw != null && raw.Contours.Count > 0)
                {
                    using (var path = VectorPathRenderer.CreateGraphicsPath(raw, (ushort)unitsPerEm, (short)ascender, size * 0.85f))
                    {
                        var bounds = path.GetBounds();
                        if (bounds.Width > 0 && bounds.Height > 0)
                        {
                            float dx = (size - bounds.Width) / 2.0f - bounds.X;
                            float dy = (size - bounds.Height) / 2.0f - bounds.Y;
                            using (var matrix = new Matrix())
                            {
                                matrix.Translate(dx, dy);
                                path.Transform(matrix);
                            }
                            using (var brush = new SolidBrush(Color.FromArgb(30, 41, 59)))
                            {
                                g.FillPath(brush, path);
                            }
                            return bmp;
                        }
                    }
                }

                // 2. Fallback GDI+ text render using per-codepoint font
                float fontSize = size * 0.65f;
                using (var font = GetFontForCodePoint(codePoint, fontSize))
                using (var brush = new SolidBrush(Color.FromArgb(30, 41, 59)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(charText, font, brush, new RectangleF(0, 0, size, size), sf);
                }
            }
            return bmp;
        }

        public string ExportGlyphSvg(int codePoint, string charText)
        {
            // 1. Primary OpenType vector path
            if (OpenTypeReader != null)
            {
                var glyph = OpenTypeReader.GetGlyphOutline(codePoint);
                if (glyph != null && glyph.Contours.Count > 0)
                {
                    return VectorPathRenderer.GenerateFullSvg(
                        glyph,
                        CurrentFont.UnitsPerEm,
                        CurrentFont.Ascender,
                        CurrentFont.Descender,
                        CurrentFont.FamilyName
                    );
                }
            }

            // 1b. Composite Fallback reader vector path
            if (AutoCompositeEnabled)
            {
                var fb = GetFallbackReaderForCodePoint(codePoint);
                if (fb != null && fb.Header != null)
                {
                    var glyph = fb.GetGlyphOutline(codePoint);
                    if (glyph != null && glyph.Contours.Count > 0)
                    {
                        return VectorPathRenderer.GenerateFullSvg(
                            glyph,
                            fb.Header.UnitsPerEm,
                            fb.Header.Ascender,
                            fb.Header.Descender,
                            fb.Header.FamilyName ?? "CompositeFont"
                        );
                    }
                }
            }

            // Fallback SVG if font outlines are CFF or glyph not found
            int em = CurrentFont != null ? CurrentFont.UnitsPerEm : 1000;
            int asc = CurrentFont != null ? CurrentFont.Ascender : 800;
            string family = (codePoint >= 0x20000) ? "SimSun-ExtB" : (CurrentFont != null ? CurrentFont.FamilyName : "sans-serif");

            return string.Format(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0} {0}\" width=\"{0}\" height=\"{0}\">\n" +
                "  <rect width=\"100%\" height=\"100%\" fill=\"white\"/>\n" +
                "  <text x=\"{1}\" y=\"{2}\" font-size=\"{3}\" text-anchor=\"middle\" font-family=\"{4}\">{5}</text>\n" +
                "</svg>",
                em,
                em / 2,
                (int)(asc * 0.95),
                (int)(em * 0.8),
                family,
                charText
            );
        }

        private void SelectDefaultFont()
        {
            // Prefer SimSun (covers BMP CJK 0x4E00..0x9FFF standard start block)
            string fontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string simsun = Path.Combine(fontsDir, "simsun.ttc");
            string simsunb = Path.Combine(fontsDir, "simsunb.ttf");

            if (File.Exists(simsun))
            {
                try { LoadFontFromFile(simsun, false); return; } catch { }
            }
            else if (File.Exists(simsunb))
            {
                try { LoadFontFromFile(simsunb, false); return; } catch { }
            }

            var cjkFonts = GetSystemCjkFonts();
            if (cjkFonts.Count > 0)
            {
                SelectSystemFont(cjkFonts[0]);
            }
            else
            {
                SelectSystemFont(FontFamily.GenericSerif.Name);
            }
        }

        private HashSet<int> _compositeSystemSet;
        private List<string> _compositeScannedFonts;

        public HashSet<int> GetSystemCompositeSupportedCodePoints(out List<string> scannedFonts)
        {
            lock (_syncLock)
            {
                if (_compositeSystemSet != null && _compositeScannedFonts != null)
                {
                    scannedFonts = new List<string>(_compositeScannedFonts);
                    return new HashSet<int>(_compositeSystemSet);
                }

                _compositeSystemSet = new HashSet<int>();
                _compositeScannedFonts = new List<string>();

                string fontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
                string[] candidates = new string[]
                {
                    "simsun.ttc", "simsunb.ttf", "mingliu.ttc", "mingliub.ttc",
                    "msgothic.ttc", "msmincho.ttc", "meiryo.ttc", "malgun.ttf",
                    "simkai.ttf", "simfang.ttf", "msyh.ttc", "msjh.ttc", "batang.ttc"
                };

                foreach (var file in candidates)
                {
                    string p = Path.Combine(fontsDir, file);
                    if (File.Exists(p))
                    {
                        try
                        {
                            using (var r = new OpenTypeBinaryReader(p))
                            {
                                var set = r.GetSupportedCodePoints();
                                _compositeSystemSet.UnionWith(set);
                                string name = !string.IsNullOrEmpty(r.Header.FamilyName) ? r.Header.FamilyName : file;
                                bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                                _compositeScannedFonts.Add(string.Format(isVi ? "{0} ({1}: {2:N0} ký tự)" : "{0} ({1}: {2:N0} chars)", name, file, set.Count));
                            }
                        }
                        catch { }
                    }
                }

                // If user currently loaded a custom font, also include its characters
                if (CurrentFont != null && CurrentFont.IsCustomFont && OpenTypeReader != null)
                {
                    var customSet = OpenTypeReader.GetSupportedCodePoints();
                    _compositeSystemSet.UnionWith(customSet);
                    bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                    _compositeScannedFonts.Add(string.Format(isVi ? "{0} (Tệp riêng: {1:N0} ký tự)" : "{0} (Custom font: {1:N0} chars)", CurrentFont.FamilyName, customSet.Count));
                }

                scannedFonts = new List<string>(_compositeScannedFonts);
                return new HashSet<int>(_compositeSystemSet);
            }
        }

        public static FontFamily ResolveExtBFontFamily()
        {
            string[] preferred = new[] { "Nom Na Tong", "SimSun-ExtB", "MingLiU-ExtB", "MingLiU_HKSCS-ExtB" };
            foreach (var name in preferred)
            {
                try
                {
                    using (var test = new Font(name, 12f))
                    {
                        if (test.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            return test.FontFamily;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        public static FontFamily ResolveFontFamilyForCodePoint(int codePoint, FontFamily defaultFont = null)
        {
            if (codePoint >= 0x20000 && codePoint <= 0x2FA1F)
            {
                var extB = ResolveExtBFontFamily();
                if (extB != null) return extB;
            }
            if (defaultFont != null) return defaultFont;
            return IdsGlyphRenderer.ResolveFontFamily();
        }

        public static Bitmap RenderGlyphToBitmap(string ch, int codePoint, FontFamily defaultFont, int sz = 256)
        {
            var bmp = new Bitmap(sz, sz, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                FontFamily fam = ResolveFontFamilyForCodePoint(codePoint, defaultFont);
                var rect = new RectangleF(sz * 0.05f, sz * 0.05f, sz * 0.9f, sz * 0.9f);
                using (var path = IdsGlyphRenderer.CreateComponentVectorPath(ch, fam, rect, null))
                {
                    using (var br = new SolidBrush(Color.Black))
                    {
                        g.FillPath(br, path);
                    }
                }
            }
            return bmp;
        }

        public void Dispose()
        {
            if (OpenTypeReader != null)
            {
                OpenTypeReader.Dispose();
                OpenTypeReader = null;
            }
            if (_extBReader != null)
            {
                _extBReader.Dispose();
                _extBReader = null;
            }
            if (_bmpReader != null)
            {
                _bmpReader.Dispose();
                _bmpReader = null;
            }
            if (_kanaReader != null)
            {
                _kanaReader.Dispose();
                _kanaReader = null;
            }
            if (_hangulReader != null)
            {
                _hangulReader.Dispose();
                _hangulReader = null;
            }
            if (_privateFonts != null)
            {
                _privateFonts.Dispose();
                _privateFonts = null;
            }
        }
    }
}
