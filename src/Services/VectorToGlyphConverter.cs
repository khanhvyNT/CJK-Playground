using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public class GlyphMetricsOptions
    {
        public ushort UnitsPerEm { get; set; }
        public short Ascender { get; set; }
        public short Descender { get; set; }
        public ushort AdvanceWidth { get; set; }
        public short LeftSideBearing { get; set; }
        public bool AutoCenterHorizontal { get; set; }
        public float VerticalBaselineOffset { get; set; }

        public GlyphMetricsOptions()
        {
            UnitsPerEm = 1000;
            Ascender = 880;
            Descender = -120;
            AdvanceWidth = 1000;
            LeftSideBearing = 50;
            AutoCenterHorizontal = true;
            VerticalBaselineOffset = 0f;
        }

        public GlyphMetricsOptions(FontHeaderInfo header)
        {
            AutoCenterHorizontal = true;
            VerticalBaselineOffset = 0f;
            if (header != null)
            {
                UnitsPerEm = header.UnitsPerEm > 0 ? header.UnitsPerEm : (ushort)1000;
                Ascender = header.Ascender != 0 ? header.Ascender : (short)880;
                Descender = header.Descender != 0 ? header.Descender : (short)-120;
                AdvanceWidth = UnitsPerEm;
                LeftSideBearing = (short)(UnitsPerEm * 0.05f);
            }
            else
            {
                UnitsPerEm = 1000;
                Ascender = 880;
                Descender = -120;
                AdvanceWidth = 1000;
                LeftSideBearing = 50;
            }
        }
    }

    public static class VectorToGlyphConverter
    {
        private const float DefaultTolerance = 1.0f; // 1.0 font unit error tolerance

        /// <summary>
        /// Chuyển đổi một GraphicsPath (GDI+ Cubic Béziers & Lines) thành RawGlyphOutline của TrueType
        /// với hệ tọa độ thiết kế chuẩn của Font (UnitsPerEm, Ascender, Descender, LSB).
        /// </summary>
        public static RawGlyphOutline ConvertPathToTrueTypeGlyph(
            GraphicsPath path,
            RectangleF canvasSourceRect,
            GlyphMetricsOptions options = null)
        {
            if (options == null) options = new GlyphMetricsOptions();
            if (path == null || path.PointCount == 0)
            {
                return new RawGlyphOutline
                {
                    NumberOfContours = 0,
                    AdvanceWidth = options.AdvanceWidth,
                    LeftSideBearing = options.LeftSideBearing
                };
            }

            if (canvasSourceRect.Width <= 0f) canvasSourceRect.Width = 1000f;
            if (canvasSourceRect.Height <= 0f) canvasSourceRect.Height = 1000f;

            // 1. Phân tách GraphicsPath thành danh sách các subpath ban đầu bằng GraphicsPathIterator
            var subpaths = ExtractSubpaths(path);
            if (subpaths.Count == 0)
            {
                return new RawGlyphOutline
                {
                    NumberOfContours = 0,
                    AdvanceWidth = options.AdvanceWidth,
                    LeftSideBearing = options.LeftSideBearing
                };
            }

            // 2. Chuyển đổi tọa độ từ Canvas (Top-Down) sang Font Design Units (Bottom-Up)
            float em = options.UnitsPerEm > 0 ? options.UnitsPerEm : 1000f;
            float fontHeight = options.Ascender - options.Descender;
            if (fontHeight <= 0f) fontHeight = em;

            // CJK ideographs thường cao bằng 80% - 90% UnitsPerEm trong ô vuông
            float scaleX = em / canvasSourceRect.Width;
            float scaleY = em / canvasSourceRect.Height;

            // Chuyển đổi từng điểm và chuyển Cubic -> Quadratic Bézier
            var fontContours = new List<List<OutlinePoint>>();

            foreach (var sp in subpaths)
            {
                using (sp)
                {
                    var convertedContour = ConvertSubpathToFont(sp, canvasSourceRect, options, scaleX, scaleY);
                    if (convertedContour.Count >= 3)
                    {
                        fontContours.Add(convertedContour);
                    }
                }
            }

            if (fontContours.Count == 0)
            {
                return new RawGlyphOutline
                {
                    NumberOfContours = 0,
                    AdvanceWidth = options.AdvanceWidth,
                    LeftSideBearing = options.LeftSideBearing
                };
            }

            // 3. Tính toán Bounding Box hiện tại
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var contour in fontContours)
            {
                foreach (var pt in contour)
                {
                    if (pt.X < minX) minX = pt.X;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }
            }

            // 4. Canh giữa ngang (Auto Center Horizontal) nếu được chọn
            int shiftX = 0;
            if (options.AutoCenterHorizontal && maxX >= minX)
            {
                int inkWidth = maxX - minX;
                int targetLsb = Math.Max(0, (options.AdvanceWidth - inkWidth) / 2);
                shiftX = targetLsb - minX;

                if (shiftX != 0)
                {
                    foreach (var contour in fontContours)
                    {
                        for (int pi = 0; pi < contour.Count; pi++)
                        {
                            var pt = contour[pi];
                            pt.X += shiftX;
                            contour[pi] = pt;
                        }
                    }
                    minX += shiftX;
                    maxX += shiftX;
                }
            }

            // 5. Chuẩn hóa chiều Winding theo quy tắc TrueType:
            // Khung ngoài (Outer): Theo chiều kim đồng hồ (Clockwise, Shoelace Area < 0).
            // Lỗ rỗng trong (Inner Hole): Ngược chiều kim đồng hồ (Counter-Clockwise, Shoelace Area > 0).
            NormalizeContourWindings(fontContours);

            var outline = new RawGlyphOutline
            {
                NumberOfContours = (short)fontContours.Count,
                XMin = (short)minX,
                YMin = (short)minY,
                XMax = (short)maxX,
                YMax = (short)maxY,
                AdvanceWidth = options.AdvanceWidth,
                LeftSideBearing = (short)minX,
                Contours = fontContours
            };

            return outline;
        }

        #region Coordinate Mapping & Path Parsing
        private static Point MapCanvasPointToFont(
            PointF p,
            RectangleF canvasSourceRect,
            GlyphMetricsOptions options,
            float scaleX,
            float scaleY)
        {
            // Canvas: Top = 0, Bottom = canvasSourceRect.Height
            // Font: Y = 0 (Baseline), Y = Ascender (Top), Y = Descender (Bottom)
            // CJK glyph top thường căn ở Ascender - 5%..10% Em
            float fontX = (p.X - canvasSourceRect.X) * scaleX;

            // Lật trục Y: Font Y đi lên
            float normY = (p.Y - canvasSourceRect.Y) / canvasSourceRect.Height;
            float fontY = options.Ascender - (normY * (options.Ascender - options.Descender)) + options.VerticalBaselineOffset;

            return new Point((int)Math.Round(fontX), (int)Math.Round(fontY));
        }

        private static List<GraphicsPath> ExtractSubpaths(GraphicsPath path)
        {
            var list = new List<GraphicsPath>();
            if (path == null || path.PointCount == 0) return list;

            using (var it = new GraphicsPathIterator(path))
            {
                int count = it.SubpathCount;
                for (int i = 0; i < count; i++)
                {
                    var sp = new GraphicsPath();
                    bool isClosed;
                    it.NextSubpath(sp, out isClosed);
                    if (sp.PointCount > 0)
                    {
                        list.Add(sp);
                    }
                    else
                    {
                        sp.Dispose();
                    }
                }
            }
            return list;
        }

        private static List<OutlinePoint> ConvertSubpathToFont(
            GraphicsPath sp,
            RectangleF canvasSourceRect,
            GlyphMetricsOptions options,
            float scaleX,
            float scaleY)
        {
            var result = new List<OutlinePoint>();
            var pts = sp.PathPoints;
            var types = sp.PathTypes;
            int n = pts.Length;
            if (n < 2) return result;

            Point startPt = MapCanvasPointToFont(pts[0], canvasSourceRect, options, scaleX, scaleY);
            result.Add(new OutlinePoint(startPt.X, startPt.Y, true));
            Point lastPt = startPt;

            int i = 1;
            while (i < n)
            {
                byte type = (byte)(types[i] & 0x07);
                if (type == 1) // Line
                {
                    Point p1 = MapCanvasPointToFont(pts[i], canvasSourceRect, options, scaleX, scaleY);
                    result.Add(new OutlinePoint(p1.X, p1.Y, true));
                    lastPt = p1;
                    i++;
                }
                else if (type == 3 && i + 2 < n) // Cubic Bézier (3 điểm liên tiếp: p1, p2, p3)
                {
                    Point p0 = lastPt;
                    Point p1 = MapCanvasPointToFont(pts[i], canvasSourceRect, options, scaleX, scaleY);
                    Point p2 = MapCanvasPointToFont(pts[i + 1], canvasSourceRect, options, scaleX, scaleY);
                    Point p3 = MapCanvasPointToFont(pts[i + 2], canvasSourceRect, options, scaleX, scaleY);

                    var quads = ApproximateCubicWithQuadratics(p0, p1, p2, p3, DefaultTolerance);
                    result.AddRange(quads);
                    lastPt = p3;
                    i += 3;
                }
                else
                {
                    i++;
                }
            }

            // Nếu điểm kết thúc vô tình lặp lại điểm bắt đầu, bỏ điểm cuối để tránh suy biến trong TrueType
            if (result.Count > 1 &&
                result[result.Count - 1].X == result[0].X &&
                result[result.Count - 1].Y == result[0].Y)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }
        #endregion

        #region Cubic to Quadratic Bézier Approximation
        /// <summary>
        /// Thuật toán hạ bậc đường cong Cubic Bézier (bậc 3) sang một hoặc nhiều phân đoạn Quadratic Bézier (bậc 2)
        /// sử dụng phép chia nhỏ đệ quy de Casteljau đảm bảo sai số không vượt quá tolerance.
        /// </summary>
        private static List<OutlinePoint> ApproximateCubicWithQuadratics(
            Point p0, Point p1, Point p2, Point p3, float tolerance, int depth = 0)
        {
            var result = new List<OutlinePoint>();

            // Tính điểm điều khiển bậc 2 tối ưu: Q1 = (3*(P1 + P2) - (P0 + P3)) / 4
            float q1X = (3f * (p1.X + p2.X) - (p0.X + p3.X)) / 4f;
            float q1Y = (3f * (p1.Y + p2.Y) - (p0.Y + p3.Y)) / 4f;

            // Kiểm tra độ lệch tại t = 0.5:
            // B3(0.5) = (P0 + 3*P1 + 3*P2 + P3) / 8
            // B2(0.5) = (P0 + 2*Q1 + P3) / 4
            float b3HalfX = (p0.X + 3f * p1.X + 3f * p2.X + p3.X) / 8f;
            float b3HalfY = (p0.Y + 3f * p1.Y + 3f * p2.Y + p3.Y) / 8f;

            float b2HalfX = (p0.X + 2f * q1X + p3.X) / 4f;
            float b2HalfY = (p0.Y + 2f * q1Y + p3.Y) / 4f;

            float diffX = b3HalfX - b2HalfX;
            float diffY = b3HalfY - b2HalfY;
            float errorSq = diffX * diffX + diffY * diffY;

            // Nếu sai số nhỏ hơn hoặc bằng tolerance, hoặc đã phân chia sâu quá 5 tầng
            if (errorSq <= tolerance * tolerance || depth >= 5)
            {
                // Thêm 1 điểm off-curve (Control point) và 1 điểm on-curve (End point)
                result.Add(new OutlinePoint((int)Math.Round(q1X), (int)Math.Round(q1Y), false));
                result.Add(new OutlinePoint(p3.X, p3.Y, true));
                return result;
            }

            // Ngược lại, chia đôi cubic tại t = 0.5 theo de Casteljau
            Point midP01 = Midpoint(p0, p1);
            Point midP12 = Midpoint(p1, p2);
            Point midP23 = Midpoint(p2, p3);

            Point midP012 = Midpoint(midP01, midP12);
            Point midP123 = Midpoint(midP12, midP23);

            Point midP0123 = Midpoint(midP012, midP123);

            // Nửa đầu: [p0, midP01, midP012, midP0123]
            var left = ApproximateCubicWithQuadratics(p0, midP01, midP012, midP0123, tolerance, depth + 1);
            result.AddRange(left);

            // Nửa sau: [midP0123, midP123, midP23, p3]
            var right = ApproximateCubicWithQuadratics(midP0123, midP123, midP23, p3, tolerance, depth + 1);
            result.AddRange(right);

            return result;
        }

        private static Point Midpoint(Point a, Point b)
        {
            return new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        }
        #endregion

        #region Winding Direction (Shoelace Algorithm)
        /// <summary>
        /// Tính diện tích đa giác có dấu theo công thức Shoelace.
        /// Trong hệ tọa độ TrueType (Y hướng lên):
        /// - Diện tích âm (SignedArea &lt; 0): Chiều kim đồng hồ (Clockwise).
        /// - Diện tích dương (SignedArea &gt; 0): Ngược chiều kim đồng hồ (Counter-Clockwise).
        /// </summary>
        public static double CalculateSignedArea(List<OutlinePoint> contour)
        {
            int n = contour.Count;
            if (n < 3) return 0.0;

            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                sum += (double)contour[i].X * contour[next].Y - (double)contour[next].X * contour[i].Y;
            }
            return sum * 0.5;
        }

        /// <summary>
        /// Chuẩn hóa hướng xoay của các contour theo chuẩn TrueType:
        /// - Đường biên ngoài (Outer contour): Theo chiều kim đồng hồ (Clockwise, Shoelace Area &lt; 0).
        /// - Lỗ rỗng trong (Inner Hole): Ngược chiều kim đồng hồ (Counter-Clockwise, Shoelace Area &gt; 0).
        /// Một contour chỉ là Inner Hole khi và chỉ khi:
        /// 1. Bounding box của nó nằm lọt hoàn toàn trong Bounding box của một contour bao quanh lớn hơn.
        /// 2. Điểm tâm của nó nằm bên trong contour bao quanh đó.
        /// 3. Diện tích contour bao quanh lớn hơn rõ rệt (tránh nhầm lẫn khi 2 nét chồng đè/giao nhau).
        /// </summary>
        private static void NormalizeContourWindings(List<List<OutlinePoint>> contours)
        {
            int count = contours.Count;
            if (count == 0) return;

            // 1. Tính Bounding Box và Diện tích cho từng contour
            var boxes = new List<Rectangle>();
            var areas = new List<double>();
            for (int i = 0; i < count; i++)
            {
                var c = contours[i];
                double a = CalculateSignedArea(c);
                areas.Add(a);

                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var p in c)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
                boxes.Add(new Rectangle(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY)));
            }

            // 2. Phân tích quan hệ bao quanh để xác định Hole vs Outer
            for (int i = 0; i < count; i++)
            {
                var c = contours[i];
                double area = areas[i];
                if (Math.Abs(area) < 1.0) continue;

                var boxI = boxes[i];
                int midX = (boxI.Left + boxI.Right) / 2;
                int midY = (boxI.Top + boxI.Bottom) / 2;

                int enclosingCount = 0;
                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    var boxJ = boxes[j];

                    // Bounding box của j phải bao trọn bounding box của i (với biên dung sai 2 đơn vị font)
                    bool boxEnclosed = (boxJ.Left <= boxI.Left + 2 && boxJ.Right >= boxI.Right - 2 &&
                                        boxJ.Top <= boxI.Top + 2 && boxJ.Bottom >= boxI.Bottom - 2);

                    if (boxEnclosed && Math.Abs(areas[j]) > Math.Abs(area) * 1.1)
                    {
                        if (IsPointInsideContour(midX, midY, contours[j]))
                        {
                            enclosingCount++;
                        }
                    }
                }

                bool isHole = (enclosingCount % 2 != 0);

                // Trong TrueType: Outer cần Clockwise (Area < 0), Hole cần Counter-Clockwise (Area > 0)
                if (!isHole && area > 0)
                {
                    c.Reverse();
                }
                else if (isHole && area < 0)
                {
                    c.Reverse();
                }
            }
        }

        private static bool IsPointInsideContour(int px, int py, List<OutlinePoint> contour)
        {
            bool inside = false;
            int n = contour.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                int xi = contour[i].X, yi = contour[i].Y;
                int xj = contour[j].X, yj = contour[j].Y;

                bool intersect = ((yi > py) != (yj > py)) &&
                    (px < (long)(xj - xi) * (py - yi) / (yj - yi) + xi);
                if (intersect) inside = !inside;
            }
            return inside;
        }
        #endregion
    }
}
