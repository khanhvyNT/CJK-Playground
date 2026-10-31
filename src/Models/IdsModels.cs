using System;
using System.Collections.Generic;
using System.Drawing;

namespace CJKPlayground.Models
{
    public enum TypographicAnchor
    {
        Center,
        Top,
        Bottom,
        Left,
        Right,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public enum SliceMaskType
    {
        None,
        LeftHalf,
        RightHalf,
        TopHalf,
        BottomHalf
    }

    public enum EditorToolMode
    {
        Select,       // Free Transformer Tool (Q)
        DirectSelect, // Direct Selection Tool (A)
        Pen,          // Pen Tool (P)
        Hand,         // Hand / Free Pan Tool (V / H)
        Zoom,         // Zoom Tool (Z)
        Lasso,        // Lasso Selection Tool (L)
        LassoCut      // Lasso Cut / Knife Tool (C)
    }

    public enum BoundingHandle
    {
        None,
        Body,
        TopLeft,
        TopCenter,
        TopRight,
        MiddleRight,
        BottomRight,
        BottomCenter,
        BottomLeft,
        MiddleLeft,
        Rotate
    }

    public class SmartGuideLine
    {
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }
        public string Label { get; set; }
        public Color Color { get; set; }

        public SmartGuideLine(float x1, float y1, float x2, float y2, string label, Color? color = null)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Label = label;
            Color = color ?? Color.FromArgb(236, 72, 153);
        }
    }

    public enum IdsCanvasGridType
    {
        Mizige,   // Mễ Tự Cách (米字格)
        Jiugong,  // Cửu Cung Cách (九宫格)
        None
    }

    public enum IdsColorTheme
    {
        Classic,   // Giấy xuyến cổ, mực đen mun
        Seal,      // Nền ngọc, chu sa ấn triện đỏ
        Dark,      // Nền tối, mực hoàng kim
        Blueprint  // Nền xanh kỹ thuật
    }

    public class NodeCustomTransform
    {
        public float? SplitRatio { get; set; }           // 0.15 to 0.85 (default ~0.5)
        public float[] SplitRatios3 { get; set; }        // 3-way split: e.g. [0.33f, 0.34f, 0.33f]
        public int OffsetX { get; set; }                 // -200 to +200 px
        public int OffsetY { get; set; }                 // -200 to +200 px
        public float ScaleX { get; set; }                // 0.2 to 2.5
        public float ScaleY { get; set; }                // 0.2 to 2.5
        public int Rotation { get; set; }                // -180 to +180 degrees
        public float StrokeWeight { get; set; }          // -5 to +15 (dilation)
        public float StrokeDilationX { get; set; }       // Independent X dilation
        public float StrokeDilationY { get; set; }       // Independent Y dilation
        public float? ComponentAspect { get; set; }      // Measured ink aspect ratio (width/height)
        public bool DisableAspectGuard { get; set; }
        public SliceMaskType SliceMask { get; set; }
        public int DonorExemplarCodePoint { get; set; }  // Calligraphic donor glyph codepoint
        public int[] DonorContourIndices { get; set; }   // Verified contour indices in donor glyph
        public float DonorNaturalAspect { get; set; }    // Natural aspect ratio of extracted donor component
        public bool IsVisible { get; set; }              // Layer visibility toggle
        public bool IsLocked { get; set; }               // Layer lock toggle
        public int ZIndex { get; set; }                  // Rendering stack order
        public TypographicAnchor Anchor { get; set; }     // Typographic anchor point
        public bool IsMirroredX { get; set; }            // Horizontal reflection (Fix Bug B11)
        public bool IsSubtractive { get; set; }          // Stroke subtraction flag for ㇯ (Fix Bug B13)
        public bool HasOpticalCavity { get; set; }       // Exclude inner slot from outer enclosure ink
        public List<RectangleF> CavityRects { get; set; }// Bounding rects of excluded optical cavities
        public RectangleF? CavityRect                    // Single cavity accessor
        {
            get { return (CavityRects != null && CavityRects.Count > 0) ? CavityRects[0] : (RectangleF?)null; }
            set
            {
                if (value.HasValue)
                {
                    if (CavityRects == null) CavityRects = new List<RectangleF>();
                    if (!CavityRects.Contains(value.Value)) CavityRects.Add(value.Value);
                }
            }
        }
        public bool HasClearanceHalo { get; set; }       // Calligraphic white gap buffer (留白)
        public float ClearanceHaloWidth { get; set; }    // Clearance buffer pen width

        public NodeCustomTransform()
        {
            ScaleX = 1.0f;
            ScaleY = 1.0f;
            OffsetX = 0;
            OffsetY = 0;
            Rotation = 0;
            StrokeWeight = 0;
            StrokeDilationX = 0;
            StrokeDilationY = 0;
            DisableAspectGuard = false;
            SliceMask = SliceMaskType.None;
            DonorExemplarCodePoint = 0;
            DonorContourIndices = null;
            DonorNaturalAspect = 0f;
            IsVisible = true;
            IsLocked = false;
            ZIndex = 0;
            Anchor = TypographicAnchor.Center;
            IsMirroredX = false;
            IsSubtractive = false;
            HasOpticalCavity = false;
            CavityRects = null;
            HasClearanceHalo = false;
            ClearanceHaloWidth = 0f;
        }

        public NodeCustomTransform Clone()
        {
            return new NodeCustomTransform
            {
                SplitRatio = this.SplitRatio,
                SplitRatios3 = this.SplitRatios3 != null ? (float[])this.SplitRatios3.Clone() : null,
                OffsetX = this.OffsetX,
                OffsetY = this.OffsetY,
                ScaleX = this.ScaleX,
                ScaleY = this.ScaleY,
                Rotation = this.Rotation,
                StrokeWeight = this.StrokeWeight,
                StrokeDilationX = this.StrokeDilationX,
                StrokeDilationY = this.StrokeDilationY,
                ComponentAspect = this.ComponentAspect,
                DisableAspectGuard = this.DisableAspectGuard,
                SliceMask = this.SliceMask,
                DonorExemplarCodePoint = this.DonorExemplarCodePoint,
                DonorContourIndices = this.DonorContourIndices != null ? (int[])this.DonorContourIndices.Clone() : null,
                DonorNaturalAspect = this.DonorNaturalAspect,
                IsVisible = this.IsVisible,
                IsLocked = this.IsLocked,
                ZIndex = this.ZIndex,
                Anchor = this.Anchor,
                IsMirroredX = this.IsMirroredX,
                IsSubtractive = this.IsSubtractive,
                HasOpticalCavity = this.HasOpticalCavity,
                CavityRects = this.CavityRects != null ? new List<RectangleF>(this.CavityRects) : null,
                HasClearanceHalo = this.HasClearanceHalo,
                ClearanceHaloWidth = this.ClearanceHaloWidth
            };
        }
    }

    public class IdsNode
    {
        public string Id { get; set; }
        public string Type { get; set; } // "operator" or "leaf"
        public string Operator { get; set; } // "⿰", "⿱", etc.
        public string Char { get; set; } // Single character (BMP or Astral Plane 2/3)
        public List<IdsNode> Children { get; set; }
        public NodeCustomTransform Transform { get; set; }

        public bool HasTrailingTokens { get; set; }
        public bool HasPlaceholders { get; set; }
        public string EnclosureWarning { get; set; }
        public int ConsumedTokens { get; set; }
        public int TotalTokens { get; set; }
        public bool IsLiteralLocked { get; set; }
        public int RawCodePoint { get; set; }
        public int ResolvedCodePoint { get; set; }
        public ulong CanonicalHash { get; set; }
        public int EffectiveStrokes { get; set; }
        public byte EffectiveShapeClassId { get; set; }

        public bool IsOperator { get { return "operator".Equals(Type, StringComparison.OrdinalIgnoreCase); } }

        public IdsNode Clone()
        {
            var node = new IdsNode
            {
                Id = this.Id,
                Type = this.Type,
                Operator = this.Operator,
                Char = this.Char,
                Transform = this.Transform != null ? this.Transform.Clone() : new NodeCustomTransform(),
                HasTrailingTokens = this.HasTrailingTokens,
                HasPlaceholders = this.HasPlaceholders,
                EnclosureWarning = this.EnclosureWarning,
                ConsumedTokens = this.ConsumedTokens,
                TotalTokens = this.TotalTokens,
                IsLiteralLocked = this.IsLiteralLocked,
                RawCodePoint = this.RawCodePoint,
                ResolvedCodePoint = this.ResolvedCodePoint,
                CanonicalHash = this.CanonicalHash,
                EffectiveStrokes = this.EffectiveStrokes,
                EffectiveShapeClassId = this.EffectiveShapeClassId,
                Children = new List<IdsNode>()
            };
            if (this.Children != null)
            {
                foreach (var c in this.Children)
                {
                    if (c != null) node.Children.Add(c.Clone());
                }
            }
            return node;
        }

        public IdsNode()
        {
            Children = new List<IdsNode>();
            Transform = new NodeCustomTransform();
        }

        public override string ToString()
        {
            if (!IsOperator) return Char ?? "□";
            return (Operator ?? "□") + (Children != null ? string.Join("", Children.ConvertAll(c => c.ToString())) : "");
        }
    }

    public class GlyphRenderData : IDisposable
    {
        public string NodeId { get; set; }
        public string Char { get; set; }
        public RectangleF TargetRect { get; set; }
        public RectangleF StageBounds { get { return TargetRect; } set { TargetRect = value; } }
        public RectangleF InkBBox { get; set; }
        public NodeCustomTransform Transform { get; set; }
        public float ActualScaleX { get; set; }
        public float ActualScaleY { get; set; }
        public float ActualX { get; set; }
        public float ActualY { get; set; }
        public float StrokeDilationX { get; set; }
        public float StrokeDilationY { get; set; }
        public string SvgPathData { get; set; }
        public System.Drawing.Drawing2D.GraphicsPath VectorPath { get; set; }
        public TypographicAnchor Anchor { get; set; }
        public bool IsMirroredX { get; set; }
        public bool IsSubtractive { get; set; }
        public bool HasOpticalCavity { get; set; }
        public List<RectangleF> CavityRects { get; set; }
        public RectangleF? CavityRect
        {
            get { return (CavityRects != null && CavityRects.Count > 0) ? CavityRects[0] : (RectangleF?)null; }
            set
            {
                if (value.HasValue)
                {
                    if (CavityRects == null) CavityRects = new List<RectangleF>();
                    if (!CavityRects.Contains(value.Value)) CavityRects.Add(value.Value);
                }
            }
        }
        public bool HasClearanceHalo { get; set; }
        public float ClearanceHaloWidth { get; set; }

        public GlyphRenderData()
        {
            Transform = new NodeCustomTransform();
            Anchor = TypographicAnchor.Center;
            IsMirroredX = false;
            IsSubtractive = false;
            HasOpticalCavity = false;
            CavityRects = null;
            HasClearanceHalo = false;
            ClearanceHaloWidth = 0f;
        }

        public void Dispose()
        {
            if (VectorPath != null)
            {
                VectorPath.Dispose();
                VectorPath = null;
            }
        }
    }

    public class IdsPreset
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string NameEn { get; set; }
        public string Category { get; set; } // "vietnamese_nom", "cjk_classic", "complex_monuments"
        public string Ids { get; set; }
        public string ResultChar { get; set; }
        public string PuaCode { get; set; }
        public string Reading { get; set; }
        public string Meaning { get; set; }
        public string MeaningEn { get; set; }

        public IdsPreset() { }

        public IdsPreset(string id, string name, string category, string ids, string resultChar, string puaCode, string reading, string meaning, string nameEn = null, string meaningEn = null)
        {
            Id = id;
            Name = name;
            Category = category;
            Ids = ids;
            ResultChar = resultChar;
            PuaCode = puaCode;
            Reading = reading;
            Meaning = meaning;
            NameEn = nameEn;
            MeaningEn = meaningEn;
        }

        public string GetDisplayName(bool isVi)
        {
            if (!isVi && !string.IsNullOrEmpty(NameEn)) return NameEn;
            return Name;
        }

        public string GetDisplayMeaning(bool isVi)
        {
            if (!isVi && !string.IsNullOrEmpty(MeaningEn)) return MeaningEn;
            return Meaning;
        }
    }

    public class SavedCustomGlyph
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string IdsString { get; set; }
        public string PuaCode { get; set; }
        public string Reading { get; set; }
        public string Meaning { get; set; }
        public long CreatedAt { get; set; }
        public string AstJson { get; set; }

        public SavedCustomGlyph()
        {
            CreatedAt = DateTime.UtcNow.Ticks;
        }
    }

    public class IdsCanvasThemeConfig
    {
        public Color BgColor { get; set; }
        public Color PaperBorderColor { get; set; }
        public Color GridColor { get; set; }
        public float GridOpacity { get; set; }
        public Color InkColor { get; set; }
        public Color ActiveHighlightColor { get; set; }

        public static IdsCanvasThemeConfig GetTheme(IdsColorTheme theme)
        {
            switch (theme)
            {
                case IdsColorTheme.Seal:
                    return new IdsCanvasThemeConfig
                    {
                        BgColor = Color.FromArgb(255, 253, 245),
                        PaperBorderColor = Color.FromArgb(254, 205, 211),
                        GridColor = Color.FromArgb(225, 29, 72),
                        GridOpacity = 0.35f,
                        InkColor = Color.FromArgb(190, 18, 60), // Chu sa ấn triện
                        ActiveHighlightColor = Color.FromArgb(225, 29, 72)
                    };
                case IdsColorTheme.Dark:
                    return new IdsCanvasThemeConfig
                    {
                        BgColor = Color.FromArgb(9, 13, 22),
                        PaperBorderColor = Color.FromArgb(30, 41, 59),
                        GridColor = Color.FromArgb(245, 158, 11),
                        GridOpacity = 0.35f,
                        InkColor = Color.FromArgb(254, 243, 199), // Hoàng kim trên nền đêm
                        ActiveHighlightColor = Color.FromArgb(245, 158, 11)
                    };
                case IdsColorTheme.Blueprint:
                    return new IdsCanvasThemeConfig
                    {
                        BgColor = Color.FromArgb(4, 29, 51),
                        PaperBorderColor = Color.FromArgb(14, 116, 144),
                        GridColor = Color.FromArgb(56, 189, 248),
                        GridOpacity = 0.40f,
                        InkColor = Color.FromArgb(224, 242, 254), // Xanh phấn kỹ thuật
                        ActiveHighlightColor = Color.FromArgb(6, 182, 212)
                    };
                case IdsColorTheme.Classic:
                default:
                    return new IdsCanvasThemeConfig
                    {
                        BgColor = Color.FromArgb(252, 248, 238), // Giấy tuyên xuyến cổ
                        PaperBorderColor = Color.FromArgb(214, 200, 178),
                        GridColor = Color.FromArgb(217, 119, 6),
                        GridOpacity = 0.35f,
                        InkColor = Color.FromArgb(30, 41, 59), // Mực đen mun
                        ActiveHighlightColor = Color.FromArgb(217, 119, 6)
                    };
            }
        }
    }
}
