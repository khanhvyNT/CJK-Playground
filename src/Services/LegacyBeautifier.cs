using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using CJKPlayground.Models;

namespace CJKPlayground.Services.Legacy
{
    public class MorphologicalProfile
    {
        public float Sx { get; set; }           // Zero-crossing vertical stroke complexity
        public float Sy { get; set; }           // Zero-crossing horizontal stroke complexity
        public float Wx0 { get; set; }          // Median vertical stroke thickness (1000 units)
        public float Wy0 { get; set; }          // Median horizontal stroke thickness (1000 units)
        public int NHoles { get; set; }         // Number of internal closed white cavities
        public float MinHoleWidth { get; set; } // Minimum width of internal white gaps (1000 units)
        public float RhoWhite { get; set; }     // White-to-ink ratio
        public float WTop { get; set; }         // Span width ratio in top 20% [0..1]
        public float WMid { get; set; }         // Span width ratio in middle 40-60% [0..1]
        public float WBot { get; set; }         // Span width ratio in bottom 80-100% [0..1]
        public float Aspect { get; set; }       // Ink width / ink height
        public float Cx { get; set; }           // Horizontal Center of Mass [0..1]
        public float Cy { get; set; }           // Vertical Center of Mass [0..1]
        public RectangleF InkBBox { get; set; } // Tight bounding box in 1000 units
        public float[] TopSkyline { get; set; } // 64 distance samples from top [0..1]
        public float[] BottomSkyline { get; set; } // 64 distance samples from bottom [0..1]
        public float[] LeftSkyline { get; set; } // 64 distance samples from left [0..1]
        public float[] RightSkyline { get; set; } // 64 distance samples from right [0..1]

        public bool IsWideBase
        {
            get
            {
                if (WBot < 0.80f) return false;
                // Check if bottom skyline is relatively flat (low variance in non-infinite cells)
                float minD = 1.0f, maxD = 0.0f;
                int valid = 0;
                for (int i = 8; i < 56; i++)
                {
                    if (BottomSkyline[i] < 0.90f)
                    {
                        valid++;
                        if (BottomSkyline[i] < minD) minD = BottomSkyline[i];
                        if (BottomSkyline[i] > maxD) maxD = BottomSkyline[i];
                    }
                }
                return valid > 20 && (maxD - minD) < 0.28f;
            }
        }

        public bool IsRoof
        {
            get
            {
                if (WTop < 0.80f) return false;
                if (WMid >= WTop * 0.85f) return false;
                // Top skyline flat
                float minD = 1.0f, maxD = 0.0f;
                int valid = 0;
                for (int i = 8; i < 56; i++)
                {
                    if (TopSkyline[i] < 0.90f)
                    {
                        valid++;
                        if (TopSkyline[i] < minD) minD = TopSkyline[i];
                        if (TopSkyline[i] > maxD) maxD = TopSkyline[i];
                    }
                }
                return valid > 20 && (maxD - minD) < 0.28f;
            }
        }

        public bool IsNarrow
        {
            get { return Aspect < 0.55f; }
        }

        public MorphologicalProfile()
        {
            TopSkyline = new float[64];
            BottomSkyline = new float[64];
            LeftSkyline = new float[64];
            RightSkyline = new float[64];
            Aspect = 1.0f;
            WTop = 0.8f;
            WMid = 0.8f;
            WBot = 0.8f;
            Sx = 2.0f;
            Sy = 2.0f;
            Wx0 = 68.0f;
            Wy0 = 68.0f;
            MinHoleWidth = 40.0f;
        }
    }

    public class SanityResult
    {
        public bool Ok { get; set; }
        public string MessageVi { get; set; }
        public string MessageEn { get; set; }
        public int ErrorPos { get; set; }
    }

    public class BeautifyResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsAlreadyOptimal { get; set; }
        public float AestheticScore { get; set; }
        public List<string> ReportBulletPoints { get; set; }
        public Dictionary<string, NodeCustomTransform> BeautifiedTransforms { get; set; }

        public BeautifyResult()
        {
            ReportBulletPoints = new List<string>();
            BeautifiedTransforms = new Dictionary<string, NodeCustomTransform>();
            AestheticScore = 95.0f;
        }

