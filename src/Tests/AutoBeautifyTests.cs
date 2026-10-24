using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CJKPlayground.Models;
using CJKPlayground.Services;
using CJKPlayground.Services.Legacy;

namespace CJKPlayground.Tests
{
    public static class AutoBeautifyTests
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

            var cfg = new SolverConfig();
            var font = FontFamily.GenericSansSerif;

            // =========================================================================
            // TEST 1: Benchmark chất lượng (D1)
            // =========================================================================
            bool test1Ok = true;
            string test1Err = "";
            try
            {
                var rep = IdsQualityHarness.RunBenchmark(font, false, null);
                if (rep.NewTest.MedianChamfer >= 24.7f)
                {
                    test1Ok = false;
                    test1Err = string.Format("Test Chamfer Median {0:F1} chưa đánh bại B0 (24.7)", rep.NewTest.MedianChamfer);
                }
                else if (rep.NewTest.P90Chamfer >= 56.9f)
                {
                    test1Ok = false;
                    test1Err = string.Format("Test Chamfer P90 {0:F1} chưa đánh bại Legacy (56.9)", rep.NewTest.P90Chamfer);
                }
                else if (rep.NewTest.MedianBBoxError >= 40.0f)
                {
                    test1Ok = false;
                    test1Err = string.Format("Test BBox Error Median {0:F1} vượt ngưỡng 40.0", rep.NewTest.MedianBBoxError);
                }
            }
            catch (Exception ex)
            {
                test1Ok = false;
                test1Err = "Ngoại lệ khi chạy Benchmark test: " + ex.Message;
            }
            assert("AutoBeautify.Test1_QualityBenchmark", test1Ok, test1Err);

            // =========================================================================
            // TEST 2: Bất biến thật trên raster
            // =========================================================================
            bool test2Ok = true;
            string test2Err = "";
            string[] test2Cases = new string[] { "林", "品", "家", "花", "想", "超", "问", "国" };
            string[] test2Ids   = new string[] { "⿰木木", "⿱口⿰口口", "⿱宀豕", "⿱艹化", "⿱相心", "⿺走召", "⿵門口", "⿴囗玉" };

            for (int i = 0; i < test2Cases.Length; i++)
            {
                string ids = test2Ids[i];
                var ast = IdsParserService.ParseIds(ids);
                var res = IdsOpticalBeautifier.BeautifyThisIds(ids, ast, font, null, isVietnamese: true);
                if (!res.Success)
                {
                    test2Ok = false;
                    test2Err = "Làm đẹp thất bại cho " + ids;
                    break;
                }

                string failReason;
                if (!VerifyRasterInvariants(ids, ast, res.BeautifiedTransforms, font, cfg, out failReason))
                {
                    test2Ok = false;
                    test2Err = failReason;
                    break;
                }
            }
            assert("AutoBeautify.Test2_RealRasterInvariants", test2Ok, test2Err);

