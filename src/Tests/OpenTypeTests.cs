using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Tests
{
    public static class OpenTypeTests
    {
        public static bool RunAllTests(out string report)
        {
            var logs = new List<string>();
            int passed = 0;
            int failed = 0;

            Action<string, bool, string> assert = (testName, condition, details) =>
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

            // Test 1: Vector Bézier decomposition test
            var testGlyph = new RawGlyphOutline
            {
                GlyphIndex = 1,
                AdvanceWidth = 1000,
                XMin = 50,
                YMin = 50,
                XMax = 950,
                YMax = 950
            };
            // Square contour
            var contour = new List<OutlinePoint>
            {
                new OutlinePoint(100, 100, true),
                new OutlinePoint(900, 100, true),
                new OutlinePoint(900, 900, true),
                new OutlinePoint(100, 900, true)
            };
            testGlyph.Contours.Add(contour);

            string svgPath = VectorPathRenderer.BuildSvgPath(testGlyph, 1000, 800);
            assert("VectorPathRenderer.BuildSvgPath (Linear)",
                svgPath.StartsWith("M 100 700") && svgPath.Contains("L 900 700") && svgPath.Contains("Z"),
                "Chuyển đổi đường thẳng sang SVG path không đúng: " + svgPath);

            // Test 2: Quadratic Bézier curve with off-curve control points
            var curveGlyph = new RawGlyphOutline { GlyphIndex = 2, AdvanceWidth = 1000 };
            var curveContour = new List<OutlinePoint>
            {
                new OutlinePoint(100, 100, true),
                new OutlinePoint(500, 500, false), // control point
                new OutlinePoint(900, 100, true)
            };
            curveGlyph.Contours.Add(curveContour);

            string curveSvg = VectorPathRenderer.BuildSvgPath(curveGlyph, 1000, 800);
            assert("VectorPathRenderer.BuildSvgPath (Quadratic Bézier)",
                curveSvg.Contains("Q 500 300 900 700"),
                "Chuyển đổi đường cong bậc 2 sang SVG path Q không đúng: " + curveSvg);

            // Test 3: Ink Bounding Box calculation
            var ink = VectorPathRenderer.CalculateInkBounds(testGlyph);
            assert("VectorPathRenderer.CalculateInkBounds",
                ink.MinX == 100 && ink.MaxX == 900 && ink.MinY == 100 && ink.MaxY == 900 &&
                ink.Width == 800 && ink.Height == 800 && ink.CenterX == 500.0,
                "Tính toán Ink Bounding Box không chính xác");

            // Test 4: Parse Real Windows CJK Font (simsunb.ttf or simsun.ttc)
            string fontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string simsunb = Path.Combine(fontsDir, "simsunb.ttf");
            string simsun = Path.Combine(fontsDir, "simsun.ttc");
            string targetFont = File.Exists(simsunb) ? simsunb : (File.Exists(simsun) ? simsun : null);

            if (targetFont != null)
            {
                using (var reader = new OpenTypeBinaryReader(targetFont))
                {
                    assert("OpenTypeBinaryReader.Header (head & maxp)",
                        reader.Header.UnitsPerEm > 0 && reader.Header.NumGlyphs > 0,
                        string.Format("Đọc bảng head/maxp thất bại: Em={0}, Glyphs={1}", reader.Header.UnitsPerEm, reader.Header.NumGlyphs));

                    assert("OpenTypeBinaryReader.SupportedCharacters (cmap)",
                        reader.SupportedCharacterCount > 1000,
                        "Số lượng ký tự trong bảng cmap quá ít: " + reader.SupportedCharacterCount);

                    // Test 5: Format 12 Plane 2 Support (Chữ Nôm / Ext B)
                    // In simsunb.ttf, characters like U+20000, U+20001, U+215F6 (𡗶) are mapped!
                    if (targetFont.EndsWith("simsunb.ttf", StringComparison.OrdinalIgnoreCase))
                    {
                        bool hasPlane2 = reader.IsCharacterSupported(0x20000) || reader.IsCharacterSupported(0x20001);
                        ushort gid = reader.GetGlyphIndex(0x20000);
                        assert("OpenTypeBinaryReader.CmapFormat12 (Plane 2 / Ext B)",
                            hasPlane2 && gid > 0,
                            "Bảng CMap Format 12 không đọc được ký tự Plane 2 U+20000");

                        // Test 6: Glyph outline & metrics extraction
                        var outline = reader.GetGlyphOutline(0x20000);
                        assert("OpenTypeBinaryReader.GetGlyphOutline",
                            outline != null && outline.GlyphIndex > 0 && outline.AdvanceWidth > 0,
                            "Không đọc được vector glyph outline hoặc metrics");
                    }
                    else
                    {
                        // simsun.ttc BMP test (U+4E00 '一', U+5929 '天')
                        bool hasBmp = reader.IsCharacterSupported(0x4E00) && reader.IsCharacterSupported(0x5929);
                        assert("OpenTypeBinaryReader.CmapFormat4 (BMP)",
                            hasBmp,
                            "Bảng CMap Format 4 không đọc được ký tự BMP 0x4E00");
                    }
                }
            }
            else
            {
                logs.Add("[SKIP] Không tìm thấy simsunb.ttf / simsun.ttc trong C:\\Windows\\Fonts để chạy test font thực tế.");
            }

            // Test 7: IDS Parser Service - Binary decomposition
            var astBinary = IdsParserService.ParseIds("⿱天上", false);
            assert("IdsParserService.ParseIds (Binary)",
                astBinary != null && astBinary.Operator == "⿱" && astBinary.Children != null && astBinary.Children.Count == 2 && astBinary.Children[0].Char == "天" && astBinary.Children[1].Char == "上",
                "Phân tích cú pháp IDS nhị phân '⿱天上' thất bại");

            // Test 8: IDS Parser Service - Ternary composition
            var astTernary = IdsParserService.ParseIds("⿲木木木", false);
            assert("IdsParserService.ParseIds (Ternary)",
                astTernary != null && astTernary.Operator == "⿲" && astTernary.Children != null && astTernary.Children.Count == 3 && astTernary.Children[0].Char == "木" && astTernary.Children[1].Char == "木" && astTernary.Children[2].Char == "木",
                "Phân tích cú pháp IDS tam phân '⿲木木木' thất bại");

            // Test 9: IDS Parser Service - Astral Plane (Plane 2 Chữ Nôm surrogate pair)
            var astAstral = IdsParserService.ParseIds("⿱天𡗶", false);
            assert("IdsParserService.ParseIds (Astral Plane / Chữ Nôm)",
                astAstral != null && astAstral.Children != null && astAstral.Children.Count == 2 && astAstral.Children[1].Char == "𡗶",
                "Phân tích cú pháp IDS với ký tự Plane 2 '𡗶' bị cắt vụn surrogate pair");

            // Test 10: IDS Parser Service - Smart Radicals
            var astSmart = IdsParserService.ParseIds("⿰水木", true);
            assert("IdsParserService.SmartRadicals",
                astSmart != null && astSmart.Children != null && astSmart.Children.Count == 2 && astSmart.Children[0].Char == "氵",
                "Chuyển đổi Smart Radical biến thể 水 sang 氵 thất bại");

            // Test 11: IDS Layout Engine - 1000x1000 Frame & Bounding Boxes
            var layoutItems = IdsLayoutEngine.ComputeLayout(astBinary);
            assert("IdsLayoutEngine.ComputeLayout",
                layoutItems != null && layoutItems.Count == 2 &&
                layoutItems[0].TargetRect.Y < layoutItems[1].TargetRect.Y &&
                layoutItems[0].TargetRect.Width > 0 && layoutItems[1].TargetRect.Height > 0,
                "Tính toán layout 1000x1000 cho '⿱天上' thất bại");

            // Test 12: IDS Preset Service - Comprehensive CJK & Chữ Nôm presets
            var presets = IdsPresetService.Presets;
            bool hasNomPreset = presets.Exists(p => p.Category == "vietnamese_nom" && p.Name.Contains("Trời"));
            assert("IdsPresetService.SamplePresets",
                presets != null && presets.Count >= 18 && hasNomPreset,
                "Danh sách preset mẫu thiếu hoặc không đủ 18 mẫu kinh điển");

            // Test 13: IDS Glyph Renderer - Standalone SVG Vector Generation
            string svgOut = IdsGlyphRenderer.GenerateFullSvg(layoutItems, FontFamily.GenericSansSerif);
            assert("IdsGlyphRenderer.GenerateFullSvg",
                !string.IsNullOrEmpty(svgOut) && svgOut.Contains("<svg") && svgOut.Contains("viewBox=\"0 0 1000 1000\"") && svgOut.Contains("</svg>"),
                "Xuất file vector SVG độc lập thất bại");

            // Test 14: Layer 1 - ValidateAstSanity (Rejects placeholders, trailing tokens, over-nesting)
            string err1, err2, err3;
            var astPlaceholder = IdsParserService.ParseIds("⿰日□");
            bool val1 = IdsOpticalBeautifier.ValidateAstSanity("⿰日□", astPlaceholder, out err1);

            var astTrailing = IdsParserService.ParseIds("⿰日月火");
            bool val2 = IdsOpticalBeautifier.ValidateAstSanity("⿰日月火", astTrailing, out err2);

            var astValid = IdsParserService.ParseIds("⿱天上");
            bool val3 = IdsOpticalBeautifier.ValidateAstSanity("⿱天上", astValid, out err3);

            assert("IdsOpticalBeautifier.ValidateAstSanity (Layer 1)",
                !val1 && err1.Contains("chưa hoàn chỉnh") &&
                !val2 && err2.Contains("sai ngữ pháp") &&
                val3 && string.IsNullOrEmpty(err3),
                "Kiểm định ngữ pháp AST Tầng 1 thất bại");

            // Test 15: Layer 2 - ExtractOpticalMetrics (Zero-crossing Sx, Sy, Center of Mass, Skylines)
            var metricsTian = IdsOpticalBeautifier.ExtractOpticalMetrics("天", FontFamily.GenericSansSerif);
            assert("IdsOpticalBeautifier.ExtractOpticalMetrics (Layer 2)",
                metricsTian != null && metricsTian.Sx >= 1.0f && metricsTian.Sy >= 1.0f &&
                metricsTian.Cx > 0.3f && metricsTian.Cx < 0.7f &&
                metricsTian.TopSkyline != null && metricsTian.TopSkyline.Length == 64 &&
                metricsTian.BottomSkyline != null && metricsTian.BottomSkyline.Length == 64,
                "Trích xuất đặc trưng quang học Tầng 2 thất bại");

            // Test 16: Layer 3 & 4 - Split Ratio Power Law & Interlocking (⿱天上)
            var beautifyTianShang = IdsOpticalBeautifier.BeautifyThisIds("⿱天上", astValid, FontFamily.GenericSansSerif, true);
            bool hasInterlock = beautifyTianShang.ReportBulletPoints.Exists(b => b.Contains("Khớp mộng") || b.Contains("Interlock"));
            bool hasBaseAnchor = beautifyTianShang.ReportBulletPoints.Exists(b => b.Contains("Bệ đỡ ngang") || b.Contains("Base-Anchor"));
            assert("IdsOpticalBeautifier.SplitRatioAndInterlocking (Layers 3 & 4)",
                beautifyTianShang.Success && hasInterlock && hasBaseAnchor,
                string.Format("Tối ưu tỷ lệ lũy thừa và khớp mộng 2D thất bại cho '⿱天上' (Succ={0}, Interlock={1}, BaseAnchor={2}, Rep={3})",
                    beautifyTianShang.Success, hasInterlock, hasBaseAnchor, string.Join(";", beautifyTianShang.ReportBulletPoints)));

            // Test 17: Layer 5 - Safe Frame & Stroke Weight
            bool hasSafeFrame = beautifyTianShang.ReportBulletPoints.Exists(b => b.Contains("Mễ Tự Cách") || b.Contains("Safe-frame"));
            assert("IdsOpticalBeautifier.EqualizeAndNormalize (Layer 5)",
                hasSafeFrame && beautifyTianShang.BeautifiedTransforms.Count >= 2,
                "Chuẩn hóa khung an toàn Mễ Tự Cách và cân bằng nét Tầng 5 thất bại");

            // Test 18: All 12 IDC Operators Coverage (Ternary ⿲, ⿳ and Enclosure ⿴, ⿺)
            var astTernaryH = IdsParserService.ParseIds("⿲木木木");
            var resTernaryH = IdsOpticalBeautifier.BeautifyThisIds("⿲木木木", astTernaryH, FontFamily.GenericSansSerif, true);

            var astTernaryV = IdsParserService.ParseIds("⿳亠口月");
            var resTernaryV = IdsOpticalBeautifier.BeautifyThisIds("⿳亠口月", astTernaryV, FontFamily.GenericSansSerif, true);

            var astEnclosure = IdsParserService.ParseIds("⿴囗玉");
            var resEnclosure = IdsOpticalBeautifier.BeautifyThisIds("⿴囗玉", astEnclosure, FontFamily.GenericSansSerif, true);

            var astWalking = IdsParserService.ParseIds("⿺辶元");
            var resWalking = IdsOpticalBeautifier.BeautifyThisIds("⿺辶元", astWalking, FontFamily.GenericSansSerif, true);

            bool okTernary = resTernaryH.Success && resTernaryH.ReportBulletPoints.Exists(b => b.Contains("3 cột"));
            bool okTernaryV = resTernaryV.Success && resTernaryV.ReportBulletPoints.Exists(b => b.Contains("3 tầng"));
            bool okEnclosure = resEnclosure.Success && resEnclosure.ReportBulletPoints.Exists(b => b.Contains("cấu trúc bao"));
            bool okWalking = resWalking.Success && resWalking.ReportBulletPoints.Exists(b => b.Contains("cấu trúc bao"));

            assert("IdsOpticalBeautifier.AllOperatorsCoverage",
                okTernary && okTernaryV && okEnclosure && okWalking,
                "Làm đẹp các toán tử 3 cột/tầng hoặc toán tử bao bọc (⿲, ⿳, ⿴, ⿺) thất bại");

            report = string.Format("Kết quả kiểm thử OpenType Engine (Pha 2):\nĐã chạy: {0} bài test\nThành công: {1}\nThất bại: {2}\n\nChi tiết:\n{3}",
                passed + failed, passed, failed, string.Join("\n", logs));

            return failed == 0;
        }
    }
}
