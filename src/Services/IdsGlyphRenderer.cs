using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Text;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public static class IdsGlyphRenderer
    {
        public static FontFamily ResolveFontFamily(string fontName = null)
        {
            if (!string.IsNullOrEmpty(fontName))
            {
                try
                {
                    using (var test = new Font(fontName, 12f))
                    {
                        if (test.Name.Equals(fontName, StringComparison.OrdinalIgnoreCase))
                        {
                            return test.FontFamily;
                        }
                    }
                }
                catch { }
            }

            // Fallback list of common CJK fonts on Windows
            string[] preferred = new[]
            {
                "Nom Na Tong", "SimSun", "NSimSun", "Microsoft YaHei", "MingLiU",
                "MS Gothic", "Malgun Gothic", "KaiTi", "SimSun-ExtB", "Segoe UI Symbol"
            };

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

            return FontFamily.GenericSansSerif;
        }

        private static FontFamily _cachedExtBFamily;

        public static FontFamily ResolveComponentFontFamily(string ch, FontFamily preferredFamily)
        {
            if (preferredFamily == null) preferredFamily = ResolveFontFamily();
            if (string.IsNullOrEmpty(ch)) return preferredFamily;

            int cp = HanIdsCorpusIndex.GetFirstCodePoint(ch);
            if (cp >= 0x20000)
            {
                string famName = preferredFamily.Name ?? "";
                if (famName.IndexOf("ExtB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    famName.IndexOf("Nom", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return preferredFamily;
                }

                if (_cachedExtBFamily != null) return _cachedExtBFamily;
                string[] extBNames = new[] { "SimSun-ExtB", "MingLiU-ExtB", "PMingLiU-ExtB", "Nom Na Tong", "Microsoft YaHei" };
                foreach (var name in extBNames)
                {
                    try
                    {
                        using (var f = new Font(name, 12f))
                        {
                            if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                _cachedExtBFamily = f.FontFamily;
                                return _cachedExtBFamily;
                            }
                        }
                    }
                    catch { }
                }
            }
            return preferredFamily;
        }

        public static GraphicsPath CreateComponentVectorPath(
            string ch,
            FontFamily family,
            RectangleF targetRect,
            NodeCustomTransform transform)
        {
            var resultPath = new GraphicsPath();
            if (string.IsNullOrEmpty(ch)) return resultPath;

            FontFamily effectiveFamily = ResolveComponentFontFamily(ch, family);

            using (var rawPath = new GraphicsPath())
            {
                bool usedDonor = false;
                if (transform != null &&
                    transform.DonorExemplarCodePoint > 0 &&
                    transform.DonorContourIndices != null &&
                    transform.DonorContourIndices.Length > 0)
                {
                    usedDonor = OpticalComponentDonorService.TryBuildDonorVectorPath(
                        transform.DonorExemplarCodePoint,
                        transform.DonorContourIndices,
                        effectiveFamily,
                        rawPath);
                }

                if (!usedDonor || rawPath.PointCount == 0)
                {
                    rawPath.Reset();
                    using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;

                        // Add string at large em size for precision
                        rawPath.AddString(ch, effectiveFamily, (int)FontStyle.Regular, 500f, new PointF(250f, 250f), sf);
                    }
                }

                RectangleF bounds = rawPath.GetBounds();
                if (bounds.Width < 1f || bounds.Height < 1f)
                {
                    // Fallback to other CJK system fonts before giving up
                    string[] fallbacks = new[] { "Nom Na Tong", "SimSun-ExtB", "MingLiU-ExtB", "PMingLiU-ExtB", "Microsoft YaHei", "SimSun" };
                    foreach (var fb in fallbacks)
                    {
                        if (string.Equals(fb, effectiveFamily.Name, StringComparison.OrdinalIgnoreCase)) continue;
                        var fbFamily = ResolveFontFamily(fb);
                        if (fbFamily != null && !fbFamily.Equals(effectiveFamily))
                        {
                            rawPath.Reset();
                            using (var sfFb = (StringFormat)StringFormat.GenericTypographic.Clone())
                            {
                                sfFb.Alignment = StringAlignment.Center;
                                sfFb.LineAlignment = StringAlignment.Center;
                                rawPath.AddString(ch, fbFamily, (int)FontStyle.Regular, 500f, new PointF(250f, 250f), sfFb);
                            }
                            bounds = rawPath.GetBounds();
                            if (bounds.Width >= 1f && bounds.Height >= 1f)
                            {
                                effectiveFamily = fbFamily;
                                break;
                            }
                        }
                    }
                }

                if (bounds.Width < 1f || bounds.Height < 1f)
                {
                    // Unicode .notdef hollow tofu glyph (Fix Bug B1: Hollow contour instead of solid black box)
                    float strokeW = Math.Max(2f, Math.Min(targetRect.Width, targetRect.Height) * 0.05f);
                    if (targetRect.Width > strokeW * 2f && targetRect.Height > strokeW * 2f)
                    {
                        resultPath.FillMode = FillMode.Alternate;
                        resultPath.AddRectangle(targetRect);
                        RectangleF inner = new RectangleF(
                            targetRect.X + strokeW, targetRect.Y + strokeW,
                            targetRect.Width - strokeW * 2f, targetRect.Height - strokeW * 2f);
                        resultPath.AddRectangle(inner);
                    }
                    else
                    {
                        resultPath.AddRectangle(targetRect);
                    }
                    return resultPath;
                }

                // Map ink bounds into targetRect:
                using (var matrix = new Matrix())
                {
                    float rawScaleX = targetRect.Width / bounds.Width;
                    float rawScaleY = targetRect.Height / bounds.Height;
                    float scaleX = rawScaleX;
                    float scaleY = rawScaleY;

                    if (usedDonor)
                    {
                        // Đường viền trích xuất từ mẫu thư pháp (Donor Allomorph) đã có sẵn tỷ lệ nét chuẩn vị trí:
                        // giữ nguyên kích thước trục chính của slot (chiều cao với cột dọc, chiều rộng với tầng ngang)
                        // và chỉ chống phình quá mức trên trục phụ.
                        if (targetRect.Height >= targetRect.Width)
                        {
                            if (scaleX > scaleY * 1.18f) scaleX = scaleY * 1.18f;
                        }
                        else
                        {
                            if (scaleY > scaleX * 1.18f) scaleY = scaleX * 1.18f;
                        }
                    }
                    else if (transform == null || !transform.DisableAspectGuard)
                    {
                        float inkAspect = bounds.Width / Math.Max(1f, bounds.Height);
                        float anisotropy = rawScaleX / Math.Max(0.0001f, rawScaleY);

                        if (inkAspect > 3.2f)
                        {
                            // Nét ngang đơn (như 一) không bị kéo bè thành khối dày theo chiều dọc
                            scaleX = rawScaleX;
                            scaleY = Math.Min(rawScaleY, rawScaleX * 1.25f);
                        }
                        else if (inkAspect < 0.28f)
                        {
                            // Nét sổ đơn (như 丨) không bị phình to theo chiều ngang
                            scaleY = rawScaleY;
                            scaleX = Math.Min(rawScaleX, rawScaleY * 1.25f);
                        }
                        else
                        {
                            // Fix Bug B4: Typographical Anisotropy Guard
                            // Kẹp độ méo nét cực đoan trong dải an toàn [0.25 .. 4.0]
                            // Cho phép chữ Hán trong cột dọc (⿰, ⿲) lấp đầy chiều cao slot mà không bị co lùn thành người lùn
                            // và trong tầng ngang (⿱, ⿳) trải rộng hết chiều ngang slot mà không bị co thành cột tháp hẹp
                            const float minRatio = 0.25f;
                            const float maxRatio = 4.0f;

                            if (anisotropy > maxRatio)
                            {
                                scaleX = rawScaleY * maxRatio;
                            }
                            else if (anisotropy < minRatio)
                            {
                                scaleY = rawScaleX / minRatio;
                            }
                            else
                            {
                                scaleX = rawScaleX;
                                scaleY = rawScaleY;
                            }
                        }
                    }

                    float scaledW = bounds.Width * scaleX;
                    float scaledH = bounds.Height * scaleY;
                    float offX = targetRect.X + (targetRect.Width - scaledW) / 2f;
                    float offY = targetRect.Y + (targetRect.Height - scaledH) / 2f;

                    TypographicAnchor anchor = (transform != null && transform.Anchor != TypographicAnchor.Center)
                        ? transform.Anchor
                        : TypographicAnchor.Center;

                    if (anchor == TypographicAnchor.Top || anchor == TypographicAnchor.TopLeft || anchor == TypographicAnchor.TopRight)
                    {
                        offY = targetRect.Y + Math.Min(16f, (targetRect.Height - scaledH) * 0.15f);
                    }
                    else if (anchor == TypographicAnchor.Bottom || anchor == TypographicAnchor.BottomLeft || anchor == TypographicAnchor.BottomRight)
                    {
                        offY = targetRect.Bottom - scaledH - Math.Min(16f, (targetRect.Height - scaledH) * 0.15f);
                    }

                    if (anchor == TypographicAnchor.Left || anchor == TypographicAnchor.TopLeft || anchor == TypographicAnchor.BottomLeft)
                    {
                        offX = targetRect.X + Math.Min(16f, (targetRect.Width - scaledW) * 0.15f);
                    }
                    else if (anchor == TypographicAnchor.Right || anchor == TypographicAnchor.TopRight || anchor == TypographicAnchor.BottomRight)
                    {
                        offX = targetRect.Right - scaledW - Math.Min(16f, (targetRect.Width - scaledW) * 0.15f);
                    }

                    // Translate to origin, scale, then center into target
                    matrix.Translate(-bounds.X, -bounds.Y, MatrixOrder.Append);
                    matrix.Scale(scaleX, scaleY, MatrixOrder.Append);
                    matrix.Translate(offX, offY, MatrixOrder.Append);

                    // Horizontal Reflection (Fix Bug B11)
                    if (transform != null && (transform.IsMirroredX || transform.ScaleX < 0))
                    {
                        float cx = targetRect.X + targetRect.Width / 2f;
                        matrix.Translate(-cx, 0, MatrixOrder.Append);
                        matrix.Scale(-1, 1, MatrixOrder.Append);
                        matrix.Translate(cx, 0, MatrixOrder.Append);
                    }

                    // Rotation around center of targetRect (Fix Bug B11)
                    if (transform != null && transform.Rotation != 0)
                    {
                        float cx = targetRect.X + targetRect.Width / 2f;
                        float cy = targetRect.Y + targetRect.Height / 2f;
                        matrix.RotateAt(transform.Rotation, new PointF(cx, cy), MatrixOrder.Append);
                    }

                    rawPath.Transform(matrix);

                    // Optical Cavity Contour Pruning (Tier 1: Remove autonomous inner subpaths without jagged clipping)
                    if (transform != null && transform.HasOpticalCavity && transform.CavityRects != null && transform.CavityRects.Count > 0)
                    {
                        using (var iter = new GraphicsPathIterator(rawPath))
                        {
                            int count = iter.SubpathCount;
                            if (count > 1)
                            {
                                iter.Rewind();
                                var subpaths = new List<GraphicsPath>();
                                var areas = new List<double>();
                                var boundsList = new List<RectangleF>();

                                for (int i = 0; i < count; i++)
                                {
                                    bool isClosed;
                                    var sub = new GraphicsPath();
                                    iter.NextSubpath(sub, out isClosed);
                                    var pts = sub.PathPoints;
                                    double area = 0;
                                    for (int j = 0; j < pts.Length; j++)
                                    {
                                        var p1 = pts[j];
                                        var p2 = pts[(j + 1) % pts.Length];
                                        area += (p1.X * p2.Y - p2.X * p1.Y);
                                    }
                                    area /= 2.0;
                                    subpaths.Add(sub);
                                    areas.Add(area);
                                    boundsList.Add(sub.GetBounds());
                                }

                                var pruneSet = new HashSet<int>();
                                float maxPrunableArea = targetRect.Width * targetRect.Height * 0.45f;

                                for (int i = 0; i < count; i++)
                                {
                                    if (areas[i] > 0) // Solid outer contour
                                    {
                                        var b = boundsList[i];
                                        float subArea = b.Width * b.Height;
                                        if (subArea > 0 && subArea < maxPrunableArea)
                                        {
                                            foreach (var cav in transform.CavityRects)
                                            {
                                                var inter = RectangleF.Intersect(b, cav);
                                                float interArea = inter.Width > 0 && inter.Height > 0 ? inter.Width * inter.Height : 0f;
                                                if ((interArea / subArea) > 0.58f)
                                                {
                                                    pruneSet.Add(i);
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }

                                for (int i = 0; i < count; i++)
                                {
                                    if (areas[i] < 0)
                                    {
                                        foreach (int pIdx in pruneSet)
                                        {
                                            if (boundsList[pIdx].Contains(boundsList[i]))
                                            {
                                                pruneSet.Add(i);
                                                break;
                                            }
                                        }
                                    }
                                }

                                rawPath.Reset();
                                for (int i = 0; i < count; i++)
                                {
                                    if (!pruneSet.Contains(i))
                                    {
                                        rawPath.AddPath(subpaths[i], false);
                                    }
                                    subpaths[i].Dispose();
                                }
                            }
                        }
                    }

                    resultPath.AddPath(rawPath, false);
                }
            }

            return resultPath;
        }

        public static void RenderStage(
            Graphics g,
            Rectangle stageBounds,
            List<GlyphRenderData> renderItems,
            string selectedNodeId,
            IdsCanvasGridType gridType,
            bool showBBoxes,
            IdsColorTheme theme,
            float zoom = 1.0f,
            FontFamily activeFontFamily = null,
            float panX = 0f,
            float panY = 0f)
        {
            var themeCfg = IdsCanvasThemeConfig.GetTheme(theme);
            var family = activeFontFamily ?? ResolveFontFamily();

            // High quality rendering
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Compute 1000x1000 stage scaling into screen stageBounds with pan offset
            float minDim = Math.Min(stageBounds.Width, stageBounds.Height);
            float stageSize = minDim * zoom;
            float stageX = stageBounds.X + (stageBounds.Width - stageSize) / 2f + panX;
            float stageY = stageBounds.Y + (stageBounds.Height - stageSize) / 2f + panY;

            var origTransform = g.Transform;
            var stageMatrix = origTransform.Clone();
            float scale = stageSize / 1000f;
            stageMatrix.Scale(scale, scale, MatrixOrder.Prepend);
            stageMatrix.Translate(stageX, stageY, MatrixOrder.Append);
            g.Transform = stageMatrix;

            // 1. Stage Background
            using (var bgBrush = new SolidBrush(themeCfg.BgColor))
            {
                g.FillRectangle(bgBrush, 0, 0, 1000, 1000);
            }

            // 2. Paper border
            using (var borderPen = new Pen(themeCfg.PaperBorderColor, 4f))
            {
                g.DrawRectangle(borderPen, 2, 2, 996, 996);
            }

            // 3. Grids (Mễ Tự Cách or Cửu Cung Cách)
            if (gridType != IdsCanvasGridType.None)
            {
                int gridAlpha = (int)(themeCfg.GridOpacity * 255);
                using (var gridPen = new Pen(Color.FromArgb(gridAlpha, themeCfg.GridColor), 2f))
                {
                    gridPen.DashStyle = DashStyle.Dash;
                    gridPen.DashPattern = new float[] { 6, 6 };

                    // Master inner boundary (Unified with Em-box safe zone [90, 90, 820, 820])
                    g.DrawRectangle(gridPen, 90, 90, 820, 820);

                    if (gridType == IdsCanvasGridType.Mizige)
                    {
                        // Crosshairs (Thập tự)
                        g.DrawLine(gridPen, 500, 90, 500, 910);
                        g.DrawLine(gridPen, 90, 500, 910, 500);

                        // Diagonals (Bát quái chéo)
                        g.DrawLine(gridPen, 90, 90, 910, 910);
                        g.DrawLine(gridPen, 90, 910, 910, 90);
                    }
                    else if (gridType == IdsCanvasGridType.Jiugong)
                    {
                        // 3x3 Grid
                        float stepX = 820f / 3f;
                        float stepY = 820f / 3f;

                        g.DrawLine(gridPen, 90 + stepX, 90, 90 + stepX, 910);
                        g.DrawLine(gridPen, 90 + stepX * 2, 90, 90 + stepX * 2, 910);

                        g.DrawLine(gridPen, 90, 90 + stepY, 910, 90 + stepY);
                        g.DrawLine(gridPen, 90, 90 + stepY * 2, 910, 90 + stepY * 2);
                    }
                }
            }

            // 4. Render Components
            using (var inkBrush = new SolidBrush(themeCfg.InkColor))
            {
                var sortedItems = new List<GlyphRenderData>();
                if (renderItems != null)
                {
                    foreach (var it in renderItems)
                    {
                        if (it.Transform == null || it.Transform.IsVisible)
                        {
                            sortedItems.Add(it);
                        }
                    }
                    sortedItems.Sort((a, b) =>
                    {
                        int za = a.Transform != null ? a.Transform.ZIndex : 0;
                        int zb = b.Transform != null ? b.Transform.ZIndex : 0;
                        return za.CompareTo(zb);
                    });
                }

                foreach (var item in sortedItems)
                {
                    bool isSelected = (!string.IsNullOrEmpty(selectedNodeId) && selectedNodeId == item.NodeId);

                    // Generate path if not pre-generated
                    GraphicsPath path = item.VectorPath;
                    bool ownsPath = false;
                    if (path == null)
                    {
                        path = CreateComponentVectorPath(item.Char, family, item.TargetRect, item.Transform);
                        ownsPath = true;
                    }

                    // Apply Slice Mask Clipping
                    var state = g.Save();
                    ApplySliceClip(g, item.TargetRect, item.Transform.SliceMask);

                    // Clearance Halo (Optical buffer / lưu bạch xung quanh nét giao thoa)
                    if (item.HasClearanceHalo && item.ClearanceHaloWidth > 0.5f)
                    {
                        using (var haloPen = new Pen(themeCfg.BgColor, item.ClearanceHaloWidth))
                        {
                            haloPen.LineJoin = LineJoin.Round;
                            haloPen.StartCap = LineCap.Round;
                            haloPen.EndCap = LineCap.Round;
                            g.DrawPath(haloPen, path);
                        }
                    }

                    // Stroke Dilation compensation (Anisotropic)
                    if (item.StrokeDilationX > 0.6f || item.StrokeDilationY > 0.6f)
                    {
                        float rx = Math.Max(0.2f, item.StrokeDilationX * 0.65f);
                        float ry = Math.Max(0.2f, item.StrokeDilationY * 0.65f);
                        using (var pen = new Pen(themeCfg.InkColor, rx * 1.6f))
                        {
                            pen.LineJoin = LineJoin.Miter;
                            pen.MiterLimit = 2.2f;
                            if (rx > 0.001f)
                                pen.ScaleTransform(1.0f, ry / rx);
                            g.DrawPath(pen, path);
                        }
                    }

                    // Main glyph fill (or subtraction for ㇯)
                    if (item.Transform != null && item.Transform.IsSubtractive)
                    {
                        using (var eraseBrush = new SolidBrush(themeCfg.BgColor))
                        {
                            g.FillPath(eraseBrush, path);
                        }
                    }
                    else
                    {
                        g.FillPath(inkBrush, path);
                    }
                    g.Restore(state);

                    // Highlight for selected node
                    if (isSelected)
                    {
                        using (var selPen = new Pen(themeCfg.ActiveHighlightColor, 3f))
                        {
                            selPen.DashStyle = DashStyle.Solid;
                            g.DrawRectangle(selPen, item.TargetRect.X, item.TargetRect.Y, item.TargetRect.Width, item.TargetRect.Height);
                        }
                    }

                    // Bounding Box display
                    if (showBBoxes)
                    {
                        using (var bboxPen = new Pen(Color.FromArgb(180, 239, 68, 68), 1.5f))
                        {
                            bboxPen.DashStyle = DashStyle.Dot;
                            g.DrawRectangle(bboxPen, item.TargetRect.X, item.TargetRect.Y, item.TargetRect.Width, item.TargetRect.Height);

                            using (var font = new Font("Consolas", 14f, FontStyle.Bold))
                            using (var textBrush = new SolidBrush(Color.FromArgb(220, 239, 68, 68)))
                            {
                                g.DrawString(item.Char, font, textBrush, item.TargetRect.X + 2, item.TargetRect.Y + 2);
                            }
                        }
                    }

                    if (ownsPath && path != null)
                    {
                        path.Dispose();
                    }
                }
            }

            g.Transform = origTransform;
            stageMatrix.Dispose();
        }

        private static void ApplySliceClip(Graphics g, RectangleF rect, SliceMaskType mask)
        {
            if (mask == SliceMaskType.None) return;

            RectangleF clip = rect;
            switch (mask)
            {
                case SliceMaskType.LeftHalf:
                    clip.Width /= 2f;
                    break;
                case SliceMaskType.RightHalf:
                    clip.X += clip.Width / 2f;
                    clip.Width /= 2f;
                    break;
                case SliceMaskType.TopHalf:
                    clip.Height /= 2f;
                    break;
                case SliceMaskType.BottomHalf:
                    clip.Y += clip.Height / 2f;
                    clip.Height /= 2f;
                    break;
            }

            g.SetClip(clip, CombineMode.Intersect);
        }

        public static GraphicsPath BuildCombinedVectorPath(List<GlyphRenderData> renderItems, FontFamily family)
        {
            var combined = new GraphicsPath();
            if (renderItems == null || renderItems.Count == 0) return combined;

            for (int i = 0; i < renderItems.Count; i++)
            {
                var item = renderItems[i];
                if (item.Transform != null && !item.Transform.IsVisible) continue;

                GraphicsPath compPath = item.VectorPath;
                bool ownsPath = false;
                if (compPath == null)
                {
                    compPath = CreateComponentVectorPath(item.Char, family, item.TargetRect, item.Transform);
                    ownsPath = true;
                }

                if (compPath != null && compPath.PointCount > 0)
                {
                    combined.AddPath(compPath, false);
                }

                if (ownsPath && compPath != null)
                {
                    compPath.Dispose();
                }
            }

            return combined;
        }

        public static string ConvertGraphicsPathToSvg(GraphicsPath path)
        {
            if (path == null || path.PointCount == 0) return "";

            var sb = new StringBuilder();
            PointF[] pts = path.PathPoints;
            byte[] types = path.PathTypes;

            int i = 0;
            while (i < pts.Length)
            {
                byte type = (byte)(types[i] & 0x07);
                bool isClose = (types[i] & 0x80) != 0;

                if (type == 0) // Start (MoveTo)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "M{0:F1},{1:F1} ", pts[i].X, pts[i].Y);
                    i++;
                }
                else if (type == 1) // Line
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "L{0:F1},{1:F1} ", pts[i].X, pts[i].Y);
                    i++;
                }
                else if (type == 3) // Cubic Bezier (3 points)
                {
                    if (i + 2 < pts.Length)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "C{0:F1},{1:F1} {2:F1},{3:F1} {4:F1},{5:F1} ",
                            pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, pts[i + 2].X, pts[i + 2].Y);
                        isClose = (types[i + 2] & 0x80) != 0;
                        i += 3;
                    }
                    else
                    {
                        i++;
                    }
                }
                else
                {
                    i++;
                }

                if (isClose)
                {
                    sb.Append("Z ");
                }
            }

            return sb.ToString().Trim();
        }

        public static string GenerateFullSvg(
            List<GlyphRenderData> renderItems,
            FontFamily family,
            int width = 1000,
            int height = 1000,
            string inkColor = "#1e293b",
            string bgColor = "transparent")
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendFormat(CultureInfo.InvariantCulture, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1000 1000\" width=\"{0}\" height=\"{1}\">\n", width, height);

            if (!string.IsNullOrEmpty(bgColor) && !bgColor.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "  <rect width=\"1000\" height=\"1000\" fill=\"{0}\" />\n", bgColor);
            }

            sb.AppendLine("  <defs>");
            var visibleItems = new List<GlyphRenderData>();
            if (renderItems != null)
            {
                foreach (var it in renderItems)
                {
                    if (it.Transform == null || it.Transform.IsVisible)
                    {
                        visibleItems.Add(it);
                    }
                }
                visibleItems.Sort((a, b) =>
                {
                    int za = a.Transform != null ? a.Transform.ZIndex : 0;
                    int zb = b.Transform != null ? b.Transform.ZIndex : 0;
                    return za.CompareTo(zb);
                });
            }

            int idx = 0;
            foreach (var item in visibleItems)
            {
                if (item.StrokeDilationX > 0.5f || item.StrokeDilationY > 0.5f)
                {
                    float rx = Math.Max(0.5f, item.StrokeDilationX * 0.8f);
                    float ry = Math.Max(0.5f, item.StrokeDilationY * 0.8f);
                    sb.AppendFormat(CultureInfo.InvariantCulture, "    <filter id=\"filter_stroke_{0}\">\n", idx);
                    sb.AppendFormat(CultureInfo.InvariantCulture, "      <feMorphology operator=\"dilate\" radius=\"{0} {1}\" />\n", rx, ry);
                    sb.AppendLine("    </filter>");
                }
                idx++;
            }
            sb.AppendLine("  </defs>");

            idx = 0;
            foreach (var item in visibleItems)
            {
                GraphicsPath path = item.VectorPath;
                bool ownsPath = false;
                if (path == null)
                {
                    path = CreateComponentVectorPath(item.Char, family, item.TargetRect, item.Transform);
                    ownsPath = true;
                }
                if (path != null)
                {
                    string svgD = ConvertGraphicsPathToSvg(path);
                    string filterAttr = (item.StrokeDilationX > 0.5f || item.StrokeDilationY > 0.5f)
                        ? string.Format(" filter=\"url(#filter_stroke_{0})\"", idx)
                        : "";
                    if (item.Transform != null && item.Transform.IsSubtractive)
                    {
                        string fill = (!string.IsNullOrEmpty(bgColor) && !bgColor.Equals("transparent", StringComparison.OrdinalIgnoreCase)) ? bgColor : "#ffffff";
                        sb.AppendFormat(CultureInfo.InvariantCulture, "  <path d=\"{0}\" fill=\"{1}\"{2} />\n", svgD, fill, filterAttr);
                    }
                    else
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "  <path d=\"{0}\" fill=\"{1}\"{2} />\n", svgD, inkColor, filterAttr);
                    }
                    if (ownsPath)
                    {
                        path.Dispose();
                    }
                }
                idx++;
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static Bitmap RenderHighResBitmap(
            List<GlyphRenderData> renderItems,
            FontFamily family,
            int resolution,
            Color inkColor,
            Color bgColor,
            List<GraphicsPath> customStrokes = null)
        {
            var bmp = new Bitmap(resolution, resolution, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                if (bgColor != Color.Transparent)
                {
                    g.Clear(bgColor);
                }
                else
                {
                    g.Clear(Color.FromArgb(0, 0, 0, 0));
                }

                g.ScaleTransform(resolution / 1000f, resolution / 1000f);

                using (var inkBrush = new SolidBrush(inkColor))
                {
                    var visibleItems = new List<GlyphRenderData>();
                    if (renderItems != null)
                    {
                        foreach (var it in renderItems)
                        {
                            if (it.Transform == null || it.Transform.IsVisible)
                            {
                                visibleItems.Add(it);
                            }
                        }
                        visibleItems.Sort((a, b) =>
                        {
                            int za = a.Transform != null ? a.Transform.ZIndex : 0;
                            int zb = b.Transform != null ? b.Transform.ZIndex : 0;
                            return za.CompareTo(zb);
                        });
                    }

                    foreach (var item in visibleItems)
                    {
                        GraphicsPath path = item.VectorPath;
                        bool ownsPath = false;
                        if (path == null)
                        {
                            path = CreateComponentVectorPath(item.Char, family, item.TargetRect, item.Transform);
                            ownsPath = true;
                        }

                        if (path != null)
                        {
                            var state = g.Save();
                            if (item.Transform != null && item.Transform.SliceMask != 0)
                            {
                                ApplySliceClip(g, item.TargetRect, item.Transform.SliceMask);
                            }

                            if (item.HasClearanceHalo && item.ClearanceHaloWidth > 0.5f)
                            {
                                Color haloCol = (bgColor != Color.Transparent) ? bgColor : Color.White;
                                using (var haloPen = new Pen(haloCol, item.ClearanceHaloWidth))
                                {
                                    haloPen.LineJoin = LineJoin.Round;
                                    haloPen.StartCap = LineCap.Round;
                                    haloPen.EndCap = LineCap.Round;
                                    g.DrawPath(haloPen, path);
                                }
                            }

                            if (item.StrokeDilationX > 0.6f || item.StrokeDilationY > 0.6f)
                            {
                                float rx = Math.Max(0.2f, item.StrokeDilationX * 0.65f);
                                float ry = Math.Max(0.2f, item.StrokeDilationY * 0.65f);
                                using (var pen = new Pen(inkColor, rx * 1.6f))
                                {
                                    pen.LineJoin = LineJoin.Miter;
                                    pen.MiterLimit = 2.2f;
                                    if (rx > 0.001f)
                                        pen.ScaleTransform(1.0f, ry / rx);
                                    g.DrawPath(pen, path);
                                }
                            }

                            if (item.Transform != null && item.Transform.IsSubtractive)
                            {
                                using (var eraseBrush = new SolidBrush(bgColor != Color.Transparent ? bgColor : Color.White))
                                {
                                    g.FillPath(eraseBrush, path);
                                }
                            }
                            else
                            {
                                g.FillPath(inkBrush, path);
                            }
                            g.Restore(state);

                            if (ownsPath)
                            {
                                path.Dispose();
                            }
                        }
                    }

                    // Render custom drawn / severed vector strokes
                    if (customStrokes != null && customStrokes.Count > 0)
                    {
                        using (var strokePen = new Pen(inkColor, 3.5f))
                        {
                            strokePen.StartCap = LineCap.Round;
                            strokePen.EndCap = LineCap.Round;
                            strokePen.LineJoin = LineJoin.Round;
                            foreach (var p in customStrokes)
                            {
                                if (p == null || p.PointCount < 2) continue;
                                using (var iter = new GraphicsPathIterator(p))
                                {
                                    bool isClosed;
                                    using (var temp = new GraphicsPath())
                                    {
                                        iter.NextSubpath(temp, out isClosed);
                                        if (isClosed)
                                        {
                                            g.FillPath(inkBrush, p);
                                        }
                                    }
                                }
                                g.DrawPath(strokePen, p);
                            }
                        }
                    }
                }
            }

            return bmp;
        }

        public static Bitmap RenderCompositeBitmap(IdsNode root, FontFamily font, int resolution)
        {
            var items = IdsLayoutEngine.ComputeLayout(root, new RectangleF(100f, 100f, 800f, 800f));
            return RenderHighResBitmap(items, font, resolution, Color.Black, Color.White);
        }

        public static string HitTestNodeId(
            Point clickPt,
            Rectangle stageBounds,
            List<GlyphRenderData> renderItems,
            float zoom = 1.0f,
            float panX = 0f,
            float panY = 0f)
        {
            if (renderItems == null || renderItems.Count == 0) return null;

            float minDim = Math.Min(stageBounds.Width, stageBounds.Height);
            float stageSize = minDim * zoom;
            float stageX = stageBounds.X + (stageBounds.Width - stageSize) / 2f + panX;
            float stageY = stageBounds.Y + (stageBounds.Height - stageSize) / 2f + panY;

            // Map click point to 1000x1000 space
            float relX = (clickPt.X - stageX) * (1000f / stageSize);
            float relY = (clickPt.Y - stageY) * (1000f / stageSize);

            var sortedItems = new List<GlyphRenderData>();
            foreach (var it in renderItems)
            {
                if (it.Transform == null || it.Transform.IsVisible)
                {
                    sortedItems.Add(it);
                }
            }
            sortedItems.Sort((a, b) =>
            {
                int za = a.Transform != null ? a.Transform.ZIndex : 0;
                int zb = b.Transform != null ? b.Transform.ZIndex : 0;
                return za.CompareTo(zb);
            });

            // Iterate reverse to hit topmost items first
            for (int i = sortedItems.Count - 1; i >= 0; i--)
            {
                var item = sortedItems[i];
                PointF pt = new PointF(relX, relY);
                if (item.Transform != null && item.Transform.Rotation != 0)
                {
                    PointF center = new PointF(item.TargetRect.X + item.TargetRect.Width / 2f, item.TargetRect.Y + item.TargetRect.Height / 2f);
                    double rad = -item.Transform.Rotation * Math.PI / 180.0;
                    double cos = Math.Cos(rad);
                    double sin = Math.Sin(rad);
                    float dx = pt.X - center.X;
                    float dy = pt.Y - center.Y;
                    pt = new PointF((float)(center.X + dx * cos - dy * sin), (float)(center.Y + dx * sin + dy * cos));
                }

                if (item.TargetRect.Contains(pt.X, pt.Y))
                {
                    return item.NodeId;
                }
            }

            return null;
        }
    }
}
