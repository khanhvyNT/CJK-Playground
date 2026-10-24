using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;

namespace CJKPlayground.Services
{
    public class InkBoundingBox
    {
        public int MinX { get; set; }
        public int MinY { get; set; }
        public int MaxX { get; set; }
        public int MaxY { get; set; }
        public int Width { get { return Math.Max(0, MaxX - MinX); } }
        public int Height { get { return Math.Max(0, MaxY - MinY); } }
        public double CenterX { get { return (MinX + MaxX) / 2.0; } }
        public double CenterY { get { return (MinY + MaxY) / 2.0; } }
    }

    public static class VectorPathRenderer
    {
        public static string BuildSvgPath(RawGlyphOutline glyph, ushort unitsPerEm, short ascender)
        {
            if (glyph == null || glyph.Contours.Count == 0)
                return "";

            var sb = new StringBuilder();

            foreach (var contour in glyph.Contours)
            {
                if (contour.Count == 0) continue;

                // Expand contour with implicit midpoints between consecutive off-curve points
                var points = ExpandContourPoints(contour);
                if (points.Count == 0) continue;

                // First point
                var p0 = points[0];
                int startY = ascender - p0.Y;
                sb.AppendFormat("M {0} {1} ", p0.X, startY);

                int i = 1;
                while (i < points.Count)
                {
                    var curr = points[i];
                    if (curr.OnCurve)
                    {
                        // Straight line
                        int y = ascender - curr.Y;
                        sb.AppendFormat("L {0} {1} ", curr.X, y);
                        i++;
                    }
                    else
                    {
                        // Quadratic curve to next on-curve point
                        var control = curr;
                        var next = (i + 1 < points.Count) ? points[i + 1] : points[0];
                        int cy = ascender - control.Y;
                        int ny = ascender - next.Y;
                        sb.AppendFormat("Q {0} {1} {2} {3} ", control.X, cy, next.X, ny);
                        i += 2;
                    }
                }

                sb.Append("Z ");
            }

            return sb.ToString().Trim();
        }

        private static List<OutlinePoint> ExpandContourPoints(List<OutlinePoint> contour)
        {
            var result = new List<OutlinePoint>();
            int n = contour.Count;
            if (n == 0) return result;

            // Handle start point if off-curve
            int startIndex = 0;
            if (!contour[0].OnCurve)
            {
                if (contour[n - 1].OnCurve)
                {
                    // Start at last point
                    startIndex = n - 1;
                    result.Add(contour[n - 1]);
                }
                else
                {
                    // Implicit midpoint between last and first
                    int midX = (contour[n - 1].X + contour[0].X) / 2;
                    int midY = (contour[n - 1].Y + contour[0].Y) / 2;
                    result.Add(new OutlinePoint(midX, midY, true));
                }
            }
            else
            {
                result.Add(contour[0]);
                startIndex = 0;
            }

            for (int i = 0; i < n; i++)
            {
                int currIdx = (startIndex + i) % n;
                int nextIdx = (startIndex + i + 1) % n;
                var curr = contour[currIdx];
                var next = contour[nextIdx];

                if (!curr.OnCurve && !next.OnCurve)
                {
                    // Insert implicit on-curve midpoint
                    int midX = (curr.X + next.X) / 2;
                    int midY = (curr.Y + next.Y) / 2;
                    result.Add(curr);
                    result.Add(new OutlinePoint(midX, midY, true));
                }
                else
                {
                    result.Add(next);
                }
            }

            return result;
        }

        public static InkBoundingBox CalculateInkBounds(RawGlyphOutline glyph)
        {
            if (glyph == null || glyph.Contours.Count == 0)
                return new InkBoundingBox();

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            foreach (var contour in glyph.Contours)
            {
                foreach (var pt in contour)
                {
                    if (pt.X < minX) minX = pt.X;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }
            }

            if (minX == int.MaxValue)
            {
                minX = glyph.XMin;
                maxX = glyph.XMax;
                minY = glyph.YMin;
                maxY = glyph.YMax;
            }

            return new InkBoundingBox
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            };
        }

        public static string GenerateFullSvg(RawGlyphOutline glyph, ushort unitsPerEm, short ascender, short descender, string fontName)
        {
            string pathData = BuildSvgPath(glyph, unitsPerEm, ascender);
            int width = glyph != null && glyph.AdvanceWidth > 0 ? glyph.AdvanceWidth : unitsPerEm;
            int height = unitsPerEm > 0 ? unitsPerEm : 1000;
            int totalHeight = ascender - descender;
            if (totalHeight <= 0) totalHeight = height;

            var ink = CalculateInkBounds(glyph);

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendFormat("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0} {1}\" width=\"{0}\" height=\"{1}\">\n", width, totalHeight);
            sb.AppendFormat("  <!-- Font: {0} | Advance: {1} | UnitsPerEm: {2} | Ink Box: [{3},{4}]-[{5},{6}] -->\n",
                fontName ?? "CJKPlayground", width, unitsPerEm, ink.MinX, ink.MinY, ink.MaxX, ink.MaxY);
            sb.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"none\"/>");

            if (!string.IsNullOrEmpty(pathData))
            {
                sb.AppendFormat("  <path d=\"{0}\" fill=\"#1e293b\" fill-rule=\"nonzero\"/>\n", pathData);
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static GraphicsPath CreateGraphicsPath(RawGlyphOutline glyph, ushort unitsPerEm, short ascender, float targetSize)
        {
            var gp = new GraphicsPath();
            if (glyph == null || glyph.Contours.Count == 0 || unitsPerEm == 0)
                return gp;

            float scale = targetSize / unitsPerEm;

            foreach (var contour in glyph.Contours)
            {
                var points = ExpandContourPoints(contour);
                if (points.Count == 0) continue;

                var poly = new List<PointF>();
                foreach (var pt in points)
                {
                    float x = pt.X * scale;
                    float y = (ascender - pt.Y) * scale;
                    poly.Add(new PointF(x, y));
                }

                if (poly.Count >= 3)
                {
                    gp.AddLines(poly.ToArray());
                    gp.CloseFigure();
                }
            }

            return gp;
        }
    }
}
