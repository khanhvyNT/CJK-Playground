using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public interface IGlyphSource
    {
        string FontKey { get; }
        bool HasGlyph(string ch);
        Bitmap RenderGlyphBitmap(string ch, int pxSize, out RectangleF inkBBox1000);
        void GetFontStrokeThickness(out float txFont, out float tyFont);
        RectangleF MeasureExemplarSafeBox();
    }

    public class GdiGlyphSource : IGlyphSource
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetGlyphIndicesW(IntPtr hdc, string lpstr, int c, [Out] ushort[] pgi, uint fl);
        private const uint GGI_MARK_NONEXISTING_GLYPHS = 0x0001;

        private readonly FontFamily _fontFamily;
        private readonly Func<string, Bitmap> _customGlyphProvider;

        public string FontKey { get { return _fontFamily != null ? _fontFamily.Name : "GenericSansSerif"; } }

        private float _cachedTxFont = 0f;
        private float _cachedTyFont = 0f;
        private RectangleF? _cachedSafeBox = null;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> GlyphSupportCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();

        public GdiGlyphSource(FontFamily font, Func<string, Bitmap> customGlyphProvider = null)
        {
            _fontFamily = font ?? FontFamily.GenericSansSerif;
            _customGlyphProvider = customGlyphProvider;
        }

        public bool HasGlyph(string ch)
        {
            if (string.IsNullOrEmpty(ch) || ch == "□" || ch == " ") return false;

            // 1. Kiểm tra bộ phận tùy biến / PUA tự vẽ
            if (_customGlyphProvider != null && _customGlyphProvider(ch) != null)
                return true;

            string cacheKey = FontKey + "|" + ch;
            bool cachedResult;
            if (GlyphSupportCache.TryGetValue(cacheKey, out cachedResult))
                return cachedResult;

            int cp = HanIdsCorpusIndex.GetFirstCodePoint(ch);

            // 2. Đối với ký tự BMP (cp < 0x10000): dùng Win32 Native GetGlyphIndicesW
            if (cp > 0 && cp < 0x10000 && ch.Length == 1)
            {
                try
                {
                    using (var bmp = new Bitmap(16, 16))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        try
                        {
                            Font f = null;
                            try { f = new Font(_fontFamily, 16f, FontStyle.Regular, GraphicsUnit.Pixel); }
                            catch { f = new Font(FontFamily.GenericSansSerif, 16f, FontStyle.Regular, GraphicsUnit.Pixel); }

                            using (f)
                            {
                                IntPtr hFont = f.ToHfont();
                                IntPtr hOld = SelectObject(hdc, hFont);
                                ushort[] indices = new ushort[ch.Length];
                                uint ret = GetGlyphIndicesW(hdc, ch, ch.Length, indices, GGI_MARK_NONEXISTING_GLYPHS);
                                SelectObject(hdc, hOld);
                                DeleteObject(hFont);

                                if (ret != 0xFFFFFFFF && indices.Length > 0 && indices[0] != 0xFFFF)
                                {
                                    GlyphSupportCache[cacheKey] = true;
                                    return true;
                                }
                            }
                        }
                        finally
                        {
                            g.ReleaseHdc(hdc);
                        }
                    }
                }
                catch { }
            }

            // 3. Fallback (hoặc ký tự ngoài BMP U+20000..U+2FA1F): Kiểm tra bảng cmap OpenType thực tế chống .notdef box
            if (cp >= 0x10000)
            {
                if (!FontLoaderService.IsCodePointSupportedSystem(cp))
                {
                    GlyphSupportCache[cacheKey] = false;
                    return false;
                }
            }

            RectangleF testBBox;
            using (var b = RenderGlyphBitmap(ch, 64, out testBBox))
            {
                bool ok = (b != null && testBBox.Width > 0 && testBBox.Height > 0);
                GlyphSupportCache[cacheKey] = ok;
                return ok;
            }
        }

        public Bitmap RenderGlyphBitmap(string ch, int pxSize, out RectangleF inkBBox1000)
        {
            inkBBox1000 = RectangleF.Empty;
            if (string.IsNullOrEmpty(ch) || ch == "□") return null;

            // Nếu là glyph tự tạo từ provider
            if (_customGlyphProvider != null)
            {
                var custom = _customGlyphProvider(ch);
                if (custom != null)
                {
                    var res = new Bitmap(pxSize, pxSize, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(res))
                    {
                        g.Clear(Color.White);
                        g.DrawImage(custom, new Rectangle(0, 0, pxSize, pxSize));
                    }
                    inkBBox1000 = MeasureInkBoundingBox(res);
                    return res;
                }
            }

            int cp = HanIdsCorpusIndex.GetFirstCodePoint(ch);
            FontFamily effectiveFamily = FontLoaderService.ResolveFontFamilyForCodePoint(cp, _fontFamily);

            var bmp = new Bitmap(pxSize, pxSize, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.None;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

                Font f = null;
                try
                {
                    f = new Font(effectiveFamily, pxSize * 0.85f, FontStyle.Regular, GraphicsUnit.Pixel);
                }
                catch
                {
                    f = new Font(FontFamily.GenericSansSerif, pxSize * 0.85f, FontStyle.Regular, GraphicsUnit.Pixel);
                }

                using (f)
                using (var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    g.DrawString(ch, f, Brushes.Black, new RectangleF(0, 0, pxSize, pxSize), sf);
                }
            }

            inkBBox1000 = MeasureInkBoundingBox(bmp);
            if (inkBBox1000.Width <= 0 || inkBBox1000.Height <= 0)
            {
                bmp.Dispose();
                return null;
            }

            return bmp;
        }

        private static RectangleF MeasureInkBoundingBox(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            int minX = w, maxX = 0, minY = h, maxY = 0;
            bool found = false;

            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bd.Scan0;
                    int stride = bd.Stride;

                    for (int y = 0; y < h; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < w; x++)
                        {
                            byte b = row[x * 4];
                            byte g = row[x * 4 + 1];
                            byte r = row[x * 4 + 2];
                            if (r < 180 || g < 180 || b < 180)
                            {
                                found = true;
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }

            if (!found || minX > maxX || minY > maxY) return RectangleF.Empty;

            // Chuẩn hóa sang đơn vị em-box 1000x1000
            float nx = (float)minX * 1000f / w;
            float ny = (float)minY * 1000f / h;
            float nw = (float)(maxX - minX + 1) * 1000f / w;
            float nh = (float)(maxY - minY + 1) * 1000f / h;

            return new RectangleF(nx, ny, nw, nh);
        }

        public void GetFontStrokeThickness(out float txFont, out float tyFont)
        {
            if (_cachedTxFont > 0f && _cachedTyFont > 0f)
            {
                txFont = _cachedTxFont;
                tyFont = _cachedTyFont;
                return;
            }

            var txList = new System.Collections.Generic.List<float>();
            var tyList = new System.Collections.Generic.List<float>();

            // Đo độ dày nét chuẩn trên 30+ ký tự CJK phổ biến (U+4E00..U+4E35) trong font hiện tại
            for (int cp = 0x4E00; cp <= 0x4E35; cp++)
            {
                string ch = char.ConvertFromUtf32(cp);
                if (!HasGlyph(ch)) continue;

                RectangleF bbox;
                using (var bmp = RenderGlyphBitmap(ch, 256, out bbox))
                {
                    if (bmp != null)
                    {
                        float mx = MeasureMedianStrokeWidth(bmp, true);
                        float my = MeasureMedianStrokeWidth(bmp, false);
                        if (mx > 15f && mx < 250f) txList.Add(mx);
                        if (my > 15f && my < 250f) tyList.Add(my);
                    }
                }
                if (txList.Count >= 30) break;
            }

            float tx = 68.0f;
            float ty = 68.0f;
            if (txList.Count > 0)
            {
                txList.Sort();
                tx = txList[txList.Count / 2];
            }
            if (tyList.Count > 0)
            {
                tyList.Sort();
                ty = tyList[tyList.Count / 2];
            }

            _cachedTxFont = Math.Max(28.0f, Math.Min(120.0f, tx));
            _cachedTyFont = Math.Max(28.0f, Math.Min(120.0f, ty));

            txFont = _cachedTxFont;
            tyFont = _cachedTyFont;
        }

        private static float MeasureMedianStrokeWidth(Bitmap bmp, bool isVertical)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            var runs = new System.Collections.Generic.List<int>();

            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bd.Scan0;
                    int stride = bd.Stride;

                    if (isVertical)
                    {
                        // Quét ngang tìm bề rộng nét dọc
                        for (int y = h / 4; y < h * 3 / 4; y += 4)
                        {
                            byte* row = ptr + y * stride;
                            int currentRun = 0;
                            for (int x = 0; x < w; x++)
                            {
                                bool isInk = (row[x * 4 + 2] < 180 || row[x * 4 + 1] < 180 || row[x * 4] < 180);
                                if (isInk) currentRun++;
                                else
                                {
                                    if (currentRun > 0) runs.Add(currentRun);
                                    currentRun = 0;
                                }
                            }
                            if (currentRun > 0) runs.Add(currentRun);
                        }
                    }
                    else
                    {
                        // Quét dọc tìm bề dày nét ngang
                        for (int x = w / 4; x < w * 3 / 4; x += 4)
                        {
                            int currentRun = 0;
                            for (int y = 0; y < h; y++)
                            {
                                byte* pixel = ptr + y * stride + x * 4;
                                bool isInk = (pixel[2] < 180 || pixel[1] < 180 || pixel[0] < 180);
                                if (isInk) currentRun++;
                                else
                                {
                                    if (currentRun > 0) runs.Add(currentRun);
                                    currentRun = 0;
                                }
                            }
                            if (currentRun > 0) runs.Add(currentRun);
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }

            if (runs.Count == 0) return 68.0f;
            runs.Sort();
            int medianPx = runs[runs.Count / 2];
            return (float)medianPx * 1000f / w;
        }

        public RectangleF MeasureExemplarSafeBox()
        {
            if (_cachedSafeBox.HasValue) return _cachedSafeBox.Value;

            var seeds = ExemplarSeeds.GetSeeds("");
            var minXs = new System.Collections.Generic.List<float>();
            var maxXs = new System.Collections.Generic.List<float>();
            var minYs = new System.Collections.Generic.List<float>();
            var maxYs = new System.Collections.Generic.List<float>();

            foreach (var s in seeds)
            {
                if (!HasGlyph(s.Character)) continue;
                RectangleF bb;
                using (var b = RenderGlyphBitmap(s.Character, 128, out bb))
                {
                    if (b != null && bb.Width > 0 && bb.Height > 0)
                    {
                        minXs.Add(bb.Left);
                        maxXs.Add(bb.Right);
                        minYs.Add(bb.Top);
                        maxYs.Add(bb.Bottom);
                    }
                }
            }

            if (minXs.Count > 0)
            {
                minXs.Sort();
                maxXs.Sort();
                minYs.Sort();
                maxYs.Sort();

                // Dùng phân phối thực tế (10th percentile đến 90th percentile của tập hạt giống)
                int p10 = (int)Math.Max(0, Math.Floor(minXs.Count * 0.10f));
                int p90 = (int)Math.Min(minXs.Count - 1, Math.Ceiling(minXs.Count * 0.90f));

                float mMinX = minXs[p10];
                float mMaxX = maxXs[p90];
                float mMinY = minYs[p10];
                float mMaxY = maxYs[p90];

                _cachedSafeBox = new RectangleF(mMinX, mMinY, Math.Max(100f, mMaxX - mMinX), Math.Max(100f, mMaxY - mMinY));
                return _cachedSafeBox.Value;
            }

            _cachedSafeBox = new RectangleF(90f, 90f, 820f, 820f);
            return _cachedSafeBox.Value;
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
