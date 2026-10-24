using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public class EvaluationMetrics
    {
        public float ChamferDistance { get; set; }  // Symmetric Chamfer distance in 1000 units
        public float L1ProfileError { get; set; }   // Normalized L1 error of row/col projections [0..1]
        public float BBoxError { get; set; }        // Mean deviation of 4 ink bbox edges in 1000 units
    }

    public class MethodBenchmarkSummary
    {
        public string MethodName { get; set; }
        public float MedianChamfer { get; set; }
        public float P90Chamfer { get; set; }
        public float MedianL1Profile { get; set; }
        public float P90L1Profile { get; set; }
        public float MedianBBoxError { get; set; }
        public float P90BBoxError { get; set; }
    }

    public class BenchmarkReport
    {
        public int TotalItems { get; set; }
        public int TrainItemsCount { get; set; }
        public int TestItemsCount { get; set; }

        public MethodBenchmarkSummary B0Train { get; set; }
        public MethodBenchmarkSummary B0Test { get; set; }
        public MethodBenchmarkSummary LegacyTrain { get; set; }
        public MethodBenchmarkSummary LegacyTest { get; set; }
        public MethodBenchmarkSummary NewTrain { get; set; }
        public MethodBenchmarkSummary NewTest { get; set; }

        public string ContactSheetPath { get; set; }

        public string FormatSummaryTable()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("==========================================================================================");
            sb.AppendLine(string.Format("  BENCHMARK CHẤT LƯỢNG AUTO-BEAUTIFY (Tổng: {0} chữ | Train: {1} | Test: {2})",
                TotalItems, TrainItemsCount, TestItemsCount));
            sb.AppendLine("==========================================================================================");
            sb.AppendLine("Split  | Phương pháp        | Chamfer (Med / P90) | L1 Profile (Med / P90) | BBox Err (Med / P90)");
            sb.AppendLine("-------+--------------------+---------------------+------------------------+---------------------");

            Action<string, MethodBenchmarkSummary> addRow = (split, m) =>
            {
                if (m == null) return;
                sb.AppendLine(string.Format("{0,-6} | {1,-18} | {2,7:F1} / {3,7:F1} | {4,7:F3} / {5,7:F3}  | {6,7:F1} / {7,7:F1}",
                    split, m.MethodName, m.MedianChamfer, m.P90Chamfer, m.MedianL1Profile, m.P90L1Profile, m.MedianBBoxError, m.P90BBoxError));
            };

            addRow("TRAIN", B0Train);
            addRow("TRAIN", LegacyTrain);
            addRow("TRAIN", NewTrain);
            sb.AppendLine("-------+--------------------+---------------------+------------------------+---------------------");
            addRow("TEST",  B0Test);
            addRow("TEST",  LegacyTest);
            addRow("TEST",  NewTest);
            sb.AppendLine("==========================================================================================");
            if (!string.IsNullOrEmpty(ContactSheetPath))
            {
                sb.AppendLine("Contact Sheet PNG: " + ContactSheetPath);
            }
            return sb.ToString();
        }
    }

    public static class IdsQualityHarness
    {
        private const int RasterSize = 128;

        public static BenchmarkReport RunBenchmark(FontFamily font, bool generateContactSheet = true, string contactSheetFile = "contact_sheet.png")
        {
            var rep = new BenchmarkReport();
            var glyphSrc = new GdiGlyphSource(font);

            // 1. Lọc tập chữ hợp lệ trong font (Cả chữ X và các lá con đều có glyph)
            var validItems = new List<IdsBenchmarkItem>();
            foreach (var item in IdsBenchmarkDataset.Items)
            {
                if (!glyphSrc.HasGlyph(item.Character)) continue;

                var ast = IdsParserService.ParseIds(item.Ids);
                if (ast == null) continue;

                bool allLeavesAvailable = true;
                Action<IdsNode> checkLeaves = null;
                checkLeaves = n =>
                {
                    if (n == null || !allLeavesAvailable) return;
                    if (!n.IsOperator)
                    {
                        if (string.IsNullOrEmpty(n.Char) || !glyphSrc.HasGlyph(n.Char))
                        {
                            allLeavesAvailable = false;
                        }
                    }
                    else if (n.Children != null)
                    {
                        foreach (var c in n.Children) checkLeaves(c);
                    }
                };
                checkLeaves(ast);

                if (allLeavesAvailable)
                {
                    validItems.Add(item);
                }
            }

            rep.TotalItems = validItems.Count;

            var b0TrainChamfer = new List<float>();
            var b0TestChamfer = new List<float>();
            var b0TrainL1 = new List<float>();
            var b0TestL1 = new List<float>();
            var b0TrainBBox = new List<float>();
            var b0TestBBox = new List<float>();

            var legTrainChamfer = new List<float>();
            var legTestChamfer = new List<float>();
            var legTrainL1 = new List<float>();
            var legTestL1 = new List<float>();
            var legTrainBBox = new List<float>();
            var legTestBBox = new List<float>();

            var newTrainChamfer = new List<float>();
            var newTestChamfer = new List<float>();
            var newTrainL1 = new List<float>();
            var newTestL1 = new List<float>();
            var newTrainBBox = new List<float>();
            var newTestBBox = new List<float>();

            int trainCount = 0;
            int testCount = 0;

            foreach (var item in validItems)
            {
                bool isTest = item.IsTest;
                if (isTest) testCount++; else trainCount++;

                // 2. Render Real Glyph
                RectangleF dummy;
                using (var maskReal = glyphSrc.RenderGlyphBitmap(item.Character, RasterSize, out dummy))
                {
                    if (maskReal == null) continue;

                    // Leave-One-Out: Loại X khỏi ExemplarSeeds nếu trùng một cách thực chất và an toàn luồng
                    using (ExemplarSeeds.ScopedExclude(item.Character))
                    {
                        // A. B0: Baseline chia đều không làm đẹp
                        var astB0 = IdsParserService.ParseIds(item.Ids);
                        using (var maskB0 = RenderCompositeMask(astB0, new Dictionary<string, NodeCustomTransform>(), font))
                        {
                            var mB0 = EvaluateMaskMetrics(maskB0, maskReal);
                            if (isTest)
                            {
                                b0TestChamfer.Add(mB0.ChamferDistance);
                                b0TestL1.Add(mB0.L1ProfileError);
                                b0TestBBox.Add(mB0.BBoxError);
                            }
                            else
                            {
                                b0TrainChamfer.Add(mB0.ChamferDistance);
                                b0TrainL1.Add(mB0.L1ProfileError);
                                b0TrainBBox.Add(mB0.BBoxError);
                            }
                        }

                        // B. Legacy Beautifier
                        var astLeg = IdsParserService.ParseIds(item.Ids);
                        var resLeg = Legacy.LegacyBeautifier.BeautifyThisIds(item.Ids, astLeg, font, null, true);
                        using (var maskLeg = RenderCompositeMask(astLeg, resLeg.BeautifiedTransforms, font))
                        {
                            var mLeg = EvaluateMaskMetrics(maskLeg, maskReal);
                            if (isTest)
                            {
                                legTestChamfer.Add(mLeg.ChamferDistance);
                                legTestL1.Add(mLeg.L1ProfileError);
                                legTestBBox.Add(mLeg.BBoxError);
                            }
                            else
                            {
                                legTrainChamfer.Add(mLeg.ChamferDistance);
                                legTrainL1.Add(mLeg.L1ProfileError);
                                legTrainBBox.Add(mLeg.BBoxError);
                            }
                        }

                        // C. Solver Mới
                        var astNew = IdsParserService.ParseIds(item.Ids);
                        var resNew = IdsOpticalBeautifier.BeautifyThisIds(item.Ids, astNew, font, null, true);
                        using (var maskNew = RenderCompositeMask(astNew, resNew.BeautifiedTransforms, font))
                        {
                            var mNew = EvaluateMaskMetrics(maskNew, maskReal);
                            if (isTest)
                            {
                                newTestChamfer.Add(mNew.ChamferDistance);
                                newTestL1.Add(mNew.L1ProfileError);
                                newTestBBox.Add(mNew.BBoxError);
                            }
                            else
                            {
                                newTrainChamfer.Add(mNew.ChamferDistance);
                                newTrainL1.Add(mNew.L1ProfileError);
                                newTrainBBox.Add(mNew.BBoxError);
                            }
                        }
                    }
                }
            }

            rep.TrainItemsCount = trainCount;
            rep.TestItemsCount = testCount;

            rep.B0Train = Summarize("B0 (Mặc định)", b0TrainChamfer, b0TrainL1, b0TrainBBox);
            rep.B0Test = Summarize("B0 (Mặc định)", b0TestChamfer, b0TestL1, b0TestBBox);

            rep.LegacyTrain = Summarize("Legacy Beautifier", legTrainChamfer, legTrainL1, legTrainBBox);
            rep.LegacyTest = Summarize("Legacy Beautifier", legTestChamfer, legTestL1, legTestBBox);

            rep.NewTrain = Summarize("Solver Mới", newTrainChamfer, newTrainL1, newTrainBBox);
            rep.NewTest = Summarize("Solver Mới", newTestChamfer, newTestL1, newTestBBox);

            if (generateContactSheet)
            {
                try
                {
                    GenerateContactSheet(contactSheetFile, validItems, font);
                    rep.ContactSheetPath = Path.GetFullPath(contactSheetFile);
                }
                catch { }
            }

            return rep;
        }

        private static MethodBenchmarkSummary Summarize(string name, List<float> chamfers, List<float> l1s, List<float> bboxes)
        {
            if (chamfers.Count == 0) return new MethodBenchmarkSummary { MethodName = name };

            chamfers.Sort();
            l1s.Sort();
            bboxes.Sort();

            int count = chamfers.Count;
            int medIdx = count / 2;
            int p90Idx = Math.Min(count - 1, (int)(count * 0.90f));

            return new MethodBenchmarkSummary
            {
                MethodName = name,
                MedianChamfer = chamfers[medIdx],
                P90Chamfer = chamfers[p90Idx],
                MedianL1Profile = l1s[medIdx],
                P90L1Profile = l1s[p90Idx],
                MedianBBoxError = bboxes[medIdx],
                P90BBoxError = bboxes[p90Idx]
            };
        }

        public static EvaluationMetrics EvaluateMaskMetrics(Bitmap maskA, Bitmap maskB)
        {
            var res = new EvaluationMetrics();
            int w = maskA.Width;
            int h = maskA.Height;

            bool[,] a = ExtractBinary(maskA);
            bool[,] b = ExtractBinary(maskB);

            // 1. Khoảng cách Chamfer đối xứng
            float[,] dtA = ComputeDistanceTransform(a, w, h);
            float[,] dtB = ComputeDistanceTransform(b, w, h);

            double sumAtoB = 0;
            int countA = 0;
            double sumBtoA = 0;
            int countB = 0;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (a[x, y]) { sumAtoB += dtB[x, y]; countA++; }
                    if (b[x, y]) { sumBtoA += dtA[x, y]; countB++; }
                }
            }

            double chamferPx = 0;
            if (countA > 0 && countB > 0)
            {
                chamferPx = (sumAtoB / countA + sumBtoA / countB) / 2.0;
            }
            else if (countA > 0)
            {
                chamferPx = sumAtoB / countA;
            }
            else if (countB > 0)
            {
                chamferPx = sumBtoA / countB;
            }

            res.ChamferDistance = (float)(chamferPx * 1000.0 / RasterSize);

            // 2. Sai số hồ sơ chiếu hàng và cột L1 chuẩn hóa
            float[] rowA = new float[h];
            float[] colA = new float[w];
            float[] rowB = new float[h];
            float[] colB = new float[w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (a[x, y]) { rowA[y]++; colA[x]++; }
                    if (b[x, y]) { rowB[y]++; colB[x]++; }
                }
            }

            NormalizeVector(rowA);
            NormalizeVector(colA);
            NormalizeVector(rowB);
            NormalizeVector(colB);

            float l1Row = 0f, l1Col = 0f;
            for (int y = 0; y < h; y++) l1Row += Math.Abs(rowA[y] - rowB[y]);
            for (int x = 0; x < w; x++) l1Col += Math.Abs(colA[x] - colB[x]);
            res.L1ProfileError = (l1Row + l1Col) / 2f;

            // 3. Sai số 4 cạnh ink bbox
            Rectangle bboxA = GetInkBoundingBox(a, w, h);
            Rectangle bboxB = GetInkBoundingBox(b, w, h);

            float dxMin = Math.Abs(bboxA.Left - bboxB.Left) * 1000f / w;
            float dxMax = Math.Abs(bboxA.Right - bboxB.Right) * 1000f / w;
            float dyMin = Math.Abs(bboxA.Top - bboxB.Top) * 1000f / h;
            float dyMax = Math.Abs(bboxA.Bottom - bboxB.Bottom) * 1000f / h;

            res.BBoxError = (dxMin + dxMax + dyMin + dyMax) / 4f;

            return res;
        }

        private static void NormalizeVector(float[] v)
        {
            float s = 0;
            for (int i = 0; i < v.Length; i++) s += v[i];
            if (s > 0)
            {
                for (int i = 0; i < v.Length; i++) v[i] /= s;
            }
        }

        private static Rectangle GetInkBoundingBox(bool[,] grid, int w, int h)
        {
            int minX = w, maxX = 0, minY = h, maxY = 0;
            bool found = false;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (grid[x, y])
                    {
                        found = true;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (!found) return new Rectangle(w / 4, h / 4, w / 2, h / 2);
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static bool[,] ExtractBinary(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            bool[,] grid = new bool[w, h];

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
                            byte r = row[x * 4 + 2];
                            byte g = row[x * 4 + 1];
                            byte b = row[x * 4];
                            grid[x, y] = (r < 180 || g < 180 || b < 180);
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return grid;
        }

        private static float[,] ComputeDistanceTransform(bool[,] grid, int w, int h)
        {
            float[,] dt = new float[w, h];
            var q = new Queue<Point>();

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (grid[x, y])
                    {
                        dt[x, y] = 0f;
                        q.Enqueue(new Point(x, y));
                    }
                    else
                    {
                        dt[x, y] = float.MaxValue;
                    }
                }
            }

            int[] dx = new int[] { -1, 1, 0, 0, -1, -1, 1, 1 };
            int[] dy = new int[] { 0, 0, -1, 1, -1, 1, -1, 1 };
            float[] cost = new float[] { 1f, 1f, 1f, 1f, 1.4142f, 1.4142f, 1.4142f, 1.4142f };

            while (q.Count > 0)
            {
                var pt = q.Dequeue();
                float curD = dt[pt.X, pt.Y];

                for (int i = 0; i < 8; i++)
                {
                    int nx = pt.X + dx[i];
                    int ny = pt.Y + dy[i];
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                    {
                        float newD = curD + cost[i];
                        if (newD < dt[nx, ny])
                        {
                            dt[nx, ny] = newD;
                            q.Enqueue(new Point(nx, ny));
                        }
                    }
                }
            }

            return dt;
        }

        public static Bitmap RenderCompositeMask(IdsNode root, Dictionary<string, NodeCustomTransform> transforms, FontFamily font, int pxSize = RasterSize)
        {
            var bmp = new Bitmap(pxSize, pxSize, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                Action<IdsNode> apply = null;
                apply = n =>
                {
                    if (n == null) return;
                    NodeCustomTransform t;
                    if (transforms != null && transforms.TryGetValue(n.Id, out t)) n.Transform = t;
                    if (n.Children != null) { foreach (var c in n.Children) apply(c); }
                };
                apply(root);

                var items = IdsLayoutEngine.ComputeLayout(root, new RectangleF(0, 0, 1000, 1000));
                float scale = (float)pxSize / 1000f;

                foreach (var item in items)
                {
                    if (string.IsNullOrEmpty(item.Char)) continue;

                    var rect = new RectangleF(
                        item.TargetRect.X * scale,
                        item.TargetRect.Y * scale,
                        item.TargetRect.Width * scale,
                        item.TargetRect.Height * scale);

                    using (var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    })
                    using (var f = new Font(font, Math.Max(4f, rect.Height * 0.85f), FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        g.DrawString(item.Char, f, Brushes.Black, rect, sf);
                    }
                }
            }
            return bmp;
        }

        public static void GenerateContactSheet(string outputPath, List<IdsBenchmarkItem> items, FontFamily font)
        {
            int cellW = 80;
            int cellH = 80;
            int rows = Math.Min(60, items.Count);
            int width = cellW * 5 + 100;
            int height = rows * cellH + 60;

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(248, 250, 252));
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (var headerFont = new Font("Segoe UI", 10f, FontStyle.Bold))
                using (var rowFont = new Font("Segoe UI", 9f))
                {
                    g.DrawString("IDS Cấu trúc", headerFont, Brushes.Black, 10, 15);
                    g.DrawString("B0 (Mặc định)", headerFont, Brushes.Black, 110, 15);
                    g.DrawString("Legacy", headerFont, Brushes.Black, 110 + cellW, 15);
                    g.DrawString("Solver Mới", headerFont, Brushes.Black, 110 + cellW * 2, 15);
                    g.DrawString("Glyph Thật (Font)", headerFont, Brushes.Black, 110 + cellW * 3, 15);

                    var glyphSrc = new GdiGlyphSource(font);

                    for (int r = 0; r < rows; r++)
                    {
                        var it = items[r];
                        int y = 50 + r * cellH;

                        g.DrawString(it.Character + "\n" + it.Ids, rowFont, Brushes.DarkSlateBlue, 10, y + 20);

                        // 1. B0
                        var astB0 = IdsParserService.ParseIds(it.Ids);
                        using (var mB0 = RenderCompositeMask(astB0, null, font, cellW - 10))
                        {
                            g.DrawImage(mB0, 110, y + 5);
                        }

                        // 2. Legacy
                        var astLeg = IdsParserService.ParseIds(it.Ids);
                        var resLeg = Legacy.LegacyBeautifier.BeautifyThisIds(it.Ids, astLeg, font, null, true);
                        using (var mLeg = RenderCompositeMask(astLeg, resLeg.BeautifiedTransforms, font, cellW - 10))
                        {
                            g.DrawImage(mLeg, 110 + cellW, y + 5);
                        }

                        // 3. New
                        var astNew = IdsParserService.ParseIds(it.Ids);
                        var resNew = IdsOpticalBeautifier.BeautifyThisIds(it.Ids, astNew, font, null, true);
                        using (var mNew = RenderCompositeMask(astNew, resNew.BeautifiedTransforms, font, cellW - 10))
                        {
                            g.DrawImage(mNew, 110 + cellW * 2, y + 5);
                        }

                        // 4. Real Glyph
                        RectangleF dummy;
                        using (var mReal = glyphSrc.RenderGlyphBitmap(it.Character, cellW - 10, out dummy))
                        {
                            if (mReal != null) g.DrawImage(mReal, 110 + cellW * 3, y + 5);
                        }

                        g.DrawLine(Pens.LightGray, 10, y + cellH, width - 10, y + cellH);
                    }
                }
            }

            bmp.Save(outputPath, ImageFormat.Png);
            bmp.Dispose();
        }
    }
}
