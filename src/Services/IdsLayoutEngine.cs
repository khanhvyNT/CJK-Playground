using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    /// <summary>
    /// STAGE 4 (Beautiful IDS v2.0):
    /// Bộ máy Bố cục Hình học Đệ quy có Ngân sách Cây con và Neo Vị trí Văn tự học
    /// (Subtree-Budgeted & Anchor-Driven Recursive Affine Layout Engine).
    /// - Phân bổ diện tích theo ngân sách cấu trúc cây con (Subtree Weight Budgeting).
    /// - Sàn kích thước khe tối thiểu (Minimum Slot Floor >= 48 units) chống suy biến.
    /// - Phân cấp Inset cho toán tử bao quanh lồng nhau và phân bổ so le chống trùng khít cho ⿻.
    /// - Hệ thống Neo điểm tựa văn tự học (TypographicAnchor: Top, Bottom, Left, Right, Center).
    /// </summary>
    public static class IdsLayoutEngine
    {
        private const float MinSlotDim = 48f;

        private static readonly ConcurrentDictionary<string, float> AspectCache =
            new ConcurrentDictionary<string, float>();

        public static float CalculateSubtreeWeight(IdsNode node)
        {
            if (node == null) return 1f;
            if (!node.IsOperator || node.Children == null || node.Children.Count == 0) return 1f;
            float sum = 0f;
            foreach (var child in node.Children)
            {
                sum += CalculateSubtreeWeight(child);
            }
            return sum + 0.35f;
        }

        public static List<GlyphRenderData> ComputeLayout(
            IdsNode rootNode,
            RectangleF? initialRect = null)
        {
            var result = new List<GlyphRenderData>();
            if (rootNode == null) return result;

            RectangleF startRect = initialRect ?? new RectangleF(90f, 90f, 820f, 820f);

            Action<IdsNode, RectangleF, TypographicAnchor, int> layoutRecursive = null;
            layoutRecursive = (node, targetRect, anchor, enclosureDepth) =>
            {
                if (node == null) return;

                var t = node.Transform ?? new NodeCustomTransform();
                float customScaleX = Math.Abs(t.ScaleX != 0 ? t.ScaleX : 1.0f);
                float customScaleY = Math.Abs(t.ScaleY != 0 ? t.ScaleY : 1.0f);
                float customOffX = t.OffsetX;
                float customOffY = t.OffsetY;

                float effectiveW = Math.Max(MinSlotDim, targetRect.Width * customScaleX);
                float effectiveH = Math.Max(MinSlotDim, targetRect.Height * customScaleY);

                // Typographic Anchoring:
                float effectiveX = targetRect.X + (targetRect.Width - effectiveW) / 2f + customOffX;
                float effectiveY = targetRect.Y + (targetRect.Height - effectiveH) / 2f + customOffY;

                if (anchor == TypographicAnchor.Top || anchor == TypographicAnchor.TopLeft || anchor == TypographicAnchor.TopRight)
                {
                    effectiveY = targetRect.Y + customOffY;
                }
                else if (anchor == TypographicAnchor.Bottom || anchor == TypographicAnchor.BottomLeft || anchor == TypographicAnchor.BottomRight)
                {
                    effectiveY = targetRect.Bottom - effectiveH + customOffY;
                }

                if (anchor == TypographicAnchor.Left || anchor == TypographicAnchor.TopLeft || anchor == TypographicAnchor.BottomLeft)
                {
                    effectiveX = targetRect.X + customOffX;
                }
                else if (anchor == TypographicAnchor.Right || anchor == TypographicAnchor.TopRight || anchor == TypographicAnchor.BottomRight)
                {
                    effectiveX = targetRect.Right - effectiveW + customOffX;
                }

                var effectiveRect = new RectangleF(effectiveX, effectiveY, effectiveW, effectiveH);

                // Khung an toàn:
                // Nếu chưa can thiệp offset thủ công (customOffX == 0 && customOffY == 0),
                // giới hạn trong em-box [-15 .. 1015] để chữ không tràn ra ngoài ô vuông mặc định.
                // Nếu người dùng đã dịch chuyển hoặc kéo thả (customOffX != 0 || customOffY != 0),
                // cho phép linh kiện di chuyển tự do trên không gian làm việc rộng [-2000 .. 3000].
                // Khung an toàn:
                // Auto-Beautify và căn lề em-box mặc định chỉ sử dụng offset vi chỉnh (<= 60px).
                // Giới hạn trong [-15 .. 1015] để đảm bảo các bất biến raster chuẩn typographic không bị vượt biên.
                // Khi người dùng chủ động kéo thả hoặc chỉnh offset lớn (> 60px), cho phép tự do di chuyển trên toàn canvas [-2000 .. 3000].
                if (Math.Abs(customOffX) <= 60f && Math.Abs(customOffY) <= 60f)
                {
                    if (effectiveRect.X < -15f) effectiveRect.X = -15f;
                    if (effectiveRect.Y < -15f) effectiveRect.Y = -15f;
                    if (effectiveRect.Right > 1015f) effectiveRect.X = Math.Max(-15f, 1015f - effectiveRect.Width);
                    if (effectiveRect.Bottom > 1015f) effectiveRect.Y = Math.Max(-15f, 1015f - effectiveRect.Height);
                }
                else
                {
                    if (effectiveRect.X < -2000f) effectiveRect.X = -2000f;
                    if (effectiveRect.Y < -2000f) effectiveRect.Y = -2000f;
                    if (effectiveRect.X > 3000f) effectiveRect.X = 3000f;
                    if (effectiveRect.Y > 3000f) effectiveRect.Y = 3000f;
                }

                if (!node.IsOperator)
                {
                    string ch = !string.IsNullOrEmpty(node.Char) ? node.Char : "□";

                    // Elastic Aspect-Ratio Guard:
                    // Khóa độ dày cho thanh ngang đơn (FlatBar: aspect > 3.2, như 一)
                    // và thanh dọc đơn (ThinPost: aspect < 0.28, như 丨).
                    if (!t.DisableAspectGuard)
                    {
                        float? aspect = t.ComponentAspect;
                        if (!aspect.HasValue && !string.IsNullOrEmpty(ch) && ch != "□")
                        {
                            aspect = MeasureAspect(ch);
                        }

                        if (aspect.HasValue)
                        {
                            float asp = aspect.Value;
                            if (asp > 3.2f)
                            {
                                float safeH = Math.Min(effectiveRect.Height, (effectiveRect.Width / asp) * 1.25f);
                                if (anchor == TypographicAnchor.Top || anchor == TypographicAnchor.TopLeft || anchor == TypographicAnchor.TopRight)
                                    effectiveRect.Y += 0;
                                else if (anchor == TypographicAnchor.Bottom || anchor == TypographicAnchor.BottomLeft || anchor == TypographicAnchor.BottomRight)
                                    effectiveRect.Y += (effectiveRect.Height - safeH);
                                else
                                    effectiveRect.Y += (effectiveRect.Height - safeH) / 2f;
                                effectiveRect.Height = Math.Max(24f, safeH);
                            }
                            else if (asp < 0.28f && asp > 0.01f)
                            {
                                float safeW = Math.Min(effectiveRect.Width, (effectiveRect.Height * asp) * 1.25f);
                                if (anchor == TypographicAnchor.Left || anchor == TypographicAnchor.TopLeft || anchor == TypographicAnchor.BottomLeft)
                                    effectiveRect.X += 0;
                                else if (anchor == TypographicAnchor.Right || anchor == TypographicAnchor.TopRight || anchor == TypographicAnchor.BottomRight)
                                    effectiveRect.X += (effectiveRect.Width - safeW);
                                else
                                    effectiveRect.X += (effectiveRect.Width - safeW) / 2f;
                                effectiveRect.Width = Math.Max(24f, safeW);
                            }
                        }
                    }

                    if (t.Anchor == TypographicAnchor.Center && anchor != TypographicAnchor.Center)
                    {
                        t.Anchor = anchor;
                    }
                    if (t.ComponentAspect == null && !string.IsNullOrEmpty(ch))
                    {
                        t.ComponentAspect = MeasureAspect(ch);
                    }

                    var item = new GlyphRenderData
                    {
                        NodeId = node.Id,
                        Char = ch,
                        TargetRect = effectiveRect,
                        InkBBox = effectiveRect,
                        Transform = t,
                        Anchor = anchor,
                        IsMirroredX = (t.IsMirroredX || t.ScaleX < 0),
                        IsSubtractive = t.IsSubtractive,
                        HasOpticalCavity = t.HasOpticalCavity,
                        CavityRects = t.CavityRects != null ? new List<RectangleF>(t.CavityRects) : null,
                        HasClearanceHalo = t.HasClearanceHalo,
                        ClearanceHaloWidth = t.ClearanceHaloWidth,
                        ActualScaleX = effectiveRect.Width / 820f,
                        ActualScaleY = effectiveRect.Height / 820f,
                        ActualX = effectiveRect.X,
                        ActualY = effectiveRect.Y,
                        StrokeDilationX = t.StrokeDilationX > 0 ? t.StrokeDilationX : Math.Max(0f, t.StrokeWeight),
                        StrokeDilationY = t.StrokeDilationY > 0 ? t.StrokeDilationY : Math.Max(0f, t.StrokeWeight)
                    };

                    result.Add(item);
                    return;
                }

                // Operator Node: phân chia không gian từ effectiveRect của toán tử
                string op = node.Operator;
                var children = node.Children ?? new List<IdsNode>();
                if (string.IsNullOrEmpty(op) || children.Count == 0) return;

                float x = effectiveRect.X;
                float y = effectiveRect.Y;
                float w = effectiveRect.Width;
                float h = effectiveRect.Height;

                switch (op)
                {
                    case "⿰":
                        {
                            float defaultRatio = 0.5f;
                            if (children.Count >= 2)
                            {
                                float w0 = CalculateSubtreeWeight(children[0]);
                                float w1 = CalculateSubtreeWeight(children[1]);
                                defaultRatio = Math.Max(0.24f, Math.Min(0.76f, w0 / (w0 + w1)));
                            }
                            float ratio = Math.Max(0.15f, Math.Min(0.85f, t.SplitRatio ?? defaultRatio));
                            float gap = Math.Min(22f, Math.Max(0f, w * 0.028f));
                            float availW = Math.Max(4f, w - gap);
                            float leftW = (float)Math.Round(availW * ratio);
                            float rightW = availW - leftW;

                            if (availW >= MinSlotDim * 2f)
                            {
                                if (leftW < MinSlotDim) { leftW = MinSlotDim; rightW = availW - leftW; }
                                else if (rightW < MinSlotDim) { rightW = MinSlotDim; leftW = availW - rightW; }
                            }

                            bool isMirrored = (t != null && t.IsMirroredX);
                            bool isRot180 = (t != null && Math.Abs(t.Rotation) == 180);
                            bool swapH = isMirrored ^ isRot180;

                            if (!swapH)
                            {
                                if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, leftW, h), TypographicAnchor.Right, enclosureDepth);
                                if (children.Count > 1) layoutRecursive(children[1], new RectangleF(x + leftW + gap, y, rightW, h), TypographicAnchor.Left, enclosureDepth);
                            }
                            else
                            {
                                if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x + rightW + gap, y, leftW, h), TypographicAnchor.Left, enclosureDepth);
                                if (children.Count > 1) layoutRecursive(children[1], new RectangleF(x, y, rightW, h), TypographicAnchor.Right, enclosureDepth);
                            }
                            break;
                        }

                    case "⿱":
                        {
                            float defaultRatio = 0.5f;
                            if (children.Count >= 2)
                            {
                                float w0 = CalculateSubtreeWeight(children[0]);
                                float w1 = CalculateSubtreeWeight(children[1]);
                                defaultRatio = Math.Max(0.24f, Math.Min(0.76f, w0 / (w0 + w1)));
                            }
                            float ratio = Math.Max(0.15f, Math.Min(0.85f, t.SplitRatio ?? defaultRatio));
                            float gap = Math.Min(22f, Math.Max(0f, h * 0.028f));
                            float availH = Math.Max(4f, h - gap);
                            float topH = (float)Math.Round(availH * ratio);
                            float botH = availH - topH;

                            if (availH >= MinSlotDim * 2f)
                            {
                                if (topH < MinSlotDim) { topH = MinSlotDim; botH = availH - topH; }
                                else if (botH < MinSlotDim) { botH = MinSlotDim; topH = availH - botH; }
                            }

                            bool isRot180 = (t != null && Math.Abs(t.Rotation) == 180);
                            if (!isRot180)
                            {
                                if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, topH), TypographicAnchor.Bottom, enclosureDepth);
                                if (children.Count > 1) layoutRecursive(children[1], new RectangleF(x, y + topH + gap, w, botH), TypographicAnchor.Top, enclosureDepth);
                            }
                            else
                            {
                                if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y + botH + gap, w, topH), TypographicAnchor.Top, enclosureDepth);
                                if (children.Count > 1) layoutRecursive(children[1], new RectangleF(x, y, w, botH), TypographicAnchor.Bottom, enclosureDepth);
                            }
                            break;
                        }

                    case "⿲":
                        {
                            float gap = Math.Min(16f, Math.Max(0f, w * 0.020f));
                            float availW = Math.Max(6f, w - gap * 2f);
                            float[] r = t.SplitRatios3;
                            if (r == null && children.Count >= 3)
                            {
                                float w0 = CalculateSubtreeWeight(children[0]);
                                float w1 = CalculateSubtreeWeight(children[1]);
                                float w2 = CalculateSubtreeWeight(children[2]);
                                float sumW = w0 + w1 + w2;
                                if (sumW > 0.001f)
                                {
                                    r = new float[] {
                                        Math.Max(0.18f, Math.Min(0.64f, w0 / sumW)),
                                        Math.Max(0.18f, Math.Min(0.64f, w1 / sumW)),
                                        Math.Max(0.18f, Math.Min(0.64f, w2 / sumW))
                                    };
                                }
                            }
                            if (r == null) r = new float[] { 0.33f, 0.34f, 0.33f };
                            float sum = r[0] + r[1] + r[2];
                            if (sum <= 0.001f) { r = new float[] { 0.33f, 0.34f, 0.33f }; sum = 1.0f; }
                            float w0f = (float)Math.Round(availW * (r[0] / sum));
                            float w1f = (float)Math.Round(availW * (r[1] / sum));
                            float w2f = Math.Max(0f, availW - w0f - w1f);

                            if (availW >= MinSlotDim * 3f)
                            {
                                if (w0f < MinSlotDim) w0f = MinSlotDim;
                                if (w1f < MinSlotDim) w1f = MinSlotDim;
                                if (w2f < MinSlotDim) w2f = MinSlotDim;
                                float curSum = w0f + w1f + w2f;
                                w0f = (float)Math.Round(availW * (w0f / curSum));
                                w1f = (float)Math.Round(availW * (w1f / curSum));
                                w2f = Math.Max(0f, availW - w0f - w1f);
                            }

                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w0f, h), TypographicAnchor.Right, enclosureDepth);
                            if (children.Count > 1) layoutRecursive(children[1], new RectangleF(x + w0f + gap, y, w1f, h), TypographicAnchor.Center, enclosureDepth);
                            if (children.Count > 2) layoutRecursive(children[2], new RectangleF(x + w0f + gap + w1f + gap, y, w2f, h), TypographicAnchor.Left, enclosureDepth);
                            break;
                        }

                    case "⿳":
                        {
                            float gap = Math.Min(10f, Math.Max(0f, h * 0.018f));
                            float availH = Math.Max(6f, h - gap * 2f);
                            float[] r = t.SplitRatios3;
                            if (r == null && children.Count >= 3)
                            {
                                float w0 = CalculateSubtreeWeight(children[0]);
                                float w1 = CalculateSubtreeWeight(children[1]);
                                float w2 = CalculateSubtreeWeight(children[2]);
                                float sumW = w0 + w1 + w2;
                                if (sumW > 0.001f)
                                {
                                    r = new float[] {
                                        Math.Max(0.18f, Math.Min(0.64f, w0 / sumW)),
                                        Math.Max(0.18f, Math.Min(0.64f, w1 / sumW)),
                                        Math.Max(0.18f, Math.Min(0.64f, w2 / sumW))
                                    };
                                }
                            }
                            if (r == null) r = new float[] { 0.33f, 0.34f, 0.33f };
                            float sum = r[0] + r[1] + r[2];
                            if (sum <= 0.001f) { r = new float[] { 0.33f, 0.34f, 0.33f }; sum = 1.0f; }
                            float h0f = (float)Math.Round(availH * (r[0] / sum));
                            float h1f = (float)Math.Round(availH * (r[1] / sum));
                            float h2f = Math.Max(0f, availH - h0f - h1f);

                            if (availH >= MinSlotDim * 3f)
                            {
                                if (h0f < MinSlotDim) h0f = MinSlotDim;
                                if (h1f < MinSlotDim) h1f = MinSlotDim;
                                if (h2f < MinSlotDim) h2f = MinSlotDim;
                                float curSum = h0f + h1f + h2f;
                                h0f = (float)Math.Round(availH * (h0f / curSum));
                                h1f = (float)Math.Round(availH * (h1f / curSum));
                                h2f = Math.Max(0f, availH - h0f - h1f);
                            }

                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h0f), TypographicAnchor.Bottom, enclosureDepth);
                            if (children.Count > 1) layoutRecursive(children[1], new RectangleF(x + y * 0f + 0f, y + h0f + gap, w, h1f), TypographicAnchor.Center, enclosureDepth);
                            if (children.Count > 2) layoutRecursive(children[2], new RectangleF(x, y + h0f + gap + h1f + gap, w, h2f), TypographicAnchor.Top, enclosureDepth);
                            break;
                        }

                    case "⿴":
                        {
                            bool isChild0Enc = children.Count > 0 && children[0].IsOperator && IsEnclosureOp(children[0].Operator);
                            float d = enclosureDepth * 0.04f;
                            RectangleF innerSlot = RectangleF.Empty;
                            if (children.Count > 1)
                            {
                                innerSlot = isChild0Enc
                                    ? new RectangleF(x + w * (0.28f + d), y + h * (0.28f + d), w * Math.Max(0.20f, 0.44f - d * 2f), h * Math.Max(0.20f, 0.44f - d * 2f))
                                    : new RectangleF(x + w * (0.18f + d), y + h * (0.18f + d), w * Math.Max(0.25f, 0.64f - d * 2f), h * Math.Max(0.25f, 0.64f - d * 2f));
                                SetClearanceHalo(children[1], Math.Max(10f, Math.Min(innerSlot.Width, innerSlot.Height) * 0.032f));
                                if (children.Count > 0) AddOpticalCavity(children[0], innerSlot);
                            }
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), TypographicAnchor.Center, enclosureDepth + 1);
                            if (children.Count > 1) layoutRecursive(children[1], innerSlot, TypographicAnchor.Center, enclosureDepth + 1);
                            break;
                        }

                    case "⿵":
                        {
                            bool isChild0Enc = children.Count > 0 && children[0].IsOperator && IsEnclosureOp(children[0].Operator);
                            float d = enclosureDepth * 0.04f;
                            RectangleF innerSlot = RectangleF.Empty;
                            if (children.Count > 1)
                            {
                                float startY = 0.26f;
                                float slotH = 0.68f;
                                if (children.Count > 0 && !children[0].IsOperator)
                                {
                                    var prof = MeasureEnclosureProfile(children[0].Char);
                                    if (prof.CanopyDepth > 0.20f)
                                    {
                                        startY = Math.Max(0.24f, Math.Min(0.50f, prof.CanopyDepth + 0.04f));
                                        slotH = Math.Max(0.35f, 0.94f - startY);
                                    }
                                    else if (prof.HasCenterBottomInk)
                                    {
                                        startY = 0.54f;
                                        slotH = 0.40f;
                                    }
                                }

                                innerSlot = isChild0Enc
                                    ? new RectangleF(x + w * (0.26f + d), y + h * (0.38f + d), w * Math.Max(0.20f, 0.48f - d * 2f), h * Math.Max(0.20f, 0.50f - d * 2f))
                                    : new RectangleF(x + w * (0.18f + d), y + h * (startY + d), w * Math.Max(0.25f, 0.64f - d * 2f), h * Math.Max(0.25f, slotH - d * 2f));
                                SetClearanceHalo(children[1], Math.Max(10f, Math.Min(innerSlot.Width, innerSlot.Height) * 0.032f));
                                if (children.Count > 0) AddOpticalCavity(children[0], innerSlot);
                            }
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), TypographicAnchor.Center, enclosureDepth + 1);
                            if (children.Count > 1) layoutRecursive(children[1], innerSlot, TypographicAnchor.Center, enclosureDepth + 1);
                            break;
                        }

                    case "⿶":
                        {
                            bool isChild0Enc = children.Count > 0 && children[0].IsOperator && IsEnclosureOp(children[0].Operator);
                            float d = enclosureDepth * 0.04f;
                            RectangleF innerSlot = RectangleF.Empty;
                            if (children.Count > 1)
                            {
                                float slotH = 0.68f;
                                if (children.Count > 0 && !children[0].IsOperator)
                                {
                                    var prof = MeasureEnclosureProfile(children[0].Char);
                                    if (prof.BottomShelfY > 0.40f && prof.BottomShelfY < 0.95f)
                                    {
                                        slotH = Math.Max(0.35f, prof.BottomShelfY - 0.08f);
                                    }
                                }

                                innerSlot = isChild0Enc
                                    ? new RectangleF(x + w * (0.26f + d), y + h * (0.18f + d), w * Math.Max(0.20f, 0.48f - d * 2f), h * Math.Max(0.20f, 0.50f - d * 2f))
                                    : new RectangleF(x + w * (0.18f + d), y + h * (0.06f + d), w * Math.Max(0.25f, 0.64f - d * 2f), h * Math.Max(0.25f, slotH - d * 2f));
                                SetClearanceHalo(children[1], Math.Max(10f, Math.Min(innerSlot.Width, innerSlot.Height) * 0.032f));
                                if (children.Count > 0) AddOpticalCavity(children[0], innerSlot);
                            }
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), TypographicAnchor.Center, enclosureDepth + 1);
                            if (children.Count > 1) layoutRecursive(children[1], innerSlot, TypographicAnchor.Center, enclosureDepth + 1);
                            break;
                        }

                    case "⿷":
                        {
                            bool isChild0Enc = children.Count > 0 && children[0].IsOperator && IsEnclosureOp(children[0].Operator);
                            float d = enclosureDepth * 0.04f;
                            RectangleF innerSlot = RectangleF.Empty;
                            if (children.Count > 1)
                            {
                                float leftExtent = 0.24f;
                                if (children.Count > 0 && !children[0].IsOperator)
                                {
                                    var prof = MeasureEnclosureProfile(children[0].Char);
                                    if (prof.LeftStemExtent > 0.26f) leftExtent = Math.Min(0.58f, prof.LeftStemExtent + 0.04f);
                                }

                                innerSlot = isChild0Enc
                                    ? new RectangleF(x + w * (0.34f + d), y + h * (0.24f + d), w * Math.Max(0.20f, 0.54f - d * 2f), h * Math.Max(0.20f, 0.52f - d * 2f))
                                    : new RectangleF(x + w * (leftExtent + d), y + h * (0.16f + d), w * Math.Max(0.25f, (0.94f - leftExtent) - d * 2f), h * Math.Max(0.25f, 0.68f - d * 2f));
                                SetClearanceHalo(children[1], Math.Max(10f, Math.Min(innerSlot.Width, innerSlot.Height) * 0.032f));
                                if (children.Count > 0) AddOpticalCavity(children[0], innerSlot);
                            }
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), TypographicAnchor.Center, enclosureDepth + 1);
                            if (children.Count > 1) layoutRecursive(children[1], innerSlot, TypographicAnchor.Center, enclosureDepth + 1);
                            break;
                        }

                    case "⿸":
                        {
                            bool isChild0Enc = children.Count > 0 && children[0].IsOperator && IsEnclosureOp(children[0].Operator);
                            float d = enclosureDepth * 0.04f;
                            RectangleF innerSlot = RectangleF.Empty;
                            if (children.Count > 1)
                            {
                                float startX = 0.26f;
                                float startY = 0.26f;
                                float slotW = 0.68f;
                                float slotH = 0.68f;
                                if (children.Count > 0 && !children[0].IsOperator)
                                {
                                    var prof = MeasureEnclosureProfile(children[0].Char);
                                    if (prof.LeftStemExtent > 0.28f)
                                    {
                                        startX = Math.Max(0.28f, Math.Min(0.52f, prof.LeftStemExtent + 0.04f));
                                        slotW = Math.Max(0.25f, 0.94f - startX);
                                    }
                                    if (prof.CanopyDepth > 0.26f)
                                    {
                                        startY = Math.Max(0.26f, Math.Min(0.50f, prof.CanopyDepth + 0.04f));
                                        slotH = Math.Max(0.25f, 0.94f - startY);
                                    }
                                    else if (prof.HasCenterBottomInk)
                                    {
                                        startX = Math.Max(0.36f, prof.LeftStemExtent);
                                        startY = 0.50f;
                                        slotW = 0.58f;
                                        slotH = 0.44f;
                                    }
                                }

                                innerSlot = isChild0Enc
                                    ? new RectangleF(x + w * (0.36f + d), y + h * (0.36f + d), w * Math.Max(0.20f, 0.54f - d * 2f), h * Math.Max(0.20f, 0.54f - d * 2f))
                                    : new RectangleF(x + w * (startX + d), y + h * (startY + d), w * Math.Max(0.25f, slotW - d * 2f), h * Math.Max(0.25f, slotH - d * 2f));
                                SetClearanceHalo(children[1], Math.Max(12f, Math.Min(innerSlot.Width, innerSlot.Height) * 0.035f));
                                if (children.Count > 0) AddOpticalCavity(children[0], innerSlot);
                            }
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), TypographicAnchor.Center, enclosureDepth + 1);
                            if (children.Count > 1) layoutRecursive(children[1], innerSlot, TypographicAnchor.Center, enclosureDepth + 1);
                            break;
                        }

                    case "⿹":
                        {
                            bool isChild0Enc = children.Count > 0 && children[0].IsOperator && IsEnclosureOp(children[0].Operator);
                            float d = enclosureDepth * 0.04f;
                            RectangleF innerSlot = RectangleF.Empty;
                            if (children.Count > 1)
                            {
                                float topCanopy = 0.26f;
                                if (children.Count > 0 && !children[0].IsOperator)
                                {
                                    var prof = MeasureEnclosureProfile(children[0].Char);
                                    if (prof.CanopyDepth > 0.26f) topCanopy = Math.Min(0.50f, prof.CanopyDepth + 0.02f);
                                }

                                innerSlot = isChild0Enc
                                    ? new RectangleF(x + w * (0.12f + d), y + h * (0.36f + d), w * Math.Max(0.20f, 0.54f - d * 2f), h * Math.Max(0.20f, 0.54f - d * 2f))
                                    : new RectangleF(x + w * (0.06f + d), y + h * (topCanopy + d), w * Math.Max(0.25f, 0.70f - d * 2f), h * Math.Max(0.25f, (0.94f - topCanopy) - d * 2f));
                                SetClearanceHalo(children[1], Math.Max(10f, Math.Min(innerSlot.Width, innerSlot.Height) * 0.032f));
                                if (children.Count > 0) AddOpticalCavity(children[0], innerSlot);
                            }
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), TypographicAnchor.Center, enclosureDepth + 1);
                            if (children.Count > 1) layoutRecursive(children[1], innerSlot, TypographicAnchor.Center, enclosureDepth + 1);
                            break;
                        }

                    case "⿺":
                        {
                            bool isChild0Enc = children.Count > 0 && children[0].IsOperator && IsEnclosureOp(children[0].Operator);
                            float d = enclosureDepth * 0.04f;
                            RectangleF innerSlot = RectangleF.Empty;
                            if (children.Count > 1)
                            {
                                float leftExtent = 0.28f;
                                float slotH = 0.68f;
                                if (children.Count > 0 && !children[0].IsOperator)
                                {
                                    var prof = MeasureEnclosureProfile(children[0].Char);
                                    if (prof.LeftStemExtent > 0.28f) leftExtent = Math.Min(0.58f, prof.LeftStemExtent + 0.04f);
                                    if (prof.BottomShelfY > 0.40f && prof.BottomShelfY < 0.95f)
                                    {
                                        slotH = Math.Max(0.35f, prof.BottomShelfY - 0.06f);
                                    }
                                    else if (prof.HasCenterBottomInk)
                                    {
                                        slotH = 0.56f;
                                    }
                                }

                                innerSlot = isChild0Enc
                                    ? new RectangleF(x + w * (0.36f + d), y + h * (0.12f + d), w * Math.Max(0.20f, 0.54f - d * 2f), h * Math.Max(0.20f, 0.54f - d * 2f))
                                    : new RectangleF(x + w * (leftExtent + d), y + h * (0.06f + d), w * Math.Max(0.25f, (0.94f - leftExtent) - d * 2f), h * Math.Max(0.25f, slotH - d * 2f));
                                SetClearanceHalo(children[1], Math.Max(12f, Math.Min(innerSlot.Width, innerSlot.Height) * 0.036f));
                                if (children.Count > 0) AddOpticalCavity(children[0], innerSlot);
                            }
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), TypographicAnchor.Center, enclosureDepth + 1);
                            if (children.Count > 1) layoutRecursive(children[1], innerSlot, TypographicAnchor.Center, enclosureDepth + 1);
                            break;
                        }

                    case "⿻":
                        {
                            // Fix Bug B5: Distinct concentric & interpenetrating slots with optical clearance buffer (留白)
                            var slot0 = new RectangleF(x + w * 0.04f, y + h * 0.04f, w * 0.92f, h * 0.92f);
                            var slot1 = new RectangleF(x + w * 0.16f, y + h * 0.16f, w * 0.68f, h * 0.68f);
                            if (children.Count > 0) layoutRecursive(children[0], slot0, TypographicAnchor.Center, enclosureDepth);
                            if (children.Count > 1)
                            {
                                SetClearanceHalo(children[1], Math.Max(12f, Math.Min(w, h) * 0.024f));
                                layoutRecursive(children[1], slot1, TypographicAnchor.Center, enclosureDepth);
                            }
                            break;
                        }

                    case "㇯":
                        {
                            // Fix Bug B13: IDC Subtraction (Distinct subtractor slot & subtraction flag)
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x + w * 0.04f, y + h * 0.04f, w * 0.92f, h * 0.92f), TypographicAnchor.Center, enclosureDepth);
                            if (children.Count > 1)
                            {
                                MarkSubtractive(children[1]);
                                layoutRecursive(children[1], new RectangleF(x + w * 0.20f, y + h * 0.20f, w * 0.60f, h * 0.60f), TypographicAnchor.Center, enclosureDepth);
                            }
                            break;
                        }

                    case "⿼":
                        {
                            // Fix Bug B12: Surround from Upper-Left and Lower-Right (Unicode 15.1 U+2FFC)
                            bool isChild0Enc = children.Count > 0 && children[0].IsOperator && IsEnclosureOp(children[0].Operator);
                            float d = enclosureDepth * 0.04f;
                            RectangleF innerSlot = RectangleF.Empty;
                            if (children.Count > 1)
                            {
                                innerSlot = isChild0Enc
                                    ? new RectangleF(x + w * (0.24f + d), y + h * (0.24f + d), w * Math.Max(0.20f, 0.52f - d * 2f), h * Math.Max(0.20f, 0.52f - d * 2f))
                                    : new RectangleF(x + w * (0.20f + d), y + h * (0.20f + d), w * Math.Max(0.25f, 0.60f - d * 2f), h * Math.Max(0.25f, 0.60f - d * 2f));
                                SetClearanceHalo(children[1], Math.Max(10f, Math.Min(innerSlot.Width, innerSlot.Height) * 0.032f));
                            }
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), TypographicAnchor.Center, enclosureDepth + 1);
                            if (children.Count > 1) layoutRecursive(children[1], innerSlot, TypographicAnchor.Center, enclosureDepth + 1);
                            break;
                        }

                    case "⿽":
                        {
                            // Fix Bug B12: Surround from Upper-Right and Lower-Left (Unicode 15.1 U+2FFD)
                            bool isChild0Enc = children.Count > 0 && children[0].IsOperator && IsEnclosureOp(children[0].Operator);
                            float d = enclosureDepth * 0.04f;
                            RectangleF innerSlot = RectangleF.Empty;
                            if (children.Count > 1)
                            {
                                innerSlot = isChild0Enc
                                    ? new RectangleF(x + w * (0.20f + d), y + h * (0.24f + d), w * Math.Max(0.20f, 0.52f - d * 2f), h * Math.Max(0.20f, 0.52f - d * 2f))
                                    : new RectangleF(x + w * (0.16f + d), y + h * (0.20f + d), w * Math.Max(0.25f, 0.60f - d * 2f), h * Math.Max(0.25f, 0.60f - d * 2f));
                                SetClearanceHalo(children[1], Math.Max(10f, Math.Min(innerSlot.Width, innerSlot.Height) * 0.032f));
                            }
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), TypographicAnchor.Center, enclosureDepth + 1);
                            if (children.Count > 1) layoutRecursive(children[1], innerSlot, TypographicAnchor.Center, enclosureDepth + 1);
                            break;
                        }

                    case "⿾":
                        {
                            // Fix Bug B11: Horizontal Reflection
                            if (children.Count > 0)
                            {
                                ApplyMirrorX(children[0]);
                                layoutRecursive(children[0], new RectangleF(x, y, w, h), anchor, enclosureDepth);
                            }
                            break;
                        }

                    case "⿿":
                        {
                            // Fix Bug B11: 180° Rotation
                            if (children.Count > 0)
                            {
                                ApplyRotation180(children[0]);
                                layoutRecursive(children[0], new RectangleF(x, y, w, h), anchor, enclosureDepth);
                            }
                            break;
                        }

                    case "〾":
                        {
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), anchor, enclosureDepth);
                            break;
                        }

                    default:
                        {
                            if (children.Count > 0) layoutRecursive(children[0], new RectangleF(x, y, w, h), anchor, enclosureDepth);
                            break;
                        }
                }
            };

            layoutRecursive(rootNode, startRect, TypographicAnchor.Center, 0);
            return result;
        }

        private static float MeasureAspect(string ch)
        {
            if (string.IsNullOrEmpty(ch) || ch == "□") return 1.0f;
            float cached;
            if (AspectCache.TryGetValue(ch, out cached)) return cached;

            try
            {
                var fam = IdsGlyphRenderer.ResolveFontFamily();
                using (var p = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        p.AddString(ch, fam, (int)FontStyle.Regular, 500f, new PointF(250f, 250f), sf);
                    }
                    if (p.PointCount > 0)
                    {
                        var bounds = p.GetBounds();
                        if (bounds.Width > 0 && bounds.Height > 0)
                        {
                            cached = bounds.Width / bounds.Height;
                            AspectCache[ch] = cached;
                            return cached;
                        }
                    }
                }
            }
            catch { }

            cached = 1.0f;
            AspectCache[ch] = cached;
            return cached;
        }

        public struct OpticalEnclosureProfile
        {
            public float LeftStemExtent;    // Extent of left body [0..1]
            public float CanopyDepth;       // Depth of top canopy [0..1]
            public float BottomShelfY;      // Where bottom horizontal support shelf begins [0..1]
            public float RightStemExtent;   // Extent of right body [0..1]
            public bool HasCenterBottomInk; // Has ink in center bottom
        }

        private static readonly ConcurrentDictionary<string, OpticalEnclosureProfile> ProfileCache =
            new ConcurrentDictionary<string, OpticalEnclosureProfile>();

        public static OpticalEnclosureProfile MeasureEnclosureProfile(string ch)
        {
            if (string.IsNullOrEmpty(ch) || ch == "□") return default(OpticalEnclosureProfile);
            OpticalEnclosureProfile cached;
            if (ProfileCache.TryGetValue(ch, out cached)) return cached;

            try
            {
                var fam = IdsGlyphRenderer.ResolveFontFamily();
                const int sz = 64;
                using (var bmp = new Bitmap(sz, sz, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var target = new RectangleF(0, 0, sz, sz);
                    using (var path = IdsGlyphRenderer.CreateComponentVectorPath(ch, fam, target, null))
                    {
                        g.FillPath(Brushes.Black, path);
                    }

                    var bd = bmp.LockBits(new Rectangle(0, 0, sz, sz), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    bool[,] ink = new bool[sz, sz];
                    unsafe
                    {
                        byte* scan0 = (byte*)bd.Scan0.ToPointer();
                        int stride = bd.Stride;
                        for (int y = 0; y < sz; y++)
                        {
                            byte* row = scan0 + y * stride;
                            for (int x = 0; x < sz; x++)
                            {
                                if (row[x * 4 + 3] > 64) ink[x, y] = true;
                            }
                        }
                    }
                    bmp.UnlockBits(bd);

                    int maxLeft = 0;
                    for (int y = (int)(sz * 0.15f); y <= (int)(sz * 0.75f); y++)
                    {
                        int rightmost = 0;
                        for (int x = 0; x < (int)(sz * 0.70f); x++)
                        {
                            if (ink[x, y]) rightmost = x;
                        }
                        if (rightmost > maxLeft) maxLeft = rightmost;
                    }

                    int maxCanopy = 0;
                    for (int x = (int)(sz * 0.15f); x <= (int)(sz * 0.85f); x++)
                    {
                        int bottommost = 0;
                        for (int y = 0; y < (int)(sz * 0.65f); y++)
                        {
                            if (ink[x, y]) bottommost = y;
                        }
                        if (bottommost > maxCanopy) maxCanopy = bottommost;
                    }

                    int shelfY = sz;
                    for (int y = (int)(sz * 0.45f); y < (int)(sz * 0.95f); y++)
                    {
                        int inkRow = 0;
                        for (int x = (int)(sz * 0.40f); x < (int)(sz * 0.92f); x++)
                        {
                            if (ink[x, y]) inkRow++;
                        }
                        if (inkRow >= (int)(sz * 0.12f) && shelfY == sz)
                        {
                            shelfY = y;
                        }
                    }

                    int maxRight = sz;
                    for (int y = (int)(sz * 0.15f); y <= (int)(sz * 0.75f); y++)
                    {
                        int leftmost = sz;
                        for (int x = sz - 1; x >= (int)(sz * 0.40f); x--)
                        {
                            if (ink[x, y]) leftmost = x;
                        }
                        if (leftmost < maxRight) maxRight = leftmost;
                    }

                    int inkBottomCount = 0;
                    for (int y = (int)(sz * 0.50f); y < (int)(sz * 0.95f); y++)
                    {
                        for (int x = (int)(sz * 0.30f); x < (int)(sz * 0.70f); x++)
                        {
                            if (ink[x, y]) inkBottomCount++;
                        }
                    }
                    float centerBottomDensity = (float)inkBottomCount / ((sz * 0.45f) * (sz * 0.40f));

                    cached = new OpticalEnclosureProfile
                    {
                        LeftStemExtent = (float)maxLeft / sz,
                        CanopyDepth = (float)maxCanopy / sz,
                        BottomShelfY = shelfY < sz ? ((float)shelfY / sz) : 0.95f,
                        RightStemExtent = (float)(sz - maxRight) / sz,
                        HasCenterBottomInk = centerBottomDensity > 0.08f
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MeasureEnclosureProfile EXCEPTION: " + ex.Message);
                cached = default(OpticalEnclosureProfile);
            }

            ProfileCache[ch] = cached;
            return cached;
        }

        private static bool IsEnclosureOp(string op)
        {
            return op == "⿴" || op == "⿵" || op == "⿶" || op == "⿷" || op == "⿸" || op == "⿹" || op == "⿺" || op == "⿼" || op == "⿽";
        }

        private static void MarkSubtractive(IdsNode node)
        {
            if (node == null) return;
            if (node.Transform == null) node.Transform = new NodeCustomTransform();
            node.Transform.IsSubtractive = true;
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    MarkSubtractive(child);
                }
            }
        }

        private static void ApplyMirrorX(IdsNode node)
        {
            if (node == null) return;
            if (node.Transform == null) node.Transform = new NodeCustomTransform();
            node.Transform.IsMirroredX = !node.Transform.IsMirroredX;
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    ApplyMirrorX(child);
                }
            }
        }

        private static void ApplyRotation180(IdsNode node)
        {
            if (node == null) return;
            if (node.Transform == null) node.Transform = new NodeCustomTransform();
            node.Transform.Rotation = (node.Transform.Rotation + 180) % 360;
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    ApplyRotation180(child);
                }
            }
        }

        private static void AddOpticalCavity(IdsNode node, RectangleF cavityRect)
        {
            if (node == null) return;
            if (node.Transform == null) node.Transform = new NodeCustomTransform();
            node.Transform.HasOpticalCavity = true;
            if (node.Transform.CavityRects == null) node.Transform.CavityRects = new List<RectangleF>();
            node.Transform.CavityRects.Add(cavityRect);
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    AddOpticalCavity(child, cavityRect);
                }
            }
        }

        private static void SetClearanceHalo(IdsNode node, float haloWidth)
        {
            if (node == null) return;
            if (node.Transform == null) node.Transform = new NodeCustomTransform();
            node.Transform.HasClearanceHalo = true;
            node.Transform.ClearanceHaloWidth = Math.Max(node.Transform.ClearanceHaloWidth, haloWidth);
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    SetClearanceHalo(child, haloWidth);
                }
            }
        }
    }
}
