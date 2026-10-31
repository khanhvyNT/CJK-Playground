using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CJKPlayground.Services
{
    /// <summary>
    /// Cung cấp các thuật toán hình học vector chính xác:
    /// - Lasso Tool: Kiểm tra điểm trong đa giác tự do (Ray Casting Point-in-Polygon).
    /// - Lasso Cut / Knife Tool: Cắt phân rã đường nét hoặc đường bao thành các phân đoạn độc lập.
    /// - Automatic Arrange & Auto-Join: Bắt dính đầu mút gần nhất, gióng vector tiếp tuyến và hàn liền các nét cắt.
    /// </summary>
    public static class IdsVectorGeometryService
    {
        private const float Epsilon = 1e-5f;

        /// <summary>
        /// Thuật toán Ray-Casting kiểm tra một điểm có nằm trong đa giác tự do hay không.
        /// </summary>
        public static bool IsPointInPolygon(PointF pt, IList<PointF> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                if (((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Đếm số điểm nằm trong đa giác Lasso và trả về danh sách các chỉ số (indices) của điểm neo được chọn.
        /// Chuẩn theo cơ chế Lasso Tool của Adobe Illustrator.
        /// </summary>
        public static int CountPointsInPolygon(PointF[] points, IList<PointF> polygon, out List<int> insideIndices)
        {
            insideIndices = new List<int>();
            if (points == null || polygon == null || polygon.Count < 3) return 0;

            for (int i = 0; i < points.Length; i++)
            {
                if (IsPointInPolygon(points[i], polygon))
                {
                    insideIndices.Add(i);
                }
            }
            return insideIndices.Count;
        }

        /// <summary>
        /// Kiểm tra xem một đường nét vector (GraphicsPath) có nằm trong hoặc cắt qua vùng Lasso hay không.
        /// Trả về danh sách tất cả các điểm neo (anchor points) lọt vào trong vùng khoanh.
        /// Hỗ trợ cả khoanh quanh điểm neo, khoanh cắt ngang nét, và khoanh lên thân nét (ink fill).
        /// </summary>
        public static bool IsPathInPolygon(GraphicsPath path, IList<PointF> polygon, out List<int> insideIndices)
        {
            insideIndices = new List<int>();
            if (path == null || path.PointCount == 0 || polygon == null || polygon.Count < 3) return false;

            PointF[] pts = path.PathPoints;
            byte[] types = path.PathTypes;

            // 1. Chuẩn Illustrator: Thu thập tất cả điểm neo thực sự nằm trong đa giác Lasso
            CountPointsInPolygon(pts, polygon, out insideIndices);
            if (insideIndices.Count > 0)
            {
                return true;
            }

            var set = new HashSet<int>();
            int subpathStart = 0;

            // 2. Tìm tất cả các đoạn thẳng/đường cong cắt ngang qua cạnh của vòng Lasso
            for (int i = 0; i < pts.Length; i++)
            {
                if ((types[i] & 0x07) == 0) // Start of subpath
                {
                    subpathStart = i;
                }

                int next = i + 1;
                if (i + 1 == pts.Length || (types[i + 1] & 0x07) == 0)
                {
                    next = subpathStart;
                }

                PointF p1 = pts[i];
                PointF p2 = pts[next];

                for (int j = 0; j < polygon.Count; j++)
                {
                    PointF q1 = polygon[j];
                    PointF q2 = polygon[(j + 1) % polygon.Count];
                    PointF inter;
                    if (LineSegmentsIntersect(p1, p2, q1, q2, out inter))
                    {
                        set.Add(i);
                        set.Add(next);
                        break;
                    }
                }
            }

            // 3. Nếu người dùng khoanh trực tiếp vào thân nét vẽ (ink) mà điểm neo nằm ngoài
            if (set.Count == 0)
            {
                for (int j = 0; j < polygon.Count; j++)
                {
                    if (path.IsVisible(polygon[j]))
                    {
                        // Điểm của Lasso nằm trên thân nét: tìm điểm neo gần nhất
                        float minDistSq = float.MaxValue;
                        int closestIdx = -1;
                        for (int k = 0; k < pts.Length; k++)
                        {
                            float d = (pts[k].X - polygon[j].X) * (pts[k].X - polygon[j].X) +
                                      (pts[k].Y - polygon[j].Y) * (pts[k].Y - polygon[j].Y);
                            if (d < minDistSq)
                            {
                                minDistSq = d;
                                closestIdx = k;
                            }
                        }
                        if (closestIdx >= 0)
                        {
                            set.Add(closestIdx);
                        }
                    }
                }
            }

            insideIndices = new List<int>(set);
            insideIndices.Sort();
            return insideIndices.Count > 0;
        }

        /// <summary>
        /// Kiểm tra một hình chữ nhật bao (bounding box) có được chọn bởi vòng Lasso hay không.
        /// Chấp nhận nếu tâm hoặc ít nhất 2 góc nằm trong Lasso polygon.
        /// </summary>
        public static bool IsBoundsInPolygon(RectangleF bounds, IList<PointF> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            // 1. Kiểm tra tâm hình chữ nhật
            PointF center = new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);
            if (IsPointInPolygon(center, polygon)) return true;

            // 2. Đếm số góc nằm trong đa giác
            int insideCorners = 0;
            PointF[] corners = new PointF[]
            {
                new PointF(bounds.Left, bounds.Top),
                new PointF(bounds.Right, bounds.Top),
                new PointF(bounds.Right, bounds.Bottom),
                new PointF(bounds.Left, bounds.Bottom)
            };

            foreach (var c in corners)
            {
                if (IsPointInPolygon(c, polygon)) insideCorners++;
            }
            if (insideCorners >= 2) return true;

            return false;
        }

        /// <summary>
        /// Tính giao điểm giữa 2 đoạn thẳng (p1-p2) và (p3-p4).
        /// </summary>
        public static bool LineSegmentsIntersect(PointF p1, PointF p2, PointF p3, PointF p4, out PointF intersection)
        {
            intersection = PointF.Empty;

            float d = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
            if (Math.Abs(d) < Epsilon) return false;

            float t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / d;
            float u = -((p1.X - p2.X) * (p1.Y - p3.Y) - (p1.Y - p2.Y) * (p1.X - p3.X)) / d;

            if (t >= 0.0f && t <= 1.0f && u >= 0.0f && u <= 1.0f)
            {
                intersection = new PointF(
                    p1.X + t * (p2.X - p1.X),
                    p1.Y + t * (p2.Y - p1.Y)
                );
                return true;
            }

            return false;
        }

        /// <summary>
        /// Cắt một nét bút (GraphicsPath) bởi một đoạn thẳng cắt (cutStart - cutEnd).
        /// Tách nét thành 2 phần độc lập tại điểm giao.
        /// </summary>
        public static bool TryCutStroke(
            GraphicsPath stroke,
            PointF cutStart,
            PointF cutEnd,
            out GraphicsPath part1,
            out GraphicsPath part2,
            out PointF junctionPoint)
        {
            part1 = null;
            part2 = null;
            junctionPoint = PointF.Empty;

            if (stroke == null || stroke.PointCount < 2) return false;

            var pts = stroke.PathPoints;
            int cutIndex = -1;
            PointF interPt = PointF.Empty;

            for (int i = 0; i < pts.Length - 1; i++)
            {
                if (LineSegmentsIntersect(pts[i], pts[i + 1], cutStart, cutEnd, out interPt))
                {
                    cutIndex = i;
                    break;
                }
            }

            if (cutIndex < 0) return false;

            // Xây dựng đoạn 1 (từ 0 đến cutIndex kèm điểm giao)
            var list1 = new List<PointF>();
            for (int i = 0; i <= cutIndex; i++) list1.Add(pts[i]);
            list1.Add(interPt);

            // Xây dựng đoạn 2 (từ điểm giao đến cuối)
            var list2 = new List<PointF>();
            list2.Add(interPt);
            for (int i = cutIndex + 1; i < pts.Length; i++) list2.Add(pts[i]);

            if (list1.Count >= 2 && list2.Count >= 2)
            {
                part1 = new GraphicsPath();
                part1.AddLines(list1.ToArray());

                part2 = new GraphicsPath();
                part2.AddLines(list2.ToArray());

                junctionPoint = interPt;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Xóa các điểm neo (anchor points) được chỉ định khỏi GraphicsPath mà vẫn bảo toàn các đường nét còn lại.
        /// Chuẩn Adobe Illustrator khi người dùng chọn điểm neo bằng Lasso hoặc Selection Box rồi nhấn Delete/Cut.
        /// </summary>
        public static GraphicsPath RemoveAnchorPoints(GraphicsPath path, ICollection<int> removeIndices)
        {
            if (path == null || path.PointCount == 0) return new GraphicsPath();
            if (removeIndices == null || removeIndices.Count == 0) return (GraphicsPath)path.Clone();

            var origPts = path.PathPoints;
            var origTypes = path.PathTypes;

            var subpaths = new List<Tuple<List<PointF>, List<byte>>>();
            List<PointF> curPts = null;
            List<byte> curTypes = null;

            for (int i = 0; i < origPts.Length; i++)
            {
                byte t = origTypes[i];
                bool isStart = (t & 0x07) == 0; // PathPointType.Start

                if (isStart || curPts == null)
                {
                    if (curPts != null && curPts.Count >= 2)
                    {
                        subpaths.Add(Tuple.Create(curPts, curTypes));
                    }
                    curPts = new List<PointF>();
                    curTypes = new List<byte>();
                }

                if (!removeIndices.Contains(i))
                {
                    curPts.Add(origPts[i]);
                    curTypes.Add(t);
                }
            }
            if (curPts != null && curPts.Count >= 2)
            {
                subpaths.Add(Tuple.Create(curPts, curTypes));
            }

            var finalPts = new List<PointF>();
            var finalTypes = new List<byte>();

            foreach (var sp in subpaths)
            {
                var pts = sp.Item1;
                var types = sp.Item2;
                if (pts.Count < 2) continue;

                types[0] = 0;

                for (int k = 1; k < types.Count - 1; k++)
                {
                    types[k] = (byte)(types[k] & 0x07);
                    if ((types[k] & 0x07) == 0) types[k] = 1;
                }

                finalPts.AddRange(pts);
                finalTypes.AddRange(types);
            }

            if (finalPts.Count >= 2)
            {
                var res = new GraphicsPath(finalPts.ToArray(), finalTypes.ToArray());
                res.FillMode = path.FillMode;
                return res;
            }

            return new GraphicsPath();
        }

        /// <summary>
        /// Trích xuất các điểm neo hoặc đường nét được chọn thành một GraphicsPath độc lập (cho thao tác Cut/Copy).
        /// </summary>
        public static GraphicsPath ExtractAnchorPoints(GraphicsPath path, ICollection<int> indices)
        {
            if (path == null || path.PointCount == 0 || indices == null || indices.Count == 0) return new GraphicsPath();

            var origPts = path.PathPoints;
            var origTypes = path.PathTypes;

            var subpaths = new List<Tuple<List<PointF>, List<byte>>>();
            List<PointF> curPts = null;
            List<byte> curTypes = null;

            for (int i = 0; i < origPts.Length; i++)
            {
                byte t = origTypes[i];
                bool isStart = (t & 0x07) == 0;

                if (isStart || curPts == null)
                {
                    if (curPts != null && curPts.Count >= 2)
                    {
                        subpaths.Add(Tuple.Create(curPts, curTypes));
                    }
                    curPts = new List<PointF>();
                    curTypes = new List<byte>();
                }

                if (indices.Contains(i))
                {
                    curPts.Add(origPts[i]);
                    curTypes.Add(t);
                }
            }
            if (curPts != null && curPts.Count >= 2)
            {
                subpaths.Add(Tuple.Create(curPts, curTypes));
            }

            var finalPts = new List<PointF>();
            var finalTypes = new List<byte>();

            foreach (var sp in subpaths)
            {
                var pts = sp.Item1;
                var types = sp.Item2;
                if (pts.Count < 2) continue;

                types[0] = 0; // PathPointType.Start
                for (int k = 1; k < types.Count; k++)
                {
                    byte ptType = (byte)(types[k] & 0x07);
                    if (ptType == 0) ptType = 1; // PathPointType.Line
                    if (k < types.Count - 1)
                    {
                        types[k] = ptType;
                    }
                    else
                    {
                        types[k] = (byte)(ptType | (types[k] & 0x80));
                    }
                }

                finalPts.AddRange(pts);
                finalTypes.AddRange(types);
            }

            if (finalPts.Count >= 2)
            {
                var res = new GraphicsPath(finalPts.ToArray(), finalTypes.ToArray());
                res.FillMode = path.FillMode;
                return res;
            }

            return new GraphicsPath();
        }

        private struct EdgeIntersection
        {
            public int EdgeIndex;
            public float EdgeParam;
            public PointF Point;
        }

        /// <summary>
        /// Cắt một nét bút hở (GraphicsPath) bởi một quỹ đạo dao cắt đa điểm (cutTrajectory).
        /// </summary>
        public static bool TryCutPolyline(
            GraphicsPath stroke,
            IList<PointF> cutTrajectory,
            out GraphicsPath part1,
            out GraphicsPath part2,
            out PointF junctionPoint)
        {
            part1 = null;
            part2 = null;
            junctionPoint = PointF.Empty;

            if (stroke == null || stroke.PointCount < 2 || cutTrajectory == null || cutTrajectory.Count < 2)
                return false;

            var pts = stroke.PathPoints;
            int cutIndex = -1;
            PointF interPt = PointF.Empty;

            for (int j = 0; j < cutTrajectory.Count - 1; j++)
            {
                PointF kA = cutTrajectory[j];
                PointF kB = cutTrajectory[j + 1];

                for (int i = 0; i < pts.Length - 1; i++)
                {
                    if (LineSegmentsIntersect(pts[i], pts[i + 1], kA, kB, out interPt))
                    {
                        cutIndex = i;
                        break;
                    }
                }
                if (cutIndex >= 0) break;
            }

            if (cutIndex < 0) return false;

            var list1 = new List<PointF>();
            for (int i = 0; i <= cutIndex; i++) list1.Add(pts[i]);
            list1.Add(interPt);

            var list2 = new List<PointF>();
            list2.Add(interPt);
            for (int i = cutIndex + 1; i < pts.Length; i++) list2.Add(pts[i]);

            if (list1.Count >= 2 && list2.Count >= 2)
            {
                part1 = new GraphicsPath();
                part1.AddLines(list1.ToArray());

                part2 = new GraphicsPath();
                part2.AddLines(list2.ToArray());

                junctionPoint = interPt;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Cắt một compound path (chứa một hoặc nhiều đường viền khép kín hoặc đường nét hở)
        /// bằng quỹ đạo cắt đa đoạn (cutTrajectory), bao gồm cả khoanh vòng (Encircled Loop Cut)
        /// và rạch ngang (Knife Slice Cut).
        /// Bảo toàn toàn bộ phần thân chữ còn lại trong remainingCompound và trả về đoạn nét cắt rời trong severedPart.
        /// </summary>
        public static bool TryCutCompoundPathByTrajectory(
            GraphicsPath compound,
            IList<PointF> cutTrajectory,
            out GraphicsPath remainingCompound,
            out GraphicsPath severedPart,
            out PointF junction)
        {
            remainingCompound = null;
            severedPart = null;
            junction = PointF.Empty;

            if (compound == null || compound.PointCount < 2 || cutTrajectory == null || cutTrajectory.Count < 2)
                return false;

            using (var iter = new GraphicsPathIterator(compound))
            {
                int count = iter.SubpathCount;
                if (count == 0) return false;

                iter.Rewind();
                var subpaths = new List<GraphicsPath>();
                var isClosedList = new List<bool>();
                for (int i = 0; i < count; i++)
                {
                    var sub = new GraphicsPath();
                    bool isClosed;
                    iter.NextSubpath(sub, out isClosed);
                    subpaths.Add(sub);
                    isClosedList.Add(isClosed);
                }

                var cutSegs = new List<Tuple<PointF, PointF>>();
                for (int j = 0; j < cutTrajectory.Count - 1; j++)
                {
                    cutSegs.Add(Tuple.Create(cutTrajectory[j], cutTrajectory[j + 1]));
                }

                PointF pStart = cutTrajectory[0];
                PointF pEnd = cutTrajectory[cutTrajectory.Count - 1];
                float closeDist = (float)Math.Sqrt((pStart.X - pEnd.X) * (pStart.X - pEnd.X) + (pStart.Y - pEnd.Y) * (pStart.Y - pEnd.Y));
                bool isLoop = (cutTrajectory.Count >= 3 && closeDist < 120f);

                int hitSubIdx = -1;
                GraphicsPath subRemaining = null;
                GraphicsPath subSevered = null;

                for (int s = 0; s < subpaths.Count; s++)
                {
                    var sub = subpaths[s];
                    bool isClosed = isClosedList[s];
                    var pts = sub.PathPoints;
                    int n = pts.Length;

                    List<int> insideIndices = null;
                    int insideCount = 0;
                    if (cutTrajectory.Count >= 3)
                    {
                        insideCount = CountPointsInPolygon(pts, cutTrajectory, out insideIndices);
                        if (insideCount > 0 && !isLoop)
                        {
                            isLoop = true;
                        }
                    }

                    var activeCutSegs = new List<Tuple<PointF, PointF>>(cutSegs);
                    if (isLoop && cutTrajectory.Count >= 3)
                    {
                        activeCutSegs.Add(Tuple.Create(pEnd, pStart));
                    }

                    // 1. Nếu toàn bộ subpath nằm trong vòng khoanh kín
                    if (isLoop && insideCount == n)
                    {
                        hitSubIdx = s;
                        subSevered = (GraphicsPath)sub.Clone();
                        subRemaining = null;
                        junction = pts[0];
                        break;
                    }

                    // 2. Nếu là subpath hở (đường nét vẽ tự do)
                    if (!isClosed)
                    {
                        GraphicsPath p1, p2;
                        PointF junc;
                        if (TryCutPolyline(sub, cutTrajectory, out p1, out p2, out junc))
                        {
                            hitSubIdx = s;
                            subRemaining = p1;
                            subSevered = p2;
                            junction = junc;
                            break;
                        }
                        continue;
                    }

                    // 3. Nếu là đường viền khép kín (Contour của ký tự CJK)
                    var inters = new List<EdgeIntersection>();
                    for (int i = 0; i < n; i++)
                    {
                        PointF pA = pts[i];
                        PointF pB = pts[(i + 1) % n];

                        foreach (var seg in activeCutSegs)
                        {
                            PointF inter;
                            if (LineSegmentsIntersect(pA, pB, seg.Item1, seg.Item2, out inter))
                            {
                                float dx = pB.X - pA.X;
                                float dy = pB.Y - pA.Y;
                                float lenSq = dx * dx + dy * dy;
                                float t = lenSq > 1e-6f ? ((inter.X - pA.X) * dx + (inter.Y - pA.Y) * dy) / lenSq : 0f;

                                inters.Add(new EdgeIntersection
                                {
                                    EdgeIndex = i,
                                    EdgeParam = t,
                                    Point = inter
                                });
                            }
                        }
                    }

                    if (inters.Count >= 2)
                    {
                        inters.Sort(delegate(EdgeIntersection a, EdgeIntersection b)
                        {
                            int cmp = a.EdgeIndex.CompareTo(b.EdgeIndex);
                            if (cmp != 0) return cmp;
                            return a.EdgeParam.CompareTo(b.EdgeParam);
                        });

                        var i1 = inters[0];
                        var i2 = inters[inters.Count - 1];
                        junction = i1.Point;

                        var listA = new List<PointF>();
                        listA.Add(i1.Point);
                        for (int k = i1.EdgeIndex + 1; k <= i2.EdgeIndex; k++) listA.Add(pts[k]);
                        listA.Add(i2.Point);

                        var listB = new List<PointF>();
                        listB.Add(i2.Point);
                        for (int k = i2.EdgeIndex + 1; k < n; k++) listB.Add(pts[k]);
                        for (int k = 0; k <= i1.EdgeIndex; k++) listB.Add(pts[k]);
                        listB.Add(i1.Point);

                        if (listA.Count >= 3 && listB.Count >= 3)
                        {
                            var gpA = new GraphicsPath();
                            gpA.AddLines(listA.ToArray());
                            gpA.CloseFigure();

                            var gpB = new GraphicsPath();
                            gpB.AddLines(listB.ToArray());
                            gpB.CloseFigure();

                            bool aIsSevered = false;
                            if (insideCount > 0 && insideIndices != null)
                            {
                                int inA = 0;
                                foreach (int idx in insideIndices)
                                {
                                    if (idx > i1.EdgeIndex && idx <= i2.EdgeIndex) inA++;
                                }
                                aIsSevered = (inA > insideCount / 2);
                            }
                            else
                            {
                                var bA = gpA.GetBounds();
                                var bB = gpB.GetBounds();
                                float areaA = bA.Width * bA.Height;
                                float areaB = bB.Width * bB.Height;
                                aIsSevered = (areaA < areaB);
                            }

                            hitSubIdx = s;
                            if (aIsSevered)
                            {
                                subSevered = gpA;
                                subRemaining = gpB;
                            }
                            else
                            {
                                subSevered = gpB;
                                subRemaining = gpA;
                            }
                            break;
                        }
                    }
                }

                if (hitSubIdx >= 0)
                {
                    remainingCompound = new GraphicsPath();
                    remainingCompound.FillMode = compound.FillMode;

                    for (int s = 0; s < subpaths.Count; s++)
                    {
                        if (s == hitSubIdx)
                        {
                            if (subRemaining != null && subRemaining.PointCount >= 2)
                            {
                                remainingCompound.AddPath(subRemaining, false);
                                subRemaining.Dispose();
                            }
                        }
                        else
                        {
                            if (subpaths[s].PointCount >= 2)
                            {
                                remainingCompound.AddPath(subpaths[s], false);
                            }
                        }
                        subpaths[s].Dispose();
                    }

                    severedPart = subSevered;
                    return true;
                }

                foreach (var s in subpaths) s.Dispose();
                return false;
            }
        }

        /// <summary>
        /// Cắt một nét trong compound path mà không làm ảnh hưởng đến các subpath còn lại của ký tự.
        /// </summary>
        public static bool TryCutCompoundPath(
            GraphicsPath compound,
            PointF cutStart,
            PointF cutEnd,
            out GraphicsPath remainingCompound,
            out GraphicsPath part1,
            out GraphicsPath part2,
            out PointF junction)
        {
            remainingCompound = null;
            part1 = null;
            part2 = null;
            junction = PointF.Empty;

            if (compound == null || compound.PointCount < 2) return false;

            using (var iter = new GraphicsPathIterator(compound))
            {
                int count = iter.SubpathCount;
                if (count == 0) return false;

                iter.Rewind();
                var subpaths = new List<GraphicsPath>();
                for (int i = 0; i < count; i++)
                {
                    var sub = new GraphicsPath();
                    bool isClosed;
                    iter.NextSubpath(sub, out isClosed);
                    subpaths.Add(sub);
                }

                int hitSubIdx = -1;
                for (int i = 0; i < subpaths.Count; i++)
                {
                    if (TryCutStroke(subpaths[i], cutStart, cutEnd, out part1, out part2, out junction))
                    {
                        hitSubIdx = i;
                        break;
                    }
                }

                if (hitSubIdx >= 0)
                {
                    remainingCompound = new GraphicsPath();
                    remainingCompound.FillMode = compound.FillMode;
                    for (int i = 0; i < subpaths.Count; i++)
                    {
                        if (i != hitSubIdx && subpaths[i].PointCount >= 2)
                        {
                            remainingCompound.AddPath(subpaths[i], false);
                        }
                    }
                    foreach (var s in subpaths) s.Dispose();
                    return true;
                }

                foreach (var s in subpaths) s.Dispose();
            }

            GraphicsPath severed;
            if (TryCutCompoundPathByTrajectory(compound, new PointF[] { cutStart, cutEnd }, out remainingCompound, out severed, out junction))
            {
                part1 = severed;
                part2 = new GraphicsPath();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Thuật toán Automatic Arrange & Auto-Join:
        /// 1. Tìm cặp đầu mút hở gần nhau nhất giữa các đoạn nét.
        /// 2. Gióng hàng chính xác (Arrange: Translation vector offset dx, dy) để khử sai số về 0.0px.
        /// 3. Hàn liền (Join) 2 đường nét thành một continuous GraphicsPath duy nhất.
        /// </summary>
        public static bool TryAutoArrangeAndJoin(
            List<GraphicsPath> strokes,
            float maxJoinDistance,
            out PointF joinedPoint,
            out string reportMessage)
        {
            joinedPoint = PointF.Empty;
            reportMessage = null;

            if (strokes == null || strokes.Count < 2)
            {
                reportMessage = "Cần ít nhất 2 đoạn nét để thực hiện ghép nối (Auto-Join).";
                return false;
            }

            float bestDistSq = maxJoinDistance * maxJoinDistance;
            int bestIdxA = -1;
            int bestIdxB = -1;
            bool endA = false; // true: đuôi stroke A, false: đầu stroke A
            bool endB = false; // true: đuôi stroke B, false: đầu stroke B

            for (int i = 0; i < strokes.Count; i++)
            {
                var sA = strokes[i];
                if (sA.PointCount < 2) continue;
                var ptsA = sA.PathPoints;
                PointF startA = ptsA[0];
                PointF tailA = ptsA[ptsA.Length - 1];

                for (int j = i + 1; j < strokes.Count; j++)
                {
                    var sB = strokes[j];
                    if (sB.PointCount < 2) continue;
                    var ptsB = sB.PathPoints;
                    PointF startB = ptsB[0];
                    PointF tailB = ptsB[ptsB.Length - 1];

                    // 4 kết hợp đầu mút:
                    // 1. Tail A -> Start B
                    float d1 = DistSq(tailA, startB);
                    if (d1 < bestDistSq)
                    {
                        bestDistSq = d1;
                        bestIdxA = i;
                        bestIdxB = j;
                        endA = true;
                        endB = false;
                    }

                    // 2. Tail A -> Tail B
                    float d2 = DistSq(tailA, tailB);
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        bestIdxA = i;
                        bestIdxB = j;
                        endA = true;
                        endB = true;
                    }

                    // 3. Start A -> Start B
                    float d3 = DistSq(startA, startB);
                    if (d3 < bestDistSq)
                    {
                        bestDistSq = d3;
                        bestIdxA = i;
                        bestIdxB = j;
                        endA = false;
                        endB = false;
                    }

                    // 4. Start A -> Tail B
                    float d4 = DistSq(startA, tailB);
                    if (d4 < bestDistSq)
                    {
                        bestDistSq = d4;
                        bestIdxA = i;
                        bestIdxB = j;
                        endA = false;
                        endB = true;
                    }
                }
            }

            if (bestIdxA < 0 || bestIdxB < 0)
            {
                reportMessage = string.Format("Không tìm thấy 2 đầu mút hở nào trong phạm vi bắt dính {0:0}px.", maxJoinDistance);
                return false;
            }

            var strokeA = strokes[bestIdxA];
            var strokeB = strokes[bestIdxB];

            var ptsListA = new List<PointF>(strokeA.PathPoints);
            var ptsListB = new List<PointF>(strokeB.PathPoints);

            // Chuẩn hóa thứ tự để nối đuôi A vào đầu B
            if (!endA)
            {
                ptsListA.Reverse();
            }
            if (endB)
            {
                ptsListB.Reverse();
            }

            PointF targetEndA = ptsListA[ptsListA.Count - 1];
            PointF currentStartB = ptsListB[0];

            // 1. AUTOMATIC ARRANGE: Tính vector dịch chuyển để đầu B bắt dính tuyệt đối vào đuôi A
            float dx = targetEndA.X - currentStartB.X;
            float dy = targetEndA.Y - currentStartB.Y;

            for (int k = 0; k < ptsListB.Count; k++)
            {
                ptsListB[k] = new PointF(ptsListB[k].X + dx, ptsListB[k].Y + dy);
            }

            // 2. AUTO-JOIN: Hàn liền 2 danh sách điểm thành 1 chuỗi liên tục
            var mergedPoints = new List<PointF>(ptsListA);
            // Bỏ qua điểm đầu của B vì đã trùng khớp tuyệt đối với điểm cuối của A
            for (int k = 1; k < ptsListB.Count; k++)
            {
                mergedPoints.Add(ptsListB[k]);
            }

            var newStroke = new GraphicsPath();
            newStroke.AddLines(mergedPoints.ToArray());

            // Nếu 2 đầu ngoài cùng cũng gần nhau dưới 25px, tự động khép kín contour
            if (DistSq(mergedPoints[0], mergedPoints[mergedPoints.Count - 1]) < 625f)
            {
                newStroke.CloseFigure();
            }

            // Cập nhật lại danh sách stroke
            strokeA.Dispose();
            strokeB.Dispose();

            // Xóa stroke có chỉ số lớn trước để không làm lệch index
            if (bestIdxA > bestIdxB)
            {
                strokes.RemoveAt(bestIdxA);
                strokes.RemoveAt(bestIdxB);
            }
            else
            {
                strokes.RemoveAt(bestIdxB);
                strokes.RemoveAt(bestIdxA);
            }

            strokes.Add(newStroke);
            joinedPoint = targetEndA;
            reportMessage = string.Format("Ghép nối Auto-Join thành công tại ({0:0}, {1:0}) — Bắt dính tuyệt đối (sai số 0.0px)!", targetEndA.X, targetEndA.Y);
            return true;
        }

        private static float DistSq(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