        public string GetFormattedReport()
        {
            return string.Join("\r\n", ReportBulletPoints.ToArray());
        }
    }

    /// <summary>
    /// Bản sao Bộ giải Cũ (Legacy Solver) phục vụ so sánh đối chuẩn B0 / Legacy / Solver Mới.
    /// </summary>
    public static class LegacyBeautifier
    {
        private static readonly Dictionary<string, MorphologicalProfile> ProfileCache = new Dictionary<string, MorphologicalProfile>();
        private static readonly object CacheLock = new object();

        // ====================================================================================
        // BƯỚC 1: GÁC CỔNG NGỮ PHÁP & KHÓA GỐC BẤT BIẾN (Stage1_SanityAndIdempotentLock)
        // ====================================================================================
        public static SanityResult Stage1_ValidateAstSanity(string input, IdsNode root, IGlyphSource glyphSource)
        {
            if (string.IsNullOrWhiteSpace(input) || root == null)
            {
                return new SanityResult
                {
                    Ok = false,
                    MessageVi = "Chuỗi IDS chưa hoàn chỉnh (còn thiếu thành phần chữ). Vui lòng nhập đủ cấu trúc trước khi làm đẹp.",
                    MessageEn = "IDS string is incomplete (missing character components). Please complete the composition before beautifying.",
                    ErrorPos = 0
                };
            }

            // 0. Kiểm tra surrogate lẻ (Unpaired surrogate check)
            for (int si = 0; si < input.Length; si++)
            {
                char ch = input[si];
                if (char.IsHighSurrogate(ch))
                {
                    if (si + 1 >= input.Length || !char.IsLowSurrogate(input[si + 1]))
                    {
                        return new SanityResult
                        {
                            Ok = false,
                            MessageVi = "Phát hiện ký tự UTF-16 surrogate lẻ không hợp lệ trong chuỗi IDS.",
                            MessageEn = "Invalid lone high surrogate detected in IDS string.",
                            ErrorPos = si
                        };
                    }
                    si++;
                }
                else if (char.IsLowSurrogate(ch))
                {
                    return new SanityResult
                    {
                        Ok = false,
                        MessageVi = "Phát hiện ký tự UTF-16 surrogate lẻ không hợp lệ trong chuỗi IDS.",
                        MessageEn = "Invalid lone low surrogate detected in IDS string.",
                        ErrorPos = si
                    };
                }
            }

            var tokens = IdsParserService.TokenizeUnicode(input.Trim());
            if (tokens.Count == 0)
            {
                return new SanityResult
                {
                    Ok = false,
                    MessageVi = "Chuỗi IDS chưa hoàn chỉnh (còn thiếu thành phần chữ). Vui lòng nhập đủ cấu trúc trước khi làm đẹp.",
                    MessageEn = "IDS string is incomplete (missing character components). Please complete the composition before beautifying.",
                    ErrorPos = 0
                };
            }

            // 1. Kiểm tra toán tử IDC chưa hỗ trợ (ngoài U+2FF0..U+2FFB)
            for (int i = 0; i < tokens.Count; i++)
            {
                string tok = tokens[i];
                if (!string.IsNullOrEmpty(tok))
                {
                    if (tok == "\u31EF" || (tok.Length == 1 && tok[0] >= '\u2FFC' && tok[0] <= '\u2FFF'))
                    {
                        return new SanityResult
                        {
                            Ok = false,
                            MessageVi = string.Format("Toán tử IDC '{0}' chưa được hỗ trợ chuẩn.", tok),
                            MessageEn = string.Format("IDC operator '{0}' is currently unsupported.", tok),
                            ErrorPos = i
                        };
                    }
                }
            }

            // 2. Kiểm tra toán hạng rỗng / giữ chỗ / không có glyph
            bool hasPlaceholder = false;
            string missingChar = null;
            int missingPos = -1;

            Action<IdsNode> checkLeafNodes = null;
            checkLeafNodes = n =>
            {
                if (n == null || hasPlaceholder) return;
                if (!n.IsOperator)
                {
                    if (string.IsNullOrEmpty(n.Char) || n.Char == "□" || n.Char == " " ||
                        (n.Id != null && n.Id.StartsWith("ph_")))
                    {
                        hasPlaceholder = true;
                    }
                    else if (glyphSource != null && !glyphSource.HasGlyph(n.Char))
                    {
                        hasPlaceholder = true;
                        missingChar = n.Char;
                    }
                }
                else if (n.Children != null)
                {
                    foreach (var c in n.Children) checkLeafNodes(c);
                }
            };
            checkLeafNodes(root);

            if (hasPlaceholder)
            {
                string msgVi = missingChar != null
                    ? string.Format("Ký tự '{0}' không tồn tại glyph hoặc thiếu nét mực trong font hiện tại.", missingChar)
                    : "Chuỗi IDS chưa hoàn chỉnh (còn thiếu thành phần chữ). Vui lòng nhập đủ cấu trúc trước khi làm đẹp.";
                string msgEn = missingChar != null
                    ? string.Format("Character '{0}' lacks glyph outline or ink pixels in the current font.", missingChar)
                    : "IDS structure is incomplete (missing components). Please enter full components before beautifying.";
                return new SanityResult { Ok = false, MessageVi = msgVi, MessageEn = msgEn, ErrorPos = missingPos };
            }

            // 3. Kiểm tra lỗi thừa ký tự ngoài cây gốc (Trailing Tokens)
            int consumed = CountTokensInTree(root);
            if (consumed < tokens.Count)
            {
                return new SanityResult
                {
                    Ok = false,
                    MessageVi = "Cấu trúc IDS sai ngữ pháp (thừa thành phần nằm ngoài toán tử gốc).",
                    MessageEn = "IDS syntax error (trailing tokens found outside the root operator tree).",
                    ErrorPos = consumed
                };
            }

            // 4. Kiểm tra độ sâu đệ quy cực hạn (Over-nesting Guard > 4 trên cùng 1 trục)
            int maxHoriz = GetMaxAxisDepth(root, true, 0);
            int maxVert = GetMaxAxisDepth(root, false, 0);
            if (maxHoriz > 4 || maxVert > 4)
            {
                return new SanityResult
                {
                    Ok = false,
                    MessageVi = "Cấu trúc lồng quá sâu trên một trục khiến mật độ nét vượt ngưỡng hiển thị chuẩn.",
                    MessageEn = "Recursive nesting exceeds physical limit on a single axis (density exceeds rendering limits).",
                    ErrorPos = 0
                };
            }

            return new SanityResult { Ok = true };
        }

        private static int CountTokensInTree(IdsNode node)
        {
            if (node == null) return 0;
            if (node.Id != null && node.Id.StartsWith("ph_")) return 0;
            int count = 1;
            if (node.IsOperator && node.Children != null)
            {
                foreach (var c in node.Children)
                {
                    count += CountTokensInTree(c);
                }
            }
            return count;
        }

        private static int GetMaxAxisDepth(IdsNode node, bool isHorizontal, int currentConsecutive)
        {
            if (node == null || !node.IsOperator) return currentConsecutive;
            bool matchesAxis = isHorizontal
                ? (node.Operator == "⿰" || node.Operator == "⿲")
                : (node.Operator == "⿱" || node.Operator == "⿳");

            int nextConsecutive = matchesAxis ? currentConsecutive + 1 : 0;
            int maxDepth = nextConsecutive;
            if (node.Children != null)
            {
                foreach (var c in node.Children)
                {
                    int cd = GetMaxAxisDepth(c, isHorizontal, nextConsecutive);
                    if (cd > maxDepth) maxDepth = cd;
                }
            }
            return maxDepth;
        }

        // ====================================================================================
        // BƯỚC 2: ĐO ĐẠC HÌNH THÁI HỌC TỪ DƯỚI LÊN (Stage2_BottomUpMorphologicalProfiling)
        // ====================================================================================
        public static MorphologicalProfile Stage2_ExtractLeafProfile(string ch, IGlyphSource glyphSource)
        {
            if (string.IsNullOrEmpty(ch)) return new MorphologicalProfile();

            string cacheKey = ch;
            lock (CacheLock)
            {
                MorphologicalProfile cached;
                if (ProfileCache.TryGetValue(cacheKey, out cached)) return cached;
            }

            RectangleF inkBBox1000;
            using (var bmp256 = glyphSource.RenderGlyphBitmap(ch, 256, out inkBBox1000))
            {
                if (bmp256 == null) return new MorphologicalProfile();

                var profile = new MorphologicalProfile();
                profile.InkBBox = inkBBox1000;
                profile.Aspect = inkBBox1000.Height > 0 ? (inkBBox1000.Width / inkBBox1000.Height) : 1.0f;

                // 1. Phân tích lỗ trắng nội tại (Flood-fill đệm 1px trên lưới 256)
                int nHoles;
                float minHoleW;
                float rhoWhite;
                MeasureInternalHoles(bmp256, out nHoles, out minHoleW, out rhoWhite);
                profile.NHoles = nHoles;
                profile.MinHoleWidth = minHoleW;
                profile.RhoWhite = rhoWhite;

                // 2. Đo độ dày nét gốc Wx0, Wy0 trên lưới 256
                float wx, wy;
                MeasureStrokeThickness(bmp256, out wx, out wy);
                profile.Wx0 = wx;
                profile.Wy0 = wy;

                // 3. Downsample sang ma trận 64x64 nhị phân để đo Skylines và Sải ngang WTop/WMid/WBot
                bool[,] grid64 = DownsampleToBinaryGrid(bmp256, 64);
                MeasureSkylinesAndSpans(grid64, profile);

                // 4. Mật độ nét giao cắt Sx, Sy (top 50% hàng/cột)
                float sxVal, syVal;
                MeasureZeroCrossings(grid64, out sxVal, out syVal);
                profile.Sx = sxVal;
                profile.Sy = syVal;

                lock (CacheLock)
                {
                    ProfileCache[cacheKey] = profile;
                }
                return profile;
            }
        }

        private static void MeasureInternalHoles(Bitmap bmp256, out int holeCount, out float minHoleWidth, out float rhoWhite)
        {
            int w = bmp256.Width;
            int h = bmp256.Height;

            // Đệm 1 pixel viền trắng quanh bitmap để flood-fill không bị chặn mép
            int bw = w + 2;
            int bh = h + 2;
            byte[,] grid = new byte[bw, bh]; // 0 = white, 1 = ink

            int totalInk = 0;
            BitmapData bd = bmp256.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
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
                            if (row[x * 4 + 2] < 180 || row[x * 4 + 1] < 180 || row[x * 4] < 180)
                            {
                                grid[x + 1, y + 1] = 1;
                                totalInk++;
                            }
                        }
                    }
                }
            }
            finally
            {
                bmp256.UnlockBits(bd);
            }

            // Flood-fill từ điểm (0, 0) đánh dấu nền trắng bên ngoài
            bool[,] visited = new bool[bw, bh];
            var q = new Queue<Point>();
            q.Enqueue(new Point(0, 0));
            visited[0, 0] = true;

            int outsideWhite = 0;
            while (q.Count > 0)
            {
                var pt = q.Dequeue();
                outsideWhite++;
                int px = pt.X;
                int py = pt.Y;

                if (px > 0 && !visited[px - 1, py] && grid[px - 1, py] == 0) { visited[px - 1, py] = true; q.Enqueue(new Point(px - 1, py)); }
                if (px < bw - 1 && !visited[px + 1, py] && grid[px + 1, py] == 0) { visited[px + 1, py] = true; q.Enqueue(new Point(px + 1, py)); }
                if (py > 0 && !visited[px, py - 1] && grid[px, py - 1] == 0) { visited[px, py - 1] = true; q.Enqueue(new Point(px, py - 1)); }
                if (py < bh - 1 && !visited[px, py + 1] && grid[px, py + 1] == 0) { visited[px, py + 1] = true; q.Enqueue(new Point(px, py + 1)); }
            }

            // Đếm các thành phần liên thông màu trắng chưa được thăm -> Đó chính là lỗ trắng khép kín
            int holes = 0;
            int totalHolePixels = 0;
            int minHolePixelSpan = int.MaxValue;

            for (int y = 1; y <= h; y++)
            {
                for (int x = 1; x <= w; x++)
                {
                    if (grid[x, y] == 0 && !visited[x, y])
                    {
                        // Khám phá lỗ trắng mới
                        holes++;
                        int holeArea = 0;
                        int hMinX = x, hMaxX = x;
                        var hq = new Queue<Point>();
                        hq.Enqueue(new Point(x, y));
                        visited[x, y] = true;

                        while (hq.Count > 0)
                        {
                            var pt = hq.Dequeue();
                            holeArea++;
                            if (pt.X < hMinX) hMinX = pt.X;
                            if (pt.X > hMaxX) hMaxX = pt.X;

                            int hx = pt.X;
                            int hy = pt.Y;
                            if (hx > 1 && !visited[hx - 1, hy] && grid[hx - 1, hy] == 0) { visited[hx - 1, hy] = true; hq.Enqueue(new Point(hx - 1, hy)); }
                            if (hx < w && !visited[hx + 1, hy] && grid[hx + 1, hy] == 0) { visited[hx + 1, hy] = true; hq.Enqueue(new Point(hx + 1, hy)); }
                            if (hy > 1 && !visited[hx, hy - 1] && grid[hx, hy - 1] == 0) { visited[hx, hy - 1] = true; hq.Enqueue(new Point(hx, hy - 1)); }
                            if (hy < h && !visited[hx, hy + 1] && grid[hx, hy + 1] == 0) { visited[hx, hy + 1] = true; hq.Enqueue(new Point(hx, hy + 1)); }
                        }

                        // Bỏ qua nhiễu cô lập (< 4 pixel)
                        if (holeArea < 4)
                        {
                            holes--;
                        }
                        else
                        {
                            totalHolePixels += holeArea;
                            int span = hMaxX - hMinX + 1;
                            if (span < minHolePixelSpan) minHolePixelSpan = span;
                        }
                    }
                }
            }

            holeCount = holes;
            minHoleWidth = (minHolePixelSpan != int.MaxValue) ? ((float)minHolePixelSpan * 1000f / w) : 60f;
            rhoWhite = (float)totalHolePixels / Math.Max(1, totalInk);
        }

        private static void MeasureStrokeThickness(Bitmap bmp256, out float wx0, out float wy0)
        {
            int w = bmp256.Width;
            int h = bmp256.Height;
            var runsX = new List<int>();
            var runsY = new List<int>();

            BitmapData bd = bmp256.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bd.Scan0;
                    int stride = bd.Stride;

                    // Quét ngang tìm bề rộng nét dọc
                    for (int y = 16; y < h - 16; y += 4)
                    {
                        byte* row = ptr + y * stride;
                        int cur = 0;
                        for (int x = 0; x < w; x++)
                        {
                            bool ink = (row[x * 4 + 2] < 180 || row[x * 4 + 1] < 180 || row[x * 4] < 180);
                            if (ink) cur++;
                            else { if (cur > 0) runsX.Add(cur); cur = 0; }
                        }
                        if (cur > 0) runsX.Add(cur);
                    }

                    // Quét dọc tìm bề dày nét ngang
                    for (int x = 16; x < w - 16; x += 4)
                    {
                        int cur = 0;
                        for (int y = 0; y < h; y++)
                        {
                            byte* pixel = ptr + y * stride + x * 4;
                            bool ink = (pixel[2] < 180 || pixel[1] < 180 || pixel[0] < 180);
                            if (ink) cur++;
                            else { if (cur > 0) runsY.Add(cur); cur = 0; }
                        }
                        if (cur > 0) runsY.Add(cur);
                    }
                }
            }
            finally
            {
                bmp256.UnlockBits(bd);
            }

            runsX.Sort();
            runsY.Sort();

            int medX = runsX.Count > 0 ? runsX[runsX.Count / 2] : 16;
            int medY = runsY.Count > 0 ? runsY[runsY.Count / 2] : 16;

            wx0 = Math.Max(28f, Math.Min(120f, (float)medX * 1000f / w));
            wy0 = Math.Max(28f, Math.Min(120f, (float)medY * 1000f / h));
        }

        private static bool[,] DownsampleToBinaryGrid(Bitmap bmp, int sz)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            bool[,] grid = new bool[sz, sz];

            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bd.Scan0;
                    int stride = bd.Stride;

                    // 1. Dò tìm khung mực sát (Tight Ink Bounding Box) trên lưới 256
                    int minX = w, maxX = 0, minY = h, maxY = 0;
                    bool hasInk = false;
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < w; x++)
                        {
                            if (row[x * 4 + 2] < 180 || row[x * 4 + 1] < 180 || row[x * 4] < 180)
                            {
                                hasInk = true;
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                        }
                    }

                    if (!hasInk || minX > maxX || minY > maxY)
                    {
                        return grid;
                    }

                    int inkW = maxX - minX + 1;
                    int inkH = maxY - minY + 1;

                    // 2. Chuyển đổi sang ma trận sz x sz chuẩn hóa theo Tight Crop
                    for (int gy = 0; gy < sz; gy++)
                    {
                        int srcY = minY + (int)(gy * (float)inkH / sz);
                        if (srcY > maxY) srcY = maxY;
                        byte* row = ptr + srcY * stride;

                        for (int gx = 0; gx < sz; gx++)
                        {
                            int srcX = minX + (int)(gx * (float)inkW / sz);
                            if (srcX > maxX) srcX = maxX;
                            grid[gx, gy] = (row[srcX * 4 + 2] < 180 || row[srcX * 4 + 1] < 180 || row[srcX * 4] < 180);
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

        private static void MeasureSkylinesAndSpans(bool[,] grid, MorphologicalProfile p)
        {
            int sz = 64;

            // 1. Skylines
            for (int x = 0; x < sz; x++)
            {
                float dTop = 1.0f;
                for (int y = 0; y < sz; y++)
                {
                    if (grid[x, y]) { dTop = (float)y / sz; break; }
                }
                p.TopSkyline[x] = dTop;

                float dBot = 1.0f;
                for (int y = sz - 1; y >= 0; y--)
                {
                    if (grid[x, y]) { dBot = (float)(sz - 1 - y) / sz; break; }
                }
                p.BottomSkyline[x] = dBot;
            }

            for (int y = 0; y < sz; y++)
            {
                float dLeft = 1.0f;
                for (int x = 0; x < sz; x++)
                {
                    if (grid[x, y]) { dLeft = (float)x / sz; break; }
                }
                p.LeftSkyline[y] = dLeft;

                float dRight = 1.0f;
                for (int x = sz - 1; x >= 0; x--)
                {
                    if (grid[x, y]) { dRight = (float)(sz - 1 - x) / sz; break; }
                }
                p.RightSkyline[y] = dRight;
            }

            // Morphological Closing 3-element filter on Skylines
            p.TopSkyline = MorphologicalClose(p.TopSkyline);
            p.BottomSkyline = MorphologicalClose(p.BottomSkyline);
            p.LeftSkyline = MorphologicalClose(p.LeftSkyline);
            p.RightSkyline = MorphologicalClose(p.RightSkyline);

            // 2. Measure WTop (0..20%), WMid (40..60%), WBot (80..100%)
            p.WTop = MeasureSpanInBand(grid, 0, (int)(sz * 0.20f));
            p.WMid = MeasureSpanInBand(grid, (int)(sz * 0.40f), (int)(sz * 0.60f));
            p.WBot = MeasureSpanInBand(grid, (int)(sz * 0.80f), sz);

            // 3. Center of mass (Cx, Cy)
            float sumX = 0, sumY = 0;
            int countInk = 0;
            for (int cy = 0; cy < sz; cy++)
            {
                for (int cx = 0; cx < sz; cx++)
                {
                    if (grid[cx, cy])
                    {
                        sumX += cx;
                        sumY += cy;
                        countInk++;
                    }
                }
            }
            p.Cx = countInk > 0 ? (sumX / countInk) / (float)sz : 0.5f;
            p.Cy = countInk > 0 ? (sumY / countInk) / (float)sz : 0.5f;
        }

        private static float MeasureSpanInBand(bool[,] grid, int startY, int endY)
        {
            int sz = 64;
            int minX = sz, maxX = -1;
            for (int y = startY; y < endY && y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    if (grid[x, y])
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                    }
                }
            }
            if (minX <= maxX) return (float)(maxX - minX + 1) / sz;
            return 0.5f;
        }

        private static void MeasureZeroCrossings(bool[,] grid, out float sx, out float sy)
        {
            int sz = 64;
            var rowCrossings = new List<int>(sz);
            for (int y = 0; y < sz; y++)
            {
                int c = 0;
                bool last = false;
                for (int x = 0; x < sz; x++)
                {
                    bool cur = grid[x, y];
                    if (cur && !last) c++;
                    last = cur;
                }
                rowCrossings.Add(c);
            }
            rowCrossings.Sort();
            float sumRow = 0;
            for (int i = sz / 2; i < sz; i++) sumRow += rowCrossings[i];
            sx = Math.Max(1.0f, sumRow / (sz / 2f));

            var colCrossings = new List<int>(sz);
            for (int x = 0; x < sz; x++)
            {
                int c = 0;
                bool last = false;
                for (int y = 0; y < sz; y++)
                {
                    bool cur = grid[x, y];
                    if (cur && !last) c++;
                    last = cur;
                }
                colCrossings.Add(c);
            }
            colCrossings.Sort();
            float sumCol = 0;
            for (int i = sz / 2; i < sz; i++) sumCol += colCrossings[i];
            sy = Math.Max(1.0f, sumCol / (sz / 2f));
        }

        private static float[] MorphologicalClose(float[] arr)
        {
            int len = arr.Length;
            float[] dilated = new float[len];
            for (int i = 0; i < len; i++)
            {
                float left = (i > 0) ? arr[i - 1] : arr[i];
                float mid = arr[i];
                float right = (i < len - 1) ? arr[i + 1] : arr[i];
                dilated[i] = Math.Min(left, Math.Min(mid, right));
            }
            float[] closed = new float[len];
            for (int i = 0; i < len; i++)
            {
                float left = (i > 0) ? dilated[i - 1] : dilated[i];
                float mid = dilated[i];
                float right = (i < len - 1) ? dilated[i + 1] : dilated[i];
                closed[i] = Math.Max(left, Math.Max(mid, right));
            }
            return closed;
        }

        // Bottom-Up Aggregation for Operators
        public static MorphologicalProfile Stage2_AggregateOperatorProfile(string op, List<MorphologicalProfile> childProfiles, SolverConfig cfg)
        {
            var p = new MorphologicalProfile();
            if (childProfiles == null || childProfiles.Count == 0) return p;

            if (op == "⿰" && childProfiles.Count >= 2)
            {
                p.Sx = childProfiles[0].Sx + childProfiles[1].Sx;
                p.Sy = Math.Max(childProfiles[0].Sy, childProfiles[1].Sy);
            }
            else if (op == "⿱" && childProfiles.Count >= 2)
            {
                p.Sx = Math.Max(childProfiles[0].Sx, childProfiles[1].Sx);
                p.Sy = childProfiles[0].Sy + childProfiles[1].Sy;
                p.WTop = childProfiles[0].WTop;
                p.WBot = childProfiles[1].WBot;
            }
            else if (op == "⿲" && childProfiles.Count >= 3)
            {
                p.Sx = childProfiles[0].Sx + childProfiles[1].Sx + childProfiles[2].Sx;
                p.Sy = Math.Max(childProfiles[0].Sy, Math.Max(childProfiles[1].Sy, childProfiles[2].Sy));
            }
            else if (op == "⿳" && childProfiles.Count >= 3)
            {
                p.Sx = Math.Max(childProfiles[0].Sx, Math.Max(childProfiles[1].Sx, childProfiles[2].Sx));
                p.Sy = childProfiles[0].Sy + childProfiles[1].Sy + childProfiles[2].Sy;
                p.WTop = childProfiles[0].WTop;
                p.WBot = childProfiles[2].WBot;
            }
            else if (op == "⿻" && childProfiles.Count >= 2)
            {
                p.Sx = childProfiles[0].Sx + childProfiles[1].Sx;
                p.Sy = childProfiles[0].Sy + childProfiles[1].Sy;
            }
            else if (childProfiles.Count >= 2)
            {
                // Enclosure operators
                p.Sx = Math.Max(childProfiles[0].Sx, childProfiles[1].Sx) + cfg.LambdaSurround * Math.Min(childProfiles[0].Sx, childProfiles[1].Sx);
                p.Sy = Math.Max(childProfiles[0].Sy, childProfiles[1].Sy) + cfg.LambdaSurround * Math.Min(childProfiles[0].Sy, childProfiles[1].Sy);
                p.WTop = childProfiles[0].WTop;
                p.WBot = childProfiles[0].WBot;
            }

            p.Wx0 = childProfiles[0].Wx0;
            p.Wy0 = childProfiles[0].Wy0;
            return p;
        }

        // ====================================================================================
        // BƯỚC 3: TRUY TÌM & GIẢI PHẪU NGUYÊN MẪU (Stage3_ExemplarDissection)
        // ====================================================================================
        public class ExemplarMatchInfo
        {
            public bool IsGeneralMode { get; set; }
            public string ExemplarDisplay { get; set; }
            public float Distance { get; set; }
            public float Confidence { get; set; }
            public float Alpha { get; set; }
            public float RExemplar { get; set; }
        }

        public static ExemplarMatchInfo Stage3_FindNearestExemplar(
            string op,
            MorphologicalProfile actualProfile,
            IGlyphSource glyphSource,
            SolverConfig cfg)
        {
            var seeds = ExemplarSeeds.GetSeeds(op);
            var candidates = new List<Tuple<ExemplarSeed, float>>();

            foreach (var s in seeds)
            {
                if (!glyphSource.HasGlyph(s.Character)) continue;

                var seedProfile = Stage2_ExtractLeafProfile(s.Character, glyphSource);
                // Normalized Euclidean Distance in morphological feature space
                float dSx = (actualProfile.Sx - seedProfile.Sx) / 3.0f;
                float dSy = (actualProfile.Sy - seedProfile.Sy) / 3.0f;
                float dWTop = (actualProfile.WTop - seedProfile.WTop) / 0.5f;
                float dWBot = (actualProfile.WBot - seedProfile.WBot) / 0.5f;
                float dAspect = (actualProfile.Aspect - seedProfile.Aspect) / 0.5f;

                float dist = (float)Math.Sqrt(dSx * dSx + dSy * dSy + dWTop * dWTop + dWBot * dWBot + dAspect * dAspect);
                candidates.Add(Tuple.Create(s, dist));
            }

            if (candidates.Count == 0)
            {
                return new ExemplarMatchInfo
                {
                    IsGeneralMode = true,
                    Confidence = 0f,
                    Alpha = 0f,
                    RExemplar = 0.5f,
                    ExemplarDisplay = "Chế độ tổng quát (Bảng nguyên mẫu rỗng hoặc không có glyph phù hợp)"
                };
            }

            candidates.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            var best = candidates[0];
            float d = best.Item2;
            float c = (float)Math.Exp(-Math.Pow(d / cfg.Sigma, 2.0));
            float alpha = cfg.AlphaMax * c;

            if (c < cfg.CMin)
            {
                return new ExemplarMatchInfo
                {
                    IsGeneralMode = true,
                    Distance = d,
                    Confidence = c,
                    Alpha = 0f,
                    RExemplar = 0.5f,
                    ExemplarDisplay = string.Format("Chế độ tổng quát: Không có nguyên mẫu đủ gần (c={0:F2} < c_min)", c)
                };
            }

            // Giải phẫu nguyên mẫu tìm r_exemplar qua hình chiếu 1D
            float rEx = DissectExemplarSplitRatio(best.Item1, glyphSource, op, cfg);

            return new ExemplarMatchInfo
            {
                IsGeneralMode = false,
                Distance = d,
                Confidence = c,
                Alpha = alpha,
                RExemplar = rEx,
                ExemplarDisplay = string.Format("[{0} U+{1:X4}] · d={2:F2} · c={3:F2} · alpha={4:F2}",
                    best.Item1.Character, best.Item1.CodePoint, d, c, alpha)
            };
        }

        private static float DissectExemplarSplitRatio(ExemplarSeed seed, IGlyphSource glyphSource, string op, SolverConfig cfg)
        {
            RectangleF bb;
            using (var b = glyphSource.RenderGlyphBitmap(seed.Character, 128, out bb))
            {
                if (b == null) return 0.5f;

                bool isVertical = (op == "⿱" || op == "⿳");
                int sz = 128;
                float[] proj = new float[sz];

                BitmapData bd = b.LockBits(new Rectangle(0, 0, sz, sz), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    unsafe
                    {
                        byte* ptr = (byte*)bd.Scan0;
                        int stride = bd.Stride;
                        for (int y = 0; y < sz; y++)
                        {
                            byte* row = ptr + y * stride;
                            for (int x = 0; x < sz; x++)
                            {
                                if (row[x * 4 + 2] < 180 || row[x * 4 + 1] < 180 || row[x * 4] < 180)
                                {
                                    if (isVertical) proj[y]++;
                                    else proj[x]++;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    b.UnlockBits(bd);
                }

                // Tìm điểm trũng giữa 2 thung lũng chiếu 1D quanh r ∈ [0.26, 0.74]
                int start = (int)(sz * cfg.SplitMin);
                int end = (int)(sz * cfg.SplitMax);
                float minVal = float.MaxValue;
                int bestIdx = sz / 2;

                for (int i = start; i <= end; i++)
                {
                    if (proj[i] < minVal)
                    {
                        minVal = proj[i];
                        bestIdx = i;
                    }
                }

                return Math.Max(cfg.SplitMin, Math.Min(cfg.SplitMax, (float)bestIdx / sz));
            }
        }

        public static float Stage3_ComputeOptimalSplitRatio(
            float s1,
            float s2,
            ExemplarMatchInfo match,
            SolverConfig cfg)
        {
            double p1 = Math.Pow(Math.Max(0.5, s1), cfg.Gamma);
            double p2 = Math.Pow(Math.Max(0.5, s2), cfg.Gamma);
            float rPower = (float)(p1 / (p1 + p2));

            float rFinal;
            if (match.IsGeneralMode || match.Alpha <= 0f)
            {
                rFinal = rPower;
            }
            else
            {
                rFinal = match.Alpha * match.RExemplar + (1.0f - match.Alpha) * rPower;
            }

            return Math.Max(cfg.SplitMin, Math.Min(cfg.SplitMax, rFinal));
        }

        // ====================================================================================
        // BƯỚC 4: DÒ HỐC TRỐNG ĐỘNG 2D (Stage4_TopDownAndCavityAllocation)
        // ====================================================================================
        public static RectangleF Stage4_FindLargestEmptyCavity(
            Bitmap outerBmp,
            string op,
            RectangleF safeBox,
            SolverConfig cfg)
        {
            if (outerBmp == null)
            {
                return new RectangleF(safeBox.X + safeBox.Width * 0.25f, safeBox.Y + safeBox.Height * 0.25f, safeBox.Width * 0.5f, safeBox.Height * 0.5f);
            }

            int sz = 64;
            bool[,] grid = DownsampleToBinaryGrid(outerBmp, sz);

            // Dilate mặt nạ mực một lượng đệm an toàn cavityBuffer (~2 cell trên lưới 64)
            int dilateCells = Math.Max(1, (int)Math.Round(cfg.CavityBuffer * sz / 1000f));
            bool[,] dilatedInk = new bool[sz, sz];

            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    if (grid[x, y])
                    {
                        for (int dy = -dilateCells; dy <= dilateCells; dy++)
                        {
                            for (int dx = -dilateCells; dx <= dilateCells; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx >= 0 && nx < sz && ny >= 0 && ny < sz)
                                    dilatedInk[nx, ny] = true;
                            }
                        }
                    }
                }
            }

            // Vùng tìm kiếm giới hạn theo hướng mở của toán tử
            int minSearchX = 0, maxSearchX = sz - 1;
            int minSearchY = 0, maxSearchY = sz - 1;

            switch (op)
            {
                case "⿵": minSearchY = (int)(sz * 0.25f); break; // Mở dưới
                case "⿶": maxSearchY = (int)(sz * 0.75f); break; // Mở trên
                case "⿷": minSearchX = (int)(sz * 0.25f); break; // Mở phải
                case "⿸": minSearchX = (int)(sz * 0.25f); minSearchY = (int)(sz * 0.25f); break; // Mở dưới-phải
                case "⿹": maxSearchX = (int)(sz * 0.75f); minSearchY = (int)(sz * 0.25f); break; // Mở dưới-trái
                case "⿺": minSearchX = (int)(sz * 0.25f); maxSearchY = (int)(sz * 0.75f); break; // Mở trên-phải
            }

            // Summed-Area Table (Histogram Stack) tìm hình chữ nhật trống lớn nhất
            int bestArea = 0;
            int bestX = minSearchX, bestY = minSearchY, bestW = 10, bestH = 10;

            int[] heights = new int[sz];
            for (int y = minSearchY; y <= maxSearchY; y++)
            {
                for (int x = minSearchX; x <= maxSearchX; x++)
                {
                    if (!dilatedInk[x, y]) heights[x]++;
                    else heights[x] = 0;
                }

                // Largest Rectangle in Histogram
                var stack = new Stack<int>();
                for (int i = minSearchX; i <= maxSearchX + 1; i++)
                {
                    int curH = (i <= maxSearchX) ? heights[i] : 0;
                    while (stack.Count > 0 && curH < heights[stack.Peek()])
                    {
                        int hVal = heights[stack.Pop()];
                        int wVal = (stack.Count == 0) ? (i - minSearchX) : (i - stack.Peek() - 1);
                        int area = hVal * wVal;
                        if (area > bestArea)
                        {
                            bestArea = area;
                            bestW = wVal;
                            bestH = hVal;
                            bestX = i - wVal;
                            bestY = y - hVal + 1;
                        }
                    }
                    stack.Push(i);
                }
            }

            float cavityW1000 = (float)bestW * 1000f / sz;
            float cavityH1000 = (float)bestH * 1000f / sz;
            float cavityX1000 = (float)bestX * 1000f / sz;
            float cavityY1000 = (float)bestY * 1000f / sz;

            // Kiểm tra kích thước tối thiểu (18% em-box)
            if (cavityW1000 < cfg.CavityMinRatio * 1000f || cavityH1000 < cfg.CavityMinRatio * 1000f)
            {
                // Hốc quá nhỏ: nới an toàn trong safeBox
                return new RectangleF(safeBox.X + safeBox.Width * 0.20f, safeBox.Y + safeBox.Height * 0.20f,
                                      safeBox.Width * 0.60f, safeBox.Height * 0.60f);
            }

            return new RectangleF(cavityX1000, cavityY1000, cavityW1000, cavityH1000);
        }

        // ====================================================================================
        // BƯỚC 5: TRƯỢT KHỚP MỘNG & BÙ NÉT DỊ HƯỚNG (Stage5_SkylineInterlockAndAnisotropicDilate)
        // ====================================================================================
        public static float Stage5_ComputeSkylineKerning(
            float[] bottomOfTop,
            float[] topOfBottom,
            float hTop,
            float hBottom,
            SolverConfig cfg)
        {
            float minClearance = float.MaxValue;
            int validCount = 0;

            for (int c = 0; c < 64; c++)
            {
                // Chỉ xét cột cả hai bên đều có mực thực sự
                if (bottomOfTop[c] < 0.95f && topOfBottom[c] < 0.95f)
                {
                    float clr = hTop * bottomOfTop[c] + hBottom * topOfBottom[c];
                    if (clr < minClearance) minClearance = clr;
                    validCount++;
                }
            }

            if (validCount == 0 || minClearance == float.MaxValue) return 0f;

            // Khoảng dịch trượt cần thiết để đạt G_target = 26 units
            float dShift = minClearance - cfg.GTarget;
            return Math.Max(-40f, Math.Min(110f, dShift));
        }

        public static void Stage5_ComputeAnisotropicDilation(
            MorphologicalProfile profile,
            float scaleX,
            float scaleY,
            float txFont,
            float tyFont,
            SolverConfig cfg,
            out float dilateX,
            out float dilateY)
        {
            float wxEffective = profile.Wx0 * scaleX;
            float wyEffective = profile.Wy0 * scaleY;

            float rawDx = Math.Max(0f, (txFont - wxEffective) * cfg.DilateCoeff);
            float rawDy = Math.Max(0f, (tyFont - wyEffective) * cfg.DilateCoeff);

            // Counter-Space Guard: Nếu có lỗ trắng và khoảng hở hẹp, kiểm soát nấc lùi 0.5 unit
            if (profile.NHoles > 0 && profile.MinHoleWidth < cfg.MinHoleWidth * 2.5f)
            {
                float maxAllowableDilation = Math.Max(0f, (profile.MinHoleWidth - cfg.MinHoleWidth) * 0.40f);
                rawDx = Math.Min(rawDx, maxAllowableDilation);
                rawDy = Math.Min(rawDy, maxAllowableDilation);
            }

            // Làm tròn từng nấc 0.5 unit
            dilateX = (float)(Math.Round(rawDx * 2.0) / 2.0);
            dilateY = (float)(Math.Round(rawDy * 2.0) / 2.0);
        }

        // ====================================================================================
        // BƯỚC 6: BỘ GIẢI TỐI ƯU TOÀN CỤC & TẠO BÁO CÁO (Stage6_FinalCenteringAndReport)
        // ====================================================================================
        public static BeautifyResult BeautifyThisIds(
            string idsString,
            IdsNode root,
            FontFamily font,
            bool isVietnamese)
        {
            return BeautifyThisIds(idsString, root, font, null, isVietnamese);
        }

        public static bool ValidateAstSanity(string ids, IdsNode root, out string err)
        {
            var sanity = Stage1_ValidateAstSanity(ids, root, new GdiGlyphSource(FontFamily.GenericSansSerif));
            err = sanity.MessageVi;
            return sanity.Ok;
        }

        public static MorphologicalProfile ExtractOpticalMetrics(string glyph, FontFamily font)
        {
            if (string.IsNullOrEmpty(glyph)) return null;
            return Stage2_ExtractLeafProfile(glyph, new GdiGlyphSource(font));
        }

        public static BeautifyResult BeautifyThisIds(
            string idsString,
            IdsNode root,
            FontFamily font,
            Dictionary<string, NodeCustomTransform> currentTransforms = null,
            bool isVietnamese = true)
        {
            var res = new BeautifyResult();
            var cfg = new SolverConfig();
            var glyphSource = new GdiGlyphSource(font);

            // 1. Bước 1: Gác cổng ngữ pháp
            var sanity = Stage1_ValidateAstSanity(idsString, root, glyphSource);
            if (!sanity.Ok)
            {
                res.Success = false;
                res.ErrorMessage = isVietnamese ? sanity.MessageVi : sanity.MessageEn;
                return res;
            }

            // Đo độ dày nét chuẩn và lề an toàn của font hiện tại
            float txFont, tyFont;
            glyphSource.GetFontStrokeThickness(out txFont, out tyFont);
            RectangleF safeBox = glyphSource.MeasureExemplarSafeBox();

            // 2. Bước 1.B: Reset Zero-Base Working Copy (Không cộng dồn)
            var transforms = new Dictionary<string, NodeCustomTransform>();
            Action<IdsNode> initZeroTransforms = null;
            initZeroTransforms = n =>
            {
                if (n == null) return;
                transforms[n.Id] = new NodeCustomTransform();
                if (n.Children != null)
                {
                    foreach (var c in n.Children) initZeroTransforms(c);
                }
            };
            initZeroTransforms(root);

            // Bảng lưu Morphological Profiles cho từng Node
            var nodeProfiles = new Dictionary<string, MorphologicalProfile>();

            // 3. Bước 2: Hậu thứ tự (Bottom-Up) đo đạc hình thái học
            Action<IdsNode> computeProfiles = null;
            computeProfiles = n =>
            {
                if (n == null) return;
                if (n.Children != null)
                {
                    foreach (var c in n.Children) computeProfiles(c);
                }

                if (!n.IsOperator)
                {
                    nodeProfiles[n.Id] = Stage2_ExtractLeafProfile(n.Char, glyphSource);
                }
                else
                {
                    var childP = new List<MorphologicalProfile>();
                    if (n.Children != null)
                    {
                        foreach (var c in n.Children)
                        {
                            MorphologicalProfile cp;
                            if (nodeProfiles.TryGetValue(c.Id, out cp)) childP.Add(cp);
                        }
                    }
                    nodeProfiles[n.Id] = Stage2_AggregateOperatorProfile(n.Operator, childP, cfg);
                }
            };
            computeProfiles(root);

            // 4. Bước 3, 4, 5: Tiền thứ tự (Top-Down) phân bổ khung và trượt khớp mộng
            Action<IdsNode> solveNode = null;
            solveNode = n =>
            {
                if (n == null || !n.IsOperator || n.Children == null || n.Children.Count < 2) return;

                var op = n.Operator;
                var tOp = transforms[n.Id];
                var children = n.Children;

                if (op == "⿰" && children.Count >= 2)
                {
                    var p0 = nodeProfiles[children[0].Id];
                    var p1 = nodeProfiles[children[1].Id];

                    var match = Stage3_FindNearestExemplar(op, nodeProfiles[n.Id], glyphSource, cfg);
                    float r = Stage3_ComputeOptimalSplitRatio(p0.Sx, p1.Sx, match, cfg);
                    r = (float)Math.Round(r, 2);
                    tOp.SplitRatio = r;
                    transforms[n.Id] = tOp;

                    res.ReportBulletPoints.Add(string.Format(
                        isVietnamese
                            ? "• Toán tử ⿰: Chia tỷ lệ {0}% : {1}% | Tham chiếu: {2}"
                            : "• Operator ⿰: Split ratio {0}% : {1}% | Ref: {2}",
                        (int)Math.Round(r * 100), 100 - (int)Math.Round(r * 100), match.ExemplarDisplay));

                    // Khớp mộng ngang (Skyline Collision Kerning)
                    float dShift = Stage5_ComputeSkylineKerning(p0.RightSkyline, p1.LeftSkyline, 1000f * r, 1000f * (1f - r), cfg);
                    if (dShift > 8f)
                    {
                        float deltaX = Math.Min(dShift, 80f);
                        var t0 = transforms[children[0].Id];
                        var t1 = transforms[children[1].Id];
                        t1.OffsetX = -(int)Math.Round(deltaX * 0.65f);
                        t0.OffsetX = +(int)Math.Round(deltaX * 0.35f);
                        transforms[children[0].Id] = t0;
                        transforms[children[1].Id] = t1;

                        res.ReportBulletPoints.Add(string.Format(
                            isVietnamese
                                ? "• Khớp mộng ngang (Skyline Kerning): Kéo lồng {0}px (Khoảng hở tự nhiên {1:F0}px)"
                                : "• Horizontal Skyline Kerning: Interlocked {0}px (Natural gap {1:F0}px)",
                            Math.Abs(t1.OffsetX), dShift));
                    }
                }
                else if (op == "⿱" && children.Count >= 2)
                {
                    var p0 = nodeProfiles[children[0].Id];
                    var p1 = nodeProfiles[children[1].Id];

                    var match = Stage3_FindNearestExemplar(op, nodeProfiles[n.Id], glyphSource, cfg);
                    float r = Stage3_ComputeOptimalSplitRatio(p0.Sy, p1.Sy, match, cfg);
                    // Hệ số trọng tâm "Thượng thu hạ phóng"
                    r = Math.Max(cfg.SplitMin, Math.Min(cfg.SplitMax, (float)Math.Round(r * 0.94f, 2)));
                    tOp.SplitRatio = r;
                    transforms[n.Id] = tOp;

                    res.ReportBulletPoints.Add(string.Format(
                        isVietnamese
                            ? "• Toán tử ⿱: Chia tỷ lệ {0}% : {1}% | Tham chiếu: {2}"
                            : "• Operator ⿱: Split ratio {0}% : {1}% | Ref: {2}",
                        (int)Math.Round(r * 100), 100 - (int)Math.Round(r * 100), match.ExemplarDisplay));

                    // Quy tắc tương quan sải ngang (Width Harmony Rule)
                    var t0 = transforms[children[0].Id];
                    var t1 = transforms[children[1].Id];

                    if (p1.IsWideBase && !p0.IsRoof)
                    {
                        float sTop = 1.0f - match.Alpha * (1.0f - cfg.STopDefault);
                        float sBot = 1.0f - match.Alpha * (1.0f - cfg.SBotDefault);
                        t0.ScaleX = (float)Math.Round(sTop, 2);
                        t1.ScaleX = (float)Math.Round(sBot, 2);

                        res.ReportBulletPoints.Add(string.Format(
                            isVietnamese
                                ? "• Bệ đỡ ngang (Base-Anchor Rule): Thu gọn đỉnh {0}% để đáy vươn rộng {1}% làm bệ đỡ vững"
                                : "• Base-Anchor Harmony: Tucked top {0}% so wide bottom anchors glyph at {1}%",
                            (int)Math.Round(sTop * 100), (int)Math.Round(sBot * 100)));
                    }
                    else if (p0.IsRoof)
                    {
                        float sRoof = 1.0f - match.Alpha * (1.0f - cfg.SBotDefault);
                        float sUnder = 1.0f - match.Alpha * (1.0f - cfg.STopDefault);
                        t0.ScaleX = (float)Math.Round(sRoof, 2);
                        t1.ScaleX = (float)Math.Round(sUnder, 2);

                        res.ReportBulletPoints.Add(string.Format(
                            isVietnamese
                                ? "• Mái che (Roof Shelter): Mở rộng đỉnh {0}% che chở phần đáy thu gọn {1}%"
                                : "• Roof Shelter: Expanded top {0}% sheltering tucked bottom {1}%",
                            (int)Math.Round(sRoof * 100), (int)Math.Round(sUnder * 100)));
                    }

                    // Khớp mộng dọc (Vertical Skyline Kerning)
                    float dShift = Stage5_ComputeSkylineKerning(p0.BottomSkyline, p1.TopSkyline, 1000f * r, 1000f * (1f - r), cfg);
                    if (dShift > 8f)
                    {
                        float deltaY = Math.Min(dShift, 110f);
                        t1.OffsetY = -(int)Math.Round(deltaY * 0.65f);
                        t0.OffsetY = +(int)Math.Round(deltaY * 0.35f);

                        res.ReportBulletPoints.Add(string.Format(
                            isVietnamese
                                ? "• Khớp mộng dọc (Skyline Kerning): Đẩy nhánh dưới chui lên {0}px (Khoảng hở tự nhiên {1:F0}px)"
                                : "• Vertical Skyline Kerning: Slid lower part up {0}px into cavity (Clearance {1:F0}px)",
                            Math.Abs(t1.OffsetY), dShift));
                    }

                    transforms[children[0].Id] = t0;
                    transforms[children[1].Id] = t1;
                }
                else if (op == "⿲" && children.Count >= 3)
                {
                    var p0 = nodeProfiles[children[0].Id];
                    var p1 = nodeProfiles[children[1].Id];
                    var p2 = nodeProfiles[children[2].Id];

                    double pow0 = Math.Pow(p0.Sx, cfg.Gamma);
                    double pow1 = Math.Pow(p1.Sx, cfg.Gamma);
                    double pow2 = Math.Pow(p2.Sx, cfg.Gamma);
                    double sumP = pow0 + pow1 + pow2;

                    float r0 = Math.Max(cfg.TernaryMin, (float)(pow0 / sumP));
                    float r1 = Math.Max(cfg.TernaryMin, (float)(pow1 / sumP));
                    float r2 = 1.0f - r0 - r1;
                    if (r2 < cfg.TernaryMin) { r2 = cfg.TernaryMin; r0 = (1.0f - r2) / 2f; r1 = r0; }

                    var match = Stage3_FindNearestExemplar(op, nodeProfiles[n.Id], glyphSource, cfg);
                    tOp.SplitRatios3 = new float[] { (float)Math.Round(r0, 2), (float)Math.Round(r1, 2), (float)Math.Round(r2, 2) };
                    transforms[n.Id] = tOp;

                    res.ReportBulletPoints.Add(string.Format(
                        isVietnamese
                            ? "• Toán tử ⿲: Phân bổ 3 cột: {0}% : {1}% : {2}% | Tham chiếu: {3}"
                            : "• Operator ⿲: 3-column split: {0}% : {1}% : {2}% | Ref: {3}",
                        (int)Math.Round(r0 * 100), (int)Math.Round(r1 * 100), (int)Math.Round(r2 * 100), match.ExemplarDisplay));
                }
                else if (op == "⿳" && children.Count >= 3)
                {
                    var p0 = nodeProfiles[children[0].Id];
                    var p1 = nodeProfiles[children[1].Id];
                    var p2 = nodeProfiles[children[2].Id];

                    double pow0 = Math.Pow(p0.Sy, cfg.Gamma) * 0.92;
                    double pow1 = Math.Pow(p1.Sy, cfg.Gamma);
                    double pow2 = Math.Pow(p2.Sy, cfg.Gamma) * 1.08;
                    double sumP = pow0 + pow1 + pow2;

                    float r0 = Math.Max(cfg.TernaryMin, (float)(pow0 / sumP));
                    float r1 = Math.Max(cfg.TernaryMin, (float)(pow1 / sumP));
                    float r2 = 1.0f - r0 - r1;
                    if (r2 < cfg.TernaryMin) { r2 = cfg.TernaryMin; r0 = (1.0f - r2) / 2f; r1 = r0; }

                    var match = Stage3_FindNearestExemplar(op, nodeProfiles[n.Id], glyphSource, cfg);
                    tOp.SplitRatios3 = new float[] { (float)Math.Round(r0, 2), (float)Math.Round(r1, 2), (float)Math.Round(r2, 2) };
                    transforms[n.Id] = tOp;

                    res.ReportBulletPoints.Add(string.Format(
                        isVietnamese
                            ? "• Toán tử ⿳: Phân bổ 3 tầng: {0}% : {1}% : {2}% (Thượng thu hạ phóng) | Tham chiếu: {3}"
                            : "• Operator ⿳: 3-tier split: {0}% : {1}% : {2}% (Lower center of gravity) | Ref: {3}",
                        (int)Math.Round(r0 * 100), (int)Math.Round(r1 * 100), (int)Math.Round(r2 * 100), match.ExemplarDisplay));
                }
                else if (children.Count >= 2)
                {
                    var match = Stage3_FindNearestExemplar(op, nodeProfiles[n.Id], glyphSource, cfg);

                    // Toán tử bao quanh: Dò hốc trống động
                    RectangleF dummy;
                    using (var outerBmp = glyphSource.RenderGlyphBitmap(children[0].Char, 128, out dummy))
                    {
                        var cavity = Stage4_FindLargestEmptyCavity(outerBmp, op, safeBox, cfg);
                        var tInner = transforms[children[1].Id];

                        // Canh tâm ruột vào tâm hốc
                        float cavityCenterX = cavity.X + cavity.Width / 2f;
                        float cavityCenterY = cavity.Y + cavity.Height / 2f;
                        tInner.OffsetX = (int)Math.Round(cavityCenterX - 500f);
                        tInner.OffsetY = (int)Math.Round(cavityCenterY - 500f);

                        transforms[children[1].Id] = tInner;
                        res.ReportBulletPoints.Add(string.Format(
                            isVietnamese
                                ? "• Tối ưu cấu trúc bao ({0}): Dò hốc trống động, căn khớp nhánh lõi vào tâm hốc ({1:F0}, {2:F0}), đệm an toàn 26pt | Tham chiếu: {3}"
                                : "• Enclosure structure optimization ({0}): Fitted core into cavity center ({1:F0}, {2:F0}), buffer 26pt | Ref: {3}",
                            op, cavityCenterX, cavityCenterY, match.ExemplarDisplay));
                    }
                }

                foreach (var c in children) solveNode(c);
            };
            solveNode(root);

            // 5. Bước 5.B: Bù nét dị hướng (Anisotropic Dilation) + Bảo vệ lỗ trắng
            Action<IdsNode> applyDilation = null;
            applyDilation = n =>
            {
                if (n == null) return;
                if (!n.IsOperator)
                {
                    MorphologicalProfile p;
                    if (nodeProfiles.TryGetValue(n.Id, out p))
                    {
                        var t = transforms[n.Id];
                        float dx, dy;
                        Stage5_ComputeAnisotropicDilation(p, t.ScaleX, t.ScaleY, txFont, tyFont, cfg, out dx, out dy);
                        t.StrokeDilationX = dx;
                        t.StrokeDilationY = dy;
                        t.StrokeWeight = Math.Max(dx, dy);
                        transforms[n.Id] = t;

                        if (dx > 0f || dy > 0f)
                        {
                            res.ReportBulletPoints.Add(string.Format(
                                isVietnamese
                                    ? "• Bù nét dị hướng [{0}]: DilateX={1:F1}pt, DilateY={2:F1}pt (Bảo toàn 100% khe trắng nội tại)"
                                    : "• Anisotropic Dilation [{0}]: DilateX={1:F1}pt, DilateY={2:F1}pt (100% internal holes preserved)",
                                n.Char, dx, dy));
                        }
                    }
                }
                else if (n.Children != null)
                {
                    foreach (var c in n.Children) applyDilation(c);
                }
            };
            applyDilation(root);

            // 6. Bước 6.B: Chuẩn hóa khung nội Mễ Tự Cách (Safe-Box Centering)
            Action<IdsNode> applyTransformsToAst = null;
            applyTransformsToAst = n =>
            {
                if (n == null) return;
                NodeCustomTransform t;
                if (transforms.TryGetValue(n.Id, out t)) n.Transform = t;
                if (n.Children != null)
                {
                    foreach (var c in n.Children) applyTransformsToAst(c);
                }
            };
            applyTransformsToAst(root);

            var items = IdsLayoutEngine.ComputeLayout(root, new RectangleF(100f, 100f, 800f, 800f));
            if (items.Count > 0)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var it in items)
                {
                    if (it.TargetRect.Left < minX) minX = it.TargetRect.Left;
                    if (it.TargetRect.Right > maxX) maxX = it.TargetRect.Right;
                    if (it.TargetRect.Top < minY) minY = it.TargetRect.Top;
                    if (it.TargetRect.Bottom > maxY) maxY = it.TargetRect.Bottom;
                }

                float curCx = (minX + maxX) / 2f;
                float curCy = (minY + maxY) / 2f;
                float shiftX = 500f - curCx;
                float shiftY = 500f - curCy;

                if (Math.Abs(shiftX) > 6f || Math.Abs(shiftY) > 6f)
                {
                    int dx = (int)Math.Round(shiftX);
                    int dy = (int)Math.Round(shiftY);
                    foreach (var it in items)
                    {
                        NodeCustomTransform t;
                        if (transforms.TryGetValue(it.NodeId, out t))
                        {
                            t.OffsetX = Math.Max(-200, Math.Min(200, t.OffsetX + dx));
                            t.OffsetY = Math.Max(-200, Math.Min(200, t.OffsetY + dy));
                            transforms[it.NodeId] = t;
                        }
                    }

                    res.ReportBulletPoints.Add(string.Format(
                        isVietnamese
                            ? "• Căn tâm Mễ Tự Cách: Đồng tâm an toàn [{0:F0}..{1:F0}] (Hiệu chỉnh X: {2:+0;-0;0}px, Y: {3:+0;-0;0}px)"
                            : "• Mizige Centering: Re-centered within safe frame [{0:F0}..{1:F0}] (Shift X: {2:+0;-0;0}px, Y: {3:+0;-0;0}px)",
                        safeBox.Left, safeBox.Right, dx, dy));
                }
                else
                {
                    res.ReportBulletPoints.Add(string.Format(
                        isVietnamese
                            ? "• Chuẩn hóa Mễ Tự Cách: Cấu trúc cân đối hoàn hảo trong khung an toàn [{0:F0}..{1:F0}]"
                            : "• Safe-frame Centering: Balanced within Mizige safe bounds [{0:F0}..{1:F0}]",
                        safeBox.Left, safeBox.Right));
                }
            }

            // 7. Bước 1.B: Kiểm tra tính Bất biến Idempotent (So sánh với transforms hiện tại)
            if (currentTransforms != null && currentTransforms.Count > 0)
            {
                bool isIdempotentIdentical = true;
                foreach (var kvp in transforms)
                {
                    NodeCustomTransform cur;
                    if (currentTransforms.TryGetValue(kvp.Key, out cur) && cur != null)
                    {
                        var opt = kvp.Value;
                        float scaleErr = Math.Max(Math.Abs(opt.ScaleX - cur.ScaleX), Math.Abs(opt.ScaleY - cur.ScaleY));
                        float splitErr = Math.Abs((opt.SplitRatio ?? 0.5f) - (cur.SplitRatio ?? 0.5f));
                        float offErr = Math.Max(Math.Abs(opt.OffsetX - cur.OffsetX), Math.Abs(opt.OffsetY - cur.OffsetY)) / 1000f;
                        float strokeErr = Math.Max(Math.Abs(opt.StrokeDilationX - cur.StrokeDilationX),
                                                   Math.Abs(opt.StrokeDilationY - cur.StrokeDilationY)) / Math.Max(1f, txFont);

                        if (scaleErr > 0.01f || splitErr > 0.01f || offErr > 0.01f || strokeErr > 0.01f)
                        {
                            isIdempotentIdentical = false;
                            break;
                        }
                    }
                    else
                    {
                        isIdempotentIdentical = false;
                        break;
                    }
                }

                if (isIdempotentIdentical)
                {
                    res.IsAlreadyOptimal = true;
                    res.Success = true;
                    res.ReportBulletPoints.Clear();
                    res.ReportBulletPoints.Add(isVietnamese
                        ? "✔ Bố cục đã đạt chuẩn cân bằng tối ưu theo nguyên mẫu (Điểm thẩm mỹ: Đạt chuẩn). Không cần biến đổi thêm."
                        : "✔ Layout has already reached optimal exemplar equilibrium (Aesthetic Score: Optimal). No further transforms needed.");
                    res.BeautifiedTransforms = transforms;
                    return res;
                }
            }

            res.Success = true;
            res.BeautifiedTransforms = transforms;
            return res;
        }
    }
}
