using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Tests
{
    public static class FontInjectionTests
    {
        public static bool RunAllTests(out string report)
        {
            var logs = new List<string>();
            int passed = 0;
            int failed = 0;

            Action<string, bool, string> assert = delegate(string testName, bool condition, string details)
            {
                if (condition)
                {
                    passed++;
                    logs.Add("[PASS] " + testName);
                }
                else
                {
                    failed++;
                    logs.Add("[FAIL] " + testName + " => " + details);
                }
            };

            // Test 1: VectorToGlyphConverter - Basic conversion & Shoelace area
            using (var path = new GraphicsPath())
            {
                // Vẽ một hình chữ nhật ngoài (100, 100, 800, 800)
                path.AddRectangle(new RectangleF(100, 100, 800, 800));
                // Vẽ một lỗ rỗng tròn bên trong (300, 300, 400, 400)
                path.AddEllipse(new RectangleF(300, 300, 400, 400));

                var options = new GlyphMetricsOptions
                {
                    UnitsPerEm = 1000,
                    Ascender = 880,
                    Descender = -120,
                    AdvanceWidth = 1000,
                    AutoCenterHorizontal = false
                };

                var outline = VectorToGlyphConverter.ConvertPathToTrueTypeGlyph(
                    path,
                    new RectangleF(0, 0, 1000, 1000),
                    options);

                assert("VectorToGlyphConverter.ContoursCount",
                    outline != null && outline.NumberOfContours == 2,
                    outline == null ? "Outline null" : "Số contour khác 2: " + outline.NumberOfContours);

                if (outline != null && outline.Contours.Count == 2)
                {
                    double areaOuter = VectorToGlyphConverter.CalculateSignedArea(outline.Contours[0]);
                    double areaHole = VectorToGlyphConverter.CalculateSignedArea(outline.Contours[1]);

                    // Khung ngoài: Clockwise trong TrueType (Shoelace < 0)
                    // Lỗ rỗng trong: Counter-Clockwise trong TrueType (Shoelace > 0)
                    assert("VectorToGlyphConverter.WindingNormalization",
                        areaOuter < 0 && areaHole > 0,
                        string.Format("Chiều quay không chuẩn: Outer={0:F1}, Hole={1:F1}", areaOuter, areaHole));
                }
            }

            // Test 1b: Overlapping Strokes Winding Preservation (Khac phuc loi mat net do nhan nham lo rong)
            using (var crossPath = new GraphicsPath())
            {
                // Net ngang va net doc giao nhau
                crossPath.AddRectangle(new RectangleF(100, 450, 800, 100));
                crossPath.AddRectangle(new RectangleF(450, 100, 100, 800));

                var optionsCross = new GlyphMetricsOptions
                {
                    UnitsPerEm = 1000,
                    Ascender = 880,
                    Descender = -120,
                    AdvanceWidth = 1000,
                    AutoCenterHorizontal = false
                };

                var crossOutline = VectorToGlyphConverter.ConvertPathToTrueTypeGlyph(
                    crossPath,
                    new RectangleF(0, 0, 1000, 1000),
                    optionsCross);

                bool bothOuter = true;
                if (crossOutline != null && crossOutline.Contours.Count == 2)
                {
                    double a0 = VectorToGlyphConverter.CalculateSignedArea(crossOutline.Contours[0]);
                    double a1 = VectorToGlyphConverter.CalculateSignedArea(crossOutline.Contours[1]);
                    // Ca 2 net giao nhau deu phai la Outer Contours (Shoelace < 0)
                    bothOuter = (a0 < 0 && a1 < 0);
                }
                else
                {
                    bothOuter = false;
                }

                assert("VectorToGlyphConverter.OverlappingStrokesPreservation",
                    bothOuter,
                    "Net ve giao nhau bi nhan nham thanh lo rong khien dao chieu Winding");
            }

            // Test 2: TrueTypeFontWriter.SerializeSimpleGlyph
            var testOutline = new RawGlyphOutline
            {
                AdvanceWidth = 1000,
                LeftSideBearing = 50,
                XMin = 50,
                YMin = 50,
                XMax = 950,
                YMax = 950
            };
            var testContour = new List<OutlinePoint>();
            testContour.Add(new OutlinePoint(100, 100, true));
            testContour.Add(new OutlinePoint(900, 100, true));
            testContour.Add(new OutlinePoint(900, 900, true));
            testContour.Add(new OutlinePoint(100, 900, true));
            testOutline.Contours.Add(testContour);

            byte[] glyphBytes = TrueTypeFontWriter.SerializeSimpleGlyph(testOutline);
            assert("TrueTypeFontWriter.SerializeSimpleGlyph",
                glyphBytes != null && glyphBytes.Length >= 10,
                "Không serialize được Simple Glyph, chiều dài byte không đúng");

            // Test 3: Cmap Table Construction (Format 4 & Format 12)
            var cmapDict = new Dictionary<int, ushort>();
            cmapDict[0x4E00] = 1;
            cmapDict[0x4E01] = 2;
            cmapDict[0xE050] = 100; // PUA BMP
            cmapDict[0x20000] = 200; // Astral Plane 2
            cmapDict[0x2B820] = 201; // Astral Plane 2

            byte[] cmapBytes = TrueTypeFontWriter.BuildCmapTable(cmapDict);
            assert("TrueTypeFontWriter.BuildCmapTable",
                cmapBytes != null && cmapBytes.Length > 36,
                "Bảng CMap tạo ra quá ngắn hoặc bị lỗi");

            // Test 4: Real Font Injection & Round-trip Verification
            string fontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string simsunb = Path.Combine(fontsDir, "simsunb.ttf");
            string arial = Path.Combine(fontsDir, "arial.ttf");
            string consola = Path.Combine(fontsDir, "consola.ttf");

            string sampleFont = null;
            if (File.Exists(arial)) sampleFont = arial;
            else if (File.Exists(consola)) sampleFont = consola;
            else if (File.Exists(simsunb)) sampleFont = simsunb;

            if (sampleFont != null)
            {
                byte[] origBytes = File.ReadAllBytes(sampleFont);

                // Tạo 2 glyph mẫu để nạp vào font
                var entryBmp = new GlyphInjectionEntry(0xE050, testOutline, 1000, 50);
                var entryAstral = new GlyphInjectionEntry(0x2B820, testOutline, 1000, 50);

                var entries = new List<GlyphInjectionEntry> { entryBmp, entryAstral };

                byte[] modBytes = TrueTypeFontWriter.InjectGlyphs(origBytes, entries);

                assert("TrueTypeFontWriter.InjectGlyphs (Generated)",
                    modBytes != null && modBytes.Length > 1000,
                    "Font sau khi nạp ký tự không tạo ra dữ liệu hợp lệ hoặc bị null");

                if (modBytes != null && modBytes.Length > 0)
                {
                    // Kiểm tra tổng Checksum toàn file: TrueType yêu cầu toàn bộ file sum phải khớp 0xB1B0AFBA
                    uint totalSum = TrueTypeFontWriter.CalculateTableChecksum(modBytes);
                    assert("TrueTypeFontWriter.CheckSumAdjustment",
                        totalSum == 0xB1B0AFBA,
                        string.Format("Tổng checksum toàn file không bằng 0xB1B0AFBA (Thực tế: 0x{0:X8})", totalSum));

                    // Đọc lại bằng OpenTypeBinaryReader để kiểm chứng round-trip
                    using (var roundTripReader = new OpenTypeBinaryReader(modBytes))
                    {
                        bool supportBmp = roundTripReader.IsCharacterSupported(0xE050);
                        bool supportAstral = roundTripReader.IsCharacterSupported(0x2B820);

                        ushort gidBmp = roundTripReader.GetGlyphIndex(0xE050);
                        ushort gidAstral = roundTripReader.GetGlyphIndex(0x2B820);

                        assert("RoundTrip.CmapDualSupport (BMP & Astral)",
                            supportBmp && supportAstral && gidBmp > 0 && gidAstral > 0,
                            string.Format("Không hỗ trợ ký tự vừa nạp: BMP(0xE050)={0}(GID={1}), Astral(0x2B820)={2}(GID={3})",
                                supportBmp, gidBmp, supportAstral, gidAstral));

                        var readOutlineBmp = roundTripReader.GetGlyphOutline(0xE050);
                        assert("RoundTrip.OutlineRetrieval (BMP)",
                            readOutlineBmp != null && readOutlineBmp.NumberOfContours == 1 && readOutlineBmp.AdvanceWidth == 1000,
                            "Outline của ký tự BMP đọc lại không đúng số contour hoặc metrics");

                        var readOutlineAstral = roundTripReader.GetGlyphOutline(0x2B820);
                        assert("RoundTrip.OutlineRetrieval (Astral Plane 2)",
                            readOutlineAstral != null && readOutlineAstral.NumberOfContours == 1 && readOutlineAstral.AdvanceWidth == 1000,
                            "Outline của ký tự Astral đọc lại không đúng số contour hoặc metrics");
                    }
                }
            }
            else
            {
                logs.Add("[SKIP] Không tìm thấy font TTF mẫu trong Windows Fonts để chạy round-trip test.");
            }

            // Test 5: Clean Non-Duplicating Modified Font Path
            string p1 = TrueTypeFontWriter.GetCleanModifiedFontPath(@"C:\Fonts\MyFont.ttf");
            string p2 = TrueTypeFontWriter.GetCleanModifiedFontPath(@"C:\Fonts\MyFont_modified.ttf");
            string p3 = TrueTypeFontWriter.GetCleanModifiedFontPath(@"C:\Fonts\MyFont_modified_modified.ttf");
            string p4 = TrueTypeFontWriter.GetCleanModifiedFontPath(@"C:\Fonts\MyFont_batch_pua.ttf");
            string p5 = TrueTypeFontWriter.GetCleanModifiedFontPath(@"C:\Fonts\MyFont_modified_batch_pua.ttf");

            bool pathCheck = (p1 == @"C:\Fonts\MyFont_modified.ttf") &&
                             (p2 == @"C:\Fonts\MyFont_modified.ttf") &&
                             (p3 == @"C:\Fonts\MyFont_modified.ttf") &&
                             (p4 == @"C:\Fonts\MyFont_modified.ttf") &&
                             (p5 == @"C:\Fonts\MyFont_modified.ttf");

            assert("TrueTypeFontWriter.GetCleanModifiedFontPath",
                pathCheck,
                string.Format("Đường dẫn file bị lặp đuôi modified: p1={0}, p2={1}, p3={2}", p1, p2, p3));

            report = string.Format("Kết quả kiểm thử Font Injection & Writer:\nĐã chạy: {0} bài test\nThành công: {1}\nThất bại: {2}\n\nChi tiết:\n{3}",
                passed + failed, passed, failed, string.Join("\n", logs));

            return failed == 0;
        }
    }
}