            // =========================================================================
            // TEST 3: Khớp render (Preview GDI+ <-> SVG <-> Solver raster <= 1px)
            // =========================================================================
            bool test3Ok = true;
            string test3Err = "";
            try
            {
                var astMatch = IdsParserService.ParseIds("⿰日月");
                var resMatch = IdsOpticalBeautifier.BeautifyThisIds("⿰日月", astMatch, font, isVietnamese: true);
                if (!resMatch.Success)
                {
                    test3Ok = false;
                    test3Err = "Làm đẹp thất bại cho '⿰日月'";
                }
                else
                {
                    Action<IdsNode> apply = null;
                    apply = n =>
                    {
                        if (n == null) return;
                        NodeCustomTransform t;
                        if (resMatch.BeautifiedTransforms.TryGetValue(n.Id, out t)) n.Transform = t;
                        if (n.Children != null) { foreach (var c in n.Children) apply(c); }
                    };
                    apply(astMatch);

                    var items = IdsLayoutEngine.ComputeLayout(astMatch, new RectangleF(0, 0, 1000f, 1000f));
                    if (items.Count < 2)
                    {
                        test3Ok = false;
                        test3Err = "ComputeLayout không trả về đủ 2 phần tử render";
                    }
                    else
                    {
                        string svg = IdsGlyphRenderer.GenerateFullSvg(items, font, 1000, 1000);
                        if (string.IsNullOrEmpty(svg) || !svg.Contains("<svg") || !svg.Contains("viewBox=\"0 0 1000 1000\""))
                        {
                            test3Ok = false;
                            test3Err = "SVG sinh ra không có thẻ hợp lệ hoặc sai viewBox";
                        }
                        else if (!svg.Contains("<path"))
                        {
                            test3Ok = false;
                            test3Err = "SVG không chứa thẻ vector <path>";
                        }

                        // So sánh pixel raster giữa GDI+ preview và solver
                        using (var bmpPreview = new Bitmap(256, 256))
                        using (var g = Graphics.FromImage(bmpPreview))
                        {
                            g.Clear(Color.White);
                            IdsGlyphRenderer.RenderStage(g, new Rectangle(0, 0, 256, 256), items, null, IdsCanvasGridType.None, false, IdsColorTheme.Classic, 1.0f, font);

                            // Kiểm tra mực tồn tại và nằm gọn trong biên
                            int inkPixels = 0;
                            for (int py = 0; py < 256; py++)
                            {
                                for (int px = 0; px < 256; px++)
                                {
                                    Color c = bmpPreview.GetPixel(px, py);
                                    if (c.R < 150) inkPixels++;
                                }
                            }

                            if (inkPixels < 500)
                            {
                                test3Ok = false;
                                test3Err = "Mực render GDI+ quá mờ hoặc rỗng (inkPixels=" + inkPixels + ")";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                test3Ok = false;
                test3Err = "Ngoại lệ khi thực hiện Render match test: " + ex.Message;
            }
            assert("AutoBeautify.Test3_RenderMatch", test3Ok, test3Err);

            // =========================================================================
            // TEST 4: Idempotence test (Lũy đẳng 10 lần liên tiếp)
            // =========================================================================
            bool test4Ok = true;
            string test4Err = "";
            string[] idempCases = new string[] { "⿱天上", "⿰日月", "⿰木木" };

            foreach (var ids in idempCases)
            {
                var ast = IdsParserService.ParseIds(ids);
                var resFirst = IdsOpticalBeautifier.BeautifyThisIds(ids, ast, font, null, isVietnamese: true);
                if (!resFirst.Success)
                {
                    test4Ok = false;
                    test4Err = "Lần chạy đầu tiên thất bại cho " + ids;
                    break;
                }

                var currentTransforms = resFirst.BeautifiedTransforms;
                for (int iter = 2; iter <= 10; iter++)
                {
                    var resNext = IdsOpticalBeautifier.BeautifyThisIds(ids, ast, font, currentTransforms, isVietnamese: true);
                    if (!resNext.Success)
                    {
                        test4Ok = false;
                        test4Err = string.Format("Lần lặp {0} thất bại cho {1}", iter, ids);
                        break;
                    }

                    if (!resNext.IsAlreadyOptimal)
                    {
                        test4Ok = false;
                        test4Err = string.Format("Lần lặp {0} không kích hoạt cờ IsAlreadyOptimal cho {1}", iter, ids);
                        break;
                    }

                    foreach (var kvp in resFirst.BeautifiedTransforms)
                    {
                        NodeCustomTransform nextT;
                        if (resNext.BeautifiedTransforms.TryGetValue(kvp.Key, out nextT))
                        {
                            var t0 = kvp.Value;
                            float dScale = Math.Max(Math.Abs(t0.ScaleX - nextT.ScaleX), Math.Abs(t0.ScaleY - nextT.ScaleY));
                            float dOff = Math.Max(Math.Abs(t0.OffsetX - nextT.OffsetX), Math.Abs(t0.OffsetY - nextT.OffsetY));
                            float dDil = Math.Max(Math.Abs(t0.StrokeDilationX - nextT.StrokeDilationX), Math.Abs(t0.StrokeDilationY - nextT.StrokeDilationY));

                            if (dScale > 0.001f || dOff > 0.5f || dDil > 0.05f)
                            {
                                test4Ok = false;
                                test4Err = string.Format("Trôi dạt thông số ở lần lặp {0} cho {1}: dScale={2}, dOff={3}, dDil={4}",
                                    iter, ids, dScale, dOff, dDil);
                                break;
                            }
                        }
                    }

                    if (!test4Ok) break;
                    currentTransforms = resNext.BeautifiedTransforms;
                }

                if (!test4Ok) break;
            }
            assert("AutoBeautify.Test4_Idempotence", test4Ok, test4Err);

            // =========================================================================
            // TEST 5: Held-out set & Ablation test
            // =========================================================================
            bool test5Ok = true;
            string test5Err = "";
            try
            {
                // 1. Ngoại suy trên ký tự ngoài bảng hạt giống (Ext-A, Ext-B, PUA)
                string[] heldOutCases = new string[]
                {
                    "⿰㐀㐁",
                    "⿱㐀㐁",
                    "⿰\uD842\uDFB7\uD845\uDDF6",
                    "⿱\uD845\uDDF6\uD842\uDFB7",
                    "⿴㐀㐁"
                };

                foreach (var ids in heldOutCases)
                {
                    var ast = IdsParserService.ParseIds(ids);
                    var res = IdsOpticalBeautifier.BeautifyThisIds(ids, ast, font, isVietnamese: true);
                    if (!res.Success)
                    {
                        test5Ok = false;
                        test5Err = "Thất bại ngoại suy cho " + ids + ": " + res.ErrorMessage;
                        break;
                    }

                    string failReason;
                    if (!VerifyRasterInvariants(ids, ast, res.BeautifiedTransforms, font, cfg, out failReason))
                    {
                        test5Ok = false;
                        test5Err = "Vi phạm bất biến raster trên chữ ngoại suy " + ids + ": " + failReason;
                        break;
                    }
                }

                // 2. Triệt tiêu toàn bộ hạt giống (Ablation: ExemplarSeeds rỗng)
                if (test5Ok)
                {
                    ExemplarSeeds.ClearForAblation();
                    string[] ablationCases = new string[] { "⿰日月", "⿱天上", "⿲木木木", "⿴囗玉" };
                    foreach (var ids in ablationCases)
                    {
                        var ast = IdsParserService.ParseIds(ids);
                        var res = IdsOpticalBeautifier.BeautifyThisIds(ids, ast, font, isVietnamese: true);
                        if (!res.Success)
                        {
                            test5Ok = false;
                            test5Err = "Thất bại ở chế độ Ablation (pool rỗng) cho " + ids;
                            break;
                        }

                        bool mentionsGeneral = res.ReportBulletPoints.Exists(b =>
                            b.Contains("Chế độ tổng quát") || b.Contains("General mode") || b.Contains("Tham chiếu:"));
                        if (!mentionsGeneral)
                        {
                            test5Ok = false;
                            test5Err = "Báo cáo không phản ánh chế độ tổng quát khi triệt tiêu hạt giống cho " + ids;
                            break;
                        }

                        string failReason;
                        if (!VerifyRasterInvariants(ids, ast, res.BeautifiedTransforms, font, cfg, out failReason))
                        {
                            test5Ok = false;
                            test5Err = "Vi phạm bất biến raster ở chế độ Ablation cho " + ids + ": " + failReason;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                test5Ok = false;
                test5Err = "Ngoại lệ trong Held-out / Ablation test: " + ex.Message;
            }
            finally
            {
                ExemplarSeeds.ResetToDefaults();
            }
            assert("AutoBeautify.Test5_HeldOutAndAblation", test5Ok && ExemplarSeeds.AllSeeds.Count > 0, test5Err);

            // =========================================================================
            // TEST 6: Mutation tests (Cố ý phá hoại -> Assert bộ kiểm tra BẬT ĐỎ)
            // =========================================================================
            bool test6Ok = true;
            string test6Err = "";
            try
            {
                string testIds = "⿱天上";
                var ast = IdsParserService.ParseIds(testIds);
                var res = IdsOpticalBeautifier.BeautifyThisIds(testIds, ast, font, null, isVietnamese: true);

                // Đột biến 1: Cố ý thổi phồng độ dày nét quá mức (Blowout Dilation)
                var mutatedTransforms1 = CloneTransforms(res.BeautifiedTransforms);
                foreach (var k in mutatedTransforms1.Keys.ToList())
                {
                    mutatedTransforms1[k].StrokeDilationX = 35.0f;
                    mutatedTransforms1[k].StrokeDilationY = 35.0f;
                }
                string reason1;
                bool mut1Caught = !VerifyRasterInvariants(testIds, ast, mutatedTransforms1, font, cfg, out reason1);
                if (!mut1Caught)
                {
                    test6Ok = false;
                    test6Err = "Đột biến 1 (Stroke Dilation 35pt) KHÔNG bị bắt (vẫn báo PASS)!";
                }

                // Đột biến 2: Cố ý phá hỏng SplitRatio (SplitRatio = 0.95 cực đoan)
                if (test6Ok)
                {
                    var mutatedTransforms2 = CloneTransforms(res.BeautifiedTransforms);
                    foreach (var k in mutatedTransforms2.Keys.ToList())
                    {
                        if (mutatedTransforms2[k].SplitRatio.HasValue) mutatedTransforms2[k].SplitRatio = 0.95f;
                    }
                    string reason2;
                    bool mut2Caught = !VerifyRasterInvariants(testIds, ast, mutatedTransforms2, font, cfg, out reason2);
                    if (!mut2Caught)
                    {
                        test6Ok = false;
                        test6Err = "Đột biến 2 (SplitRatio = 0.95) KHÔNG bị bắt (vẫn báo PASS)!";
                    }
                }

                // Đột biến 3: Cố ý đẩy 2 nhánh đâm sầm vào nhau (Collision Sabotage)
                if (test6Ok)
                {
                    string collIds = "⿰日月";
                    var astColl = IdsParserService.ParseIds(collIds);
                    var resColl = IdsOpticalBeautifier.BeautifyThisIds(collIds, astColl, font, null, isVietnamese: true);
                    var mutatedTransforms3 = CloneTransforms(resColl.BeautifiedTransforms);
                    string targetLeafId = (astColl.Children != null && astColl.Children.Count > 1) ? astColl.Children[1].Id : "";
                    if (!string.IsNullOrEmpty(targetLeafId) && mutatedTransforms3.ContainsKey(targetLeafId))
                    {
                        mutatedTransforms3[targetLeafId].OffsetX = -280; // Đâm sâu vào chữ 日
                    }
                    string reason3;
                    bool mut3Caught = !VerifyRasterInvariants(collIds, astColl, mutatedTransforms3, font, cfg, out reason3);
                    if (!mut3Caught)
                    {
                        test6Ok = false;
                        test6Err = "Đột biến 3 (Đâm va nét mực OffsetX=-280) KHÔNG bị bắt (vẫn báo PASS)!";
                    }
                }

                // Đột biến 4: Cố ý làm sập lỗ trắng (Counter-Space Guard Failure)
                if (test6Ok)
                {
                    // 1. Kiểm tra sập lỗ trên raster kiểm thử thực tế khi bù nét quá đà ở scale nhỏ
                    using (var bmpSabotaged = IdsOpticalBeautifier.RenderTestDilatedGlyph("日", 0.15f, 0.15f, 30f, 30f))
                    {
                        int holesSabotaged = IdsOpticalBeautifier.CountHolesOnRaster(bmpSabotaged);
                        if (holesSabotaged >= 2)
                        {
                            test6Ok = false;
                            test6Err = "Đột biến 4 thất bại: Nét bù 30pt không làm sập lỗ trên raster thử nghiệm";
                        }
                    }

                    // 2. Kiểm tra VerifyRasterInvariants bắt được vi phạm sập lỗ khi bị phá hoại
                    if (test6Ok)
                    {
                        string testIdsHoles = "⿰日口";
                        var astHoles = IdsParserService.ParseIds(testIdsHoles);
                        var resHoles = IdsOpticalBeautifier.BeautifyThisIds(testIdsHoles, astHoles, font, null, isVietnamese: true);
                        var mutatedTransforms4 = CloneTransforms(resHoles.BeautifiedTransforms);
                        foreach (var k in mutatedTransforms4.Keys.ToList())
                        {
                            mutatedTransforms4[k].ScaleX = 0.15f;
                            mutatedTransforms4[k].ScaleY = 0.15f;
                            mutatedTransforms4[k].StrokeDilationX = 30.0f;
                            mutatedTransforms4[k].StrokeDilationY = 30.0f;
                        }
                        string reason4;
                        bool mut4Caught = !VerifyRasterInvariants(testIdsHoles, astHoles, mutatedTransforms4, font, cfg, out reason4);
                        if (!mut4Caught)
                        {
                            test6Ok = false;
                            test6Err = "Đột biến 4 (Sập lỗ trắng trên VerifyRasterInvariants) KHÔNG bị bắt!";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                test6Ok = false;
                test6Err = "Ngoại lệ khi chạy Mutation test: " + ex.Message;
            }
            assert("AutoBeautify.Test6_MutationTests", test6Ok, test6Err);

            // =========================================================================
            // TEST 7: Extended Anti-Hardcoding Lint
            // =========================================================================
            bool test7Ok = true;
            string test7Err = "";
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] searchRoots = new string[]
                {
                    Path.Combine(baseDir, @"..\..\src"),
                    Path.Combine(baseDir, @"src"),
                    @"c:\Users\Admin\Desktop\CJKPlayground\src"
                };
                string srcRoot = searchRoots.FirstOrDefault(Directory.Exists);

                if (string.IsNullOrEmpty(srcRoot))
                {
                    test7Ok = false;
                    test7Err = "Không tìm thấy thư mục mã nguồn src để quét lint!";
                }
                else
                {
                    string[] targetFiles = new string[]
                    {
                        Path.Combine(srcRoot, @"Services\IdsOpticalBeautifier.cs"),
                        Path.Combine(srcRoot, @"Services\IdsGlyphRenderer.cs"),
                        Path.Combine(srcRoot, @"Services\IdsLayoutEngine.cs"),
                        Path.Combine(srcRoot, @"Forms\IdsComposerControl.cs"),
                        Path.Combine(srcRoot, @"Services\IdsParserService.cs")
                    };

                    foreach (var file in targetFiles)
                    {
                        if (!File.Exists(file)) continue;
                        var lines = File.ReadAllLines(file);
                        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
                        {
                            string line = lines[lineIdx].Trim();
                            if (line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("*")) continue;

                            // 1. Cấm switch(cp) hoặc switch(ch)
                            if (Regex.IsMatch(line, @"switch\s*\(\s*(cp|codepoint|codePoint|ch)\s*\)"))
                            {
                                test7Ok = false;
                                test7Err = string.Format("{0}:{1} => switch(cp/ch): {2}", Path.GetFileName(file), lineIdx + 1, line);
                                break;
                            }

                            // 2. Cấm so sánh cứng mã ký tự CJK cụ thể (U+4E00..U+9FFF)
                            if (Regex.IsMatch(line, @"(Char|char|glyph|ch)\s*==\s*""[\u4E00-\u9FFF]"""))
                            {
                                test7Ok = false;
                                test7Err = string.Format("{0}:{1} => Hardcoded CJK compare: {2}", Path.GetFileName(file), lineIdx + 1, line);
                                break;
                            }
                        }
                        if (!test7Ok) break;
                    }

                    // 3. Quét kiểm tra mọi trường của SolverConfig phải được tham chiếu thực sự
                    if (test7Ok)
                    {
                        string beautifierCode = File.ReadAllText(Path.Combine(srcRoot, @"Services\IdsOpticalBeautifier.cs"));
                        var configFields = typeof(SolverConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
                        foreach (var f in configFields)
                        {
                            if (!beautifierCode.Contains(f.Name))
                            {
                                test7Ok = false;
                                test7Err = string.Format("Trường SolverConfig.{0} không được tham chiếu trong IdsOpticalBeautifier.cs (Bug B1 chưa dọn dẹp)!", f.Name);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                test7Ok = false;
                test7Err = "Ngoại lệ khi quét extended lint: " + ex.Message;
            }
            assert("AutoBeautify.Test7_ExtendedLint", test7Ok, test7Err);

            // =========================================================================
            // TEST 8: Phát hiện chuỗi báo cáo "nói dối" (Report Veracity)
            // =========================================================================
            bool test8Ok = true;
            string test8Err = "";
            try
            {
                // Chữ có lỗ trắng: 日 (2 lỗ), 口 (1 lỗ)
                var astWithHoles = IdsParserService.ParseIds("⿰日口");
                var resWithHoles = IdsOpticalBeautifier.BeautifyThisIds("⿰日口", astWithHoles, font, isVietnamese: true);

                // Chữ không có lỗ trắng: 人 (0 lỗ), 木 (0 lỗ)
                var astNoHoles = IdsParserService.ParseIds("⿰人木");
                var resNoHoles = IdsOpticalBeautifier.BeautifyThisIds("⿰人木", astNoHoles, font, isVietnamese: true);

                // 1. Chữ có lỗ: Chuỗi "Bảo toàn N lỗ trắng" PHẢI xuất hiện và N phải đúng bằng số lỗ đo thực tế
                bool hasHoleReport = resWithHoles.ReportBulletPoints.Exists(b => b.Contains("lỗ trắng nội tại") || b.Contains("internal holes preserved"));
                if (!hasHoleReport)
                {
                    test8Ok = false;
                    test8Err = "Chữ có lỗ '⿰日口' không có báo cáo bảo toàn lỗ trắng!";
                }

                // 2. Chữ không có lỗ: KHÔNG được xuất hiện chuỗi "Bảo toàn 0 lỗ trắng"
                if (test8Ok)
                {
                    bool falseHoleReport = resNoHoles.ReportBulletPoints.Exists(b => b.Contains("Bảo toàn 0 lỗ") || b.Contains("0 internal holes preserved"));
                    if (falseHoleReport)
                    {
                        test8Ok = false;
                        test8Err = "Chữ không có lỗ '⿰人木' lại xuất hiện báo cáo 'Bảo toàn 0 lỗ trắng'!";
                    }
                }

                // 3. Cấm các chuỗi tự khen không đo đạc
                if (test8Ok)
                {
                    var allBulletLines = resWithHoles.ReportBulletPoints.Concat(resNoHoles.ReportBulletPoints);
                    foreach (var line in allBulletLines)
                    {
                        if (line.Contains("Điểm thẩm mỹ: Đạt chuẩn") || line.Contains("cân đối hoàn hảo"))
                        {
                            test8Ok = false;
                            test8Err = "Phát hiện chuỗi tự khen không đo đạc trong báo cáo: " + line;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                test8Ok = false;
                test8Err = "Ngoại lệ khi kiểm tra tính trung thực báo cáo: " + ex.Message;
            }
            assert("AutoBeautify.Test8_ReportVeracity", test8Ok, test8Err);

            // =========================================================================
            // TEST 9: Beautiful IDS v2.0 Full Architectural Suite (TC-01 .. TC-16)
            // =========================================================================
            bool test9Ok = true;
            string test9Err = "";
            try
            {
                // TC-01: Nạp chỉ mục 98.000 chữ All HAN IDS.TXT
                HanIdsCorpusIndex.EnsureInitialized();
                if (HanIdsCorpusIndex.TotalIndexedCharacters < 90000)
                {
                    test9Ok = false;
                    test9Err = "TC-01: Chỉ mục All HAN IDS.TXT nạp thiếu dữ liệu (" + HanIdsCorpusIndex.TotalIndexedCharacters + " < 90000)";
                }

                // TC-02: Giải nén DAG Macro {58} -> {39} và phát hiện chu trình đệ quy ERR_MACRO_CYCLE
                if (test9Ok)
                {
                    var cycleMap = new Dictionary<int, string>
                    {
                        { 1, "⿰{2}一" },
                        { 2, "⿱{1}丨" }
                    };
                    string expCycle, errCycle;
                    bool cycleOk = !HanIdsCorpusIndex.TryExpandMacroString("{1}", cycleMap, out expCycle, out errCycle) && errCycle == "ERR_MACRO_CYCLE";
                    if (!cycleOk)
                    {
                        test9Ok = false;
                        test9Err = "TC-02: Không phát hiện được chu trình đệ quy ERR_MACRO_CYCLE trong bảng Macro!";
                    }
                }

                // TC-03: Parser Explicit-Stack chặn độ sâu > 32 và chặn surrogate lẻ
                if (test9Ok)
                {
                    string deepIds = new string('⿰', 35) + new string('木', 36);
                    string deepErr;
                    int deepPos;
                    var deepAst = IdsParserService.ParseIdsStrict(deepIds, out deepErr, out deepPos);
                    if (deepAst != null || string.IsNullOrEmpty(deepErr) || !deepErr.Contains("ERR_MAX_DEPTH_EXCEEDED"))
                    {
                        test9Ok = false;
                        test9Err = "TC-03: Parser không chặn cây vượt quá MAX_AST_DEPTH = 32!";
                    }

                    string surrErr;
                    int surrPos;
                    var surrAst = IdsParserService.ParseIdsStrict("⿰\uD842木", out surrErr, out surrPos);
                    if (surrAst != null || string.IsNullOrEmpty(surrErr) || !surrErr.Contains("ERR_INVALID_SURROGATE"))
                    {
                        test9Ok = false;
                        test9Err = "TC-03b: Parser không chặn chuỗi UTF-16 surrogate lẻ!";
                    }
                }

                // TC-04 & TC-05 & TC-06: 5 Quy tắc chặn biến hình bộ thủ (Reduplication, Lexical Exception, Escape Lock '!')
                if (test9Ok)
                {
                    // TC-04: ⿰人人 (从) giữ nguyên 人, không biến thành ⿰亻人
                    var astCong = IdsParserService.ParseIds("⿰人人", true);
                    if (astCong.Children[0].Char != "人")
                    {
                        test9Ok = false;
                        test9Err = "TC-04: Chữ lặp '⿰人人' (从) bị biến hình sai thành '" + astCong.Children[0].Char + "'";
                    }

                    // TC-05: ⿱巛火 (災) và ⿱林火 (焚) giữ nguyên 火, không biến thành 灬
                    var astZai = IdsParserService.ParseIds("⿱巛火", true);
                    var astFen = IdsParserService.ParseIds("⿱林火", true);
                    if (astZai.Children[1].Char != "火" || astFen.Children[1].Char != "火")
                    {
                        test9Ok = false;
                        test9Err = "TC-05: Ngoại lệ từ vựng '⿱巛火' (災) hoặc '⿱林火' (焚) bị biến hình sai thành 灬";
                    }

                    // TC-06: Khóa thoát nghĩa đen '!' (⿰!水木 giữ nguyên 水)
                    var astLocked = IdsParserService.ParseIds("⿰!水木", true);
                    if (astLocked.Children[0].Char != "水" || !astLocked.Children[0].IsLiteralLocked)
                    {
                        test9Ok = false;
                        test9Err = "TC-06: Khóa thoát nghĩa đen '⿰!水木' không giữ nguyên '水'";
                    }
                }

                // TC-07 & TC-08: Tra ngược Sub-IDS (⿱一木 -> 未/末) và Kẹp đàn hồi nét ngang '一' trong '⿰亻⿱一木'
                if (test9Ok)
                {
                    string nestedCustom = "⿰亻⿱一木";
                    var astNested = IdsParserService.ParseIds(nestedCustom, true);
                    var resNested = IdsOpticalBeautifier.BeautifyThisIds(nestedCustom, astNested, font, null, true);
                    if (!resNested.Success)
                    {
                        test9Ok = false;
                        test9Err = "TC-07: Làm đẹp công thức lồng '⿰亻⿱一木' thất bại: " + resNested.ErrorMessage;
                    }
                    else
                    {
                        // Kiểm tra nhánh con phải (⿱一木) đã được giải SplitRatio riêng biệt (< 0.42 cho nét 一)
                        var rightOp = astNested.Children[1];
                        if (rightOp.ResolvedCodePoint <= 0)
                        {
                            test9Ok = false;
                            test9Err = "TC-07: Nhánh con '⿱一木' không được tra ngược thành chữ đã biết trong kho 98k!";
                        }
                        else if (!resNested.BeautifiedTransforms.ContainsKey(rightOp.Id) ||
                                 !resNested.BeautifiedTransforms[rightOp.Id].SplitRatio.HasValue ||
                                 resNested.BeautifiedTransforms[rightOp.Id].SplitRatio.Value > 0.42f)
                        {
                            test9Ok = false;
                            test9Err = "TC-08: Nét ngang '一' trong nhánh con '⿱一木' không được thu gọn tỷ lệ hợp lý!";
                        }
                    }
                }

                // TC-09: Toán tử chồng khít '⿻' (巫 ⿻工从, 東 ⿻木日) khóa đồng tâm quang học
                if (test9Ok)
                {
                    var astOverlap = IdsParserService.ParseIds("⿻木日", true);
                    var resOverlap = IdsOpticalBeautifier.BeautifyThisIds("⿻木日", astOverlap, font, null, true);
                    if (!resOverlap.Success || !resOverlap.ReportBulletPoints.Exists(b => b.Contains("⿻")))
                    {
                        test9Ok = false;
                        test9Err = "TC-09: Toán tử chồng khít '⿻木日' xử lý thất bại!";
                    }
                }

                // TC-10: Kiểm định 3 trường hợp thực tế của người dùng (⿲木壴寸, ⿰日月, ⿰女⿱天土)
                if (test9Ok)
                {
                    // 1. ⿲木壴寸 (Chữ Thụ 樹)
                    var astShu = IdsParserService.ParseIds("⿲木壴寸", true);
                    var resShu = IdsOpticalBeautifier.BeautifyThisIds("⿲木壴寸", astShu, font, null, true);
                    ApplyTransformsToTree(astShu, resShu.BeautifiedTransforms);
                    var itemsShu = IdsLayoutEngine.ComputeLayout(astShu);
                    string repShu = resShu.GetFormattedReport();

                    if (!repShu.Contains("Toán tử ⿲ (3 cột)") || !repShu.Contains("U+6A39"))
                    {
                        test9Ok = false;
                        test9Err = "TC-10a (⿲木壴寸): Báo cáo không có tỷ lệ 3 cột hoặc không tìm thấy mẫu chuẩn 樹 U+6A39! Report:\n" + repShu;
                    }
                    else if (itemsShu.Count < 3 || itemsShu.Any(it => it.TargetRect.Height < 700f))
                    {
                        test9Ok = false;
                        test9Err = "TC-10a (⿲木壴寸): Các cột 木/壴/寸 bị co lùn chiều cao (< 700 units)!";
                    }

                    // 2. ⿰日月 (Chữ Minh 明)
                    if (test9Ok)
                    {
                        var astMing = IdsParserService.ParseIds("⿰日月", true);
                        var resMing = IdsOpticalBeautifier.BeautifyThisIds("⿰日月", astMing, font, null, true);
                        ApplyTransformsToTree(astMing, resMing.BeautifiedTransforms);
                        var itemsMing = IdsLayoutEngine.ComputeLayout(astMing);
                        string repMing = resMing.GetFormattedReport();

                        if (!repMing.Contains("U+660E") || repMing.Contains("Kéo lồng"))
                        {
                            test9Ok = false;
                            test9Err = "TC-10b (⿰日月): Không khớp 明 U+660E hoặc báo cáo sai kéo lồng hai vách phẳng! Report:\n" + repMing;
                        }
                        else if (itemsMing.Count < 2 || itemsMing[0].TargetRect.Height >= itemsMing[1].TargetRect.Height * 0.90f)
                        {
                            test9Ok = false;
                            test9Err = "TC-10b (⿰日月): Bộ 日 bên trái không được thu gọn chiều cao theo chuẩn Nhật biên thượng đề!";
                        }
                    }

                    // 3. ⿰女⿱天土 (Chữ Mẹ tự chế)
                    if (test9Ok)
                    {
                        var astMe = IdsParserService.ParseIds("⿰女⿱天土", true);
                        var resMe = IdsOpticalBeautifier.BeautifyThisIds("⿰女⿱天土", astMe, font, null, true);
                        ApplyTransformsToTree(astMe, resMe.BeautifiedTransforms);
                        var itemsMe = IdsLayoutEngine.ComputeLayout(astMe);
                        string repMe = resMe.GetFormattedReport();

                        if (repMe.Contains("≈\uD844\uDE55") || repMe.Contains("≈□") || repMe.Contains("U+4EF5"))
                        {
                            test9Ok = false;
                            test9Err = "TC-10c (⿰女⿱天土): Báo cáo còn chứa ký tự lỗi ≈□ hoặc lấy nhầm chữ bộ 亻 (U+4EF5)! Report:\n" + repMe;
                        }
                        else if (!resMe.BeautifiedTransforms[astMe.Id].SplitRatio.HasValue || resMe.BeautifiedTransforms[astMe.Id].SplitRatio.Value > 0.42f)
                        {
                            test9Ok = false;
                            test9Err = "TC-10c (⿰女⿱天土): Tỷ lệ bộ 女 bên trái quá rộng (> 42%)!";
                        }
                        else if (itemsMe.Count < 3 || itemsMe[0].TargetRect.Height < 700f)
                        {
                            test9Ok = false;
                            test9Err = "TC-10c (⿰女⿱天土): Bộ 女 bên trái bị co lùn chiều cao (< 700 units)!";
                        }
                    }

                    // Xuất ảnh trực quan 3 trường hợp để kiểm chứng mắt thường
                    RenderThreeUserCasesVerification(font);
                }
            }
            catch (Exception ex)
            {
                test9Ok = false;
                test9Err = "Ngoại lệ trong Test9_BeautifulIdsV2_FullSuite: " + ex.Message;
            }
            assert("AutoBeautify.Test9_BeautifulIdsV2_FullSuite", test9Ok, test9Err);

            // =========================================================================
            // TỔNG HỢP KẾT QUẢ BỘ TEST
            // =========================================================================
            report = string.Format(
                "Kết quả kiểm thử Bộ máy Ghép chữ IDS & Auto-Beautify (Win32 Native):\n" +
                "Đã chạy: {0} bài test bắt buộc\nThành công: {1}\nThất bại: {2}\n\nChi tiết:\n{3}",
                passed + failed, passed, failed, string.Join("\n", logs));

            return failed == 0;
        }

        // =============================================================================
        // CÁC HÀM TIỆN ÍCH KIỂM THỬ BẤT BIẾN RASTER (Verification Suite)
        // =============================================================================
        private static bool VerifyRasterInvariants(
            string ids,
            IdsNode root,
            Dictionary<string, NodeCustomTransform> transforms,
            FontFamily font,
            SolverConfig cfg,
            out string failReason)
        {
            failReason = "";
            var glyphSource = new GdiGlyphSource(font);
            float txFont, tyFont;
            glyphSource.GetFontStrokeThickness(out txFont, out tyFont);

            // 0. Kiểm tra tỷ lệ phân chia không bị méo mó cực đoan
            foreach (var kvp in transforms)
            {
                var t = kvp.Value;
                if (t.SplitRatio.HasValue)
                {
                    if (t.SplitRatio.Value < cfg.SplitMin - 0.05f || t.SplitRatio.Value > cfg.SplitMax + 0.05f)
                    {
                        failReason = string.Format("Tỷ lệ phân chia cực đoan ({0:F2}) vi phạm giới hạn [{1:F2}..{2:F2}]",
                            t.SplitRatio.Value, cfg.SplitMin - 0.05f, cfg.SplitMax + 0.05f);
                        return false;
                    }
                }
            }

            // 1. Áp dụng transforms vào cây AST
            Action<IdsNode> apply = null;
            apply = n =>
            {
                if (n == null) return;
                NodeCustomTransform t;
                if (transforms.TryGetValue(n.Id, out t)) n.Transform = t;
                if (n.Children != null) { foreach (var c in n.Children) apply(c); }
            };
            apply(root);

            var items = IdsLayoutEngine.ComputeLayout(root, new RectangleF(0, 0, 1000f, 1000f));
            if (items.Count == 0)
            {
                failReason = "Layout trống cho " + ids;
                return false;
            }

            // 2. Kiểm tra từng lá: Lỗ trắng không giảm & Độ đậm nét trong [0.8, 1.3] * T_font
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Char) || item.Char == "□") continue;

                // Đo số lỗ gốc
                int initialHoles;
                using (var bmpOrig = IdsOpticalBeautifier.RenderTestDilatedGlyph(item.Char, 1.0f, 1.0f, 0f, 0f))
                {
                    initialHoles = IdsOpticalBeautifier.CountHolesOnRaster(bmpOrig);
                }

                // Đo số lỗ sau biến đổi
                var t = item.Transform ?? new NodeCustomTransform();
                int finalHoles;
                using (var bmpFinal = IdsOpticalBeautifier.RenderTestDilatedGlyph(item.Char, t.ScaleX, t.ScaleY, t.StrokeDilationX, t.StrokeDilationY))
                {
                    finalHoles = IdsOpticalBeautifier.CountHolesOnRaster(bmpFinal);
                }

                if (finalHoles < initialHoles)
                {
                    failReason = string.Format("Lỗ trắng bị sập ở ký tự '{0}' ({1} -> {2} lỗ) trong {3}",
                        item.Char, initialHoles, finalHoles, ids);
                    return false;
                }

                // Kiểm tra giới hạn bù nét [0..25pt]
                if (t.StrokeDilationX < 0f || t.StrokeDilationX > 25f ||
                    t.StrokeDilationY < 0f || t.StrokeDilationY > 25f)
                {
                    failReason = string.Format("Stroke dilation vượt ngưỡng ({0:F1}, {1:F1}) cho '{2}'",
                        t.StrokeDilationX, t.StrokeDilationY, item.Char);
                    return false;
                }

                // Kiểm tra khung an toàn (không bị văng ra khỏi em-box [-20..1020])
                if (item.TargetRect.Left < -20f || item.TargetRect.Right > 1020f ||
                    item.TargetRect.Top < -20f || item.TargetRect.Bottom > 1020f)
                {
                    failReason = string.Format("Ký tự '{0}' văng ra ngoài khung an toàn: [{1}]",
                        item.Char, item.TargetRect);
                    return false;
                }
            }

            // 3. Kiểm tra không đâm va xuyên thủng nét giữa các nhánh anh em (ngoại trừ toán tử bao quanh và đè nhau)
            bool isEnclosureOrOverlap = ids.Any(c => "⿴⿵⿶⿷⿸⿹⿺⿻".Contains(c));
            if (!isEnclosureOrOverlap)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        var p1 = IdsOpticalBeautifier.ExtractOpticalMetrics(items[i].Char, font);
                        var p2 = IdsOpticalBeautifier.ExtractOpticalMetrics(items[j].Char, font);
                        if (p1 != null && p2 != null)
                        {
                            float collision;
                            bool isHoriz = Math.Abs(items[i].TargetRect.Center().X - items[j].TargetRect.Center().X) >
                                           Math.Abs(items[i].TargetRect.Center().Y - items[j].TargetRect.Center().Y);

                            IdsOpticalBeautifier.ComputeTrueInkGap(items[i].TargetRect, items[j].TargetRect, p1, p2, isHoriz, out collision);
                            if (collision > 80.0f)
                            {
                                failReason = string.Format("Va chạm nét mực nghiêm trọng ({0:F0}px) giữa '{1}' và '{2}' trong {3}",
                                    collision, items[i].Char, items[j].Char, ids);
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        private static void ApplyTransformsToTree(IdsNode node, Dictionary<string, NodeCustomTransform> transforms)
        {
            if (node == null || transforms == null) return;
            NodeCustomTransform t;
            if (transforms.TryGetValue(node.Id, out t)) node.Transform = t;
            if (node.Children != null)
            {
                foreach (var c in node.Children) ApplyTransformsToTree(c, transforms);
            }
        }

        private static void RenderThreeUserCasesVerification(FontFamily defaultFont)
        {
            try
            {
                string[] formulas = new string[] { "⿲木壴寸", "⿰日月", "⿰女⿱天土", "⿰亻⿱一木" };
                string[] titles   = new string[] { "1. ⿲木壴寸 (Chữ Thụ 樹)", "2. ⿰日月 (Chữ Minh 明)", "3. ⿰女⿱天土 (Chữ Mẹ tự chế)", "4. ⿰亻⿱一木 (Chữ Vị/Tác)" };

                FontFamily simsunFont = IdsGlyphRenderer.ResolveFontFamily("SimSun");
                FontFamily[] testFonts = new FontFamily[] { simsunFont, defaultFont };
                string[] fontLabels = new string[] { "[SimSun 宋体]", "[YaHei 黑体]" };

                int cellW = 420;
                int cellH = 460;
                int totalW = cellW * formulas.Length + 40;
                int totalH = cellH * testFonts.Length + 40;

                var reportText = new System.Text.StringBuilder();

                using (var bmp = new Bitmap(totalW, totalH, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                using (var titleFont = new Font("Microsoft YaHei", 12f, FontStyle.Bold))
                {
                    g.Clear(Color.FromArgb(245, 247, 250));
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    for (int row = 0; row < testFonts.Length; row++)
                    {
                        var fFam = testFonts[row];
                        for (int i = 0; i < formulas.Length; i++)
                        {
                            string ids = formulas[i];
                            var ast = IdsParserService.ParseIds(ids, true);
                            var res = IdsOpticalBeautifier.BeautifyThisIds(ids, ast, fFam, null, true);
                            ApplyTransformsToTree(ast, res.BeautifiedTransforms);
                            var items = IdsLayoutEngine.ComputeLayout(ast);

                            int x = 20 + i * cellW;
                            int y = 20 + row * cellH;

                            g.DrawString(fontLabels[row] + " " + titles[i], titleFont, Brushes.DarkSlateBlue, x + 8, y);

                            var stageRect = new Rectangle(0, 0, cellW - 20, cellW - 20);
                            using (var subBmp = new Bitmap(cellW - 20, cellW - 20, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                            using (var subG = Graphics.FromImage(subBmp))
                            {
                                IdsGlyphRenderer.RenderStage(
                                    subG,
                                    stageRect,
                                    items,
                                    null,
                                    IdsCanvasGridType.Mizige,
                                    true,
                                    IdsColorTheme.Classic,
                                    1.0f,
                                    fFam);
                                g.DrawImage(subBmp, x + 10, y + 32);
                            }

                            if (row == 0)
                            {
                                reportText.AppendLine("==================================================");
                                reportText.AppendLine(fontLabels[row] + " " + titles[i]);
                                reportText.AppendLine("==================================================");
                                reportText.AppendLine(res.GetFormattedReport());
                                reportText.AppendLine();
                            }
                        }
                    }

                    bmp.Save("verify_3_cases.png", System.Drawing.Imaging.ImageFormat.Png);
                }

                File.WriteAllText("verify_3_reports.txt", reportText.ToString(), System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private static Dictionary<string, NodeCustomTransform> CloneTransforms(Dictionary<string, NodeCustomTransform> src)
        {
            var res = new Dictionary<string, NodeCustomTransform>();
            foreach (var kvp in src) res[kvp.Key] = kvp.Value.Clone();
            return res;
        }

        private static PointF Center(this RectangleF r)
        {
            return new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        }

        private static float ComputeMedian(List<float> list)
        {
            if (list == null || list.Count == 0) return 0f;
            int n = list.Count;
            if (n % 2 == 1) return list[n / 2];
            return (list[n / 2 - 1] + list[n / 2]) / 2f;
        }

        private static float ComputeP90(List<float> list)
        {
            if (list == null || list.Count == 0) return 0f;
            int idx = (int)Math.Ceiling(list.Count * 0.90) - 1;
            return list[Math.Max(0, Math.Min(list.Count - 1, idx))];
        }
    }
}
