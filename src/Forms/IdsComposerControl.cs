using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Forms
{
    public class IdsComposerControl : UserControl
    {
        // Core state
        private string _idsString = "⿱天上";
        private IdsPreset _currentPreset;
        private string _currentPresetName = "Chữ Trời (𡗶)";
        private IdsCanvasGridType _gridType = IdsCanvasGridType.Mizige;
        private IdsColorTheme _colorTheme = IdsColorTheme.Classic;
        private bool _showBBoxes = false;
        private bool _smartRadicalsEnabled = true;
        private bool _aspectGuardEnabled = true;
        private float _zoom = 1.0f;
        private float _panX = 0f;
        private float _panY = 0f;
        private FontFamily _activeFontFamily;
        private string _selectedNodeId = null;

        public readonly HashSet<string> _selectedNodeIds = new HashSet<string>();
        private Dictionary<string, NodeCustomTransform> _transformsMap = new Dictionary<string, NodeCustomTransform>();
        private Dictionary<string, NodeCustomTransform> _backupTransformsMap = new Dictionary<string, NodeCustomTransform>();
        private List<string> _lastBeautifyReport = new List<string>();
        private IdsNode _ast;
        private List<GlyphRenderData> _renderItems = new List<GlyphRenderData>();
        private readonly Dictionary<string, GraphicsPath> _componentCustomPaths = new Dictionary<string, GraphicsPath>();
        public readonly Dictionary<string, RectangleF> _componentCustomBaseRects = new Dictionary<string, RectangleF>();
        public readonly Dictionary<string, NodeCustomTransform> _componentCustomBaseTransforms = new Dictionary<string, NodeCustomTransform>();

        // Illustrator Studio Tools & Snapping
        private EditorToolMode _toolMode = EditorToolMode.Select;
        private bool _snapToGridEnabled = false;
        private bool _smartGuidesEnabled = true;
        private readonly List<SmartGuideLine> _activeSmartGuides = new List<SmartGuideLine>();
        private readonly List<GraphicsPath> _customStrokes = new List<GraphicsPath>();
        private readonly List<PointF> _activePenPoints = new List<PointF>();
        private Point _penCursorPt = Point.Empty;
        private int _selectedAnchorIndex = -1;
        private Tuple<string, NodeCustomTransform> _clipboardComponent;
        private ContextMenuStrip _canvasContextMenu;

        // Undo / Redo Manager
        private readonly UndoRedoManager _undoRedo = new UndoRedoManager(50);
        private Button _btnUndo;
        private Button _btnRedo;
        private Button _btnToolLasso;
        private Button _btnToolCut;
        private Button _btnAutoJoin;

        // Toolbar Buttons
        private Button _btnToolSelect;
        private Button _btnToolDirectSelect;
        private Button _btnToolPen;
        private Button _btnToolHand;
        private Button _btnToolZoom;
        private Button _btnSnapGrid;
        private Button _btnSmartGuides;
        private Label _lblToolStatus;

        // Layers Tab
        private TabPage _tabLayers;
        private ListView _lvLayers;

        // UI Controls - Studio Top
        private Panel _topBar;
        private FlowLayoutPanel _topFlow;
        private readonly ToolTip _mainToolTip = new ToolTip();
        private Label _lblPresetName;
        private Button _btnBeautify;
        private Button _btnUndoBeautify;
        private Button _btnOpenPresets;
        private Button _btnSaveDict;
        private Button _btnOpenDict;
        private Button _btnExport;
        private Button _btnInjectFont;
        private Button _btnTestFont;
        private Label _lblGrid;
        private ComboBox _cbGrid;
        private Label _lblTheme;
        private ComboBox _cbTheme;
        private CheckBox _chkBBoxes;
        private CheckBox _chkSmartRadicals;
        private CheckBox _chkAspectGuard;

        // UI Controls - Left
        private TabControl _tabLeft;
        private TabPage _tabFont;
        private TabPage _tabAst;
        private Label _lblFontPresets;
        private ComboBox _cbFontPreset;
        private Button _btnOpenCustomFont;
        private Label _lblFontStatus;
        private TreeView _treeAst;

        // UI Controls - Center
        private Label _lblFormulaPrompt;
        private TextBox _txtIdsInput;
        private Button _btnClearInput;
        private Button _btnCopyFormula;
        private Label _lblIdcPrompt;
        private Label _lblRadPrompt;
        private Panel _pnlCanvasContainer;
        private Button _btnZoomOut;
        private Button _btnZoomIn;
        private Button _btnResetZoom;
        private CanvasSurface _canvasSurface;
        private Panel _pnlLegibility;
        private Label _lblZoomVal;

        // UI Controls - Splitters
        private SplitContainer _mainSplit;
        private SplitContainer _rightSplit;

        // UI Controls - Right (Node Inspector)
        private Panel _pnlInspector;
        private SmoothTrackBar _trackSplitRatio;
        private Label _lblSplitRatio;
        private SmoothTrackBar _trackOffsetX;
        private Label _lblOffsetX;
        private SmoothTrackBar _trackOffsetY;
        private Label _lblOffsetY;

        public void ZoomIn()
        {
            SetZoom(_zoom + 0.15f);
        }

        public void ZoomOut()
        {
            SetZoom(_zoom - 0.15f);
        }

        public void ZoomReset()
        {
            _panX = 0f;
            _panY = 0f;
            SetZoom(1.0f);
            if (_canvasSurface != null)
            {
                _canvasSurface.Invalidate();
            }
        }

        public void SetZoom(float newZoom)
        {
            float clamped = Math.Max(0.4f, Math.Min(3.0f, (float)Math.Round(newZoom, 2)));
            if (Math.Abs(_zoom - clamped) > 0.01f)
            {
                _zoom = clamped;
                if (_lblZoomVal != null)
                {
                    _lblZoomVal.Text = string.Format("{0}%", (int)Math.Round(_zoom * 100));
                }
                if (_canvasSurface != null)
                {
                    _canvasSurface.Invalidate();
                }
            }
        }

        public void RemoveComponentCustomPath(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            if (_componentCustomPaths.ContainsKey(nodeId))
            {
                try { _componentCustomPaths[nodeId].Dispose(); } catch { }
                _componentCustomPaths.Remove(nodeId);
            }
            if (_componentCustomBaseRects.ContainsKey(nodeId))
            {
                _componentCustomBaseRects.Remove(nodeId);
            }
            if (_componentCustomBaseTransforms.ContainsKey(nodeId))
            {
                _componentCustomBaseTransforms.Remove(nodeId);
            }
        }

        public void SetComponentCustomPath(string nodeId, GraphicsPath path, RectangleF baseRect, NodeCustomTransform baseTransform)
        {
            if (string.IsNullOrEmpty(nodeId) || path == null) return;
            if (_componentCustomPaths.ContainsKey(nodeId))
            {
                try { _componentCustomPaths[nodeId].Dispose(); } catch { }
            }
            _componentCustomPaths[nodeId] = (GraphicsPath)path.Clone();
            _componentCustomBaseRects[nodeId] = baseRect;
            _componentCustomBaseTransforms[nodeId] = baseTransform != null ? baseTransform.Clone() : new NodeCustomTransform();
        }

        public void ClearAllComponentCustomPaths()
        {
            foreach (var p in _componentCustomPaths.Values)
            {
                try { if (p != null) p.Dispose(); } catch { }
            }
            _componentCustomPaths.Clear();
            _componentCustomBaseRects.Clear();
            _componentCustomBaseTransforms.Clear();
        }

        public GraphicsPath CreateFittedCustomPath(string nodeId, RectangleF targetRect, NodeCustomTransform targetTransform)
        {
            if (string.IsNullOrEmpty(nodeId) || !_componentCustomPaths.ContainsKey(nodeId)) return null;
            var basePath = _componentCustomPaths[nodeId];
            if (basePath == null) return null;

            RectangleF baseRect;
            if (!_componentCustomBaseRects.TryGetValue(nodeId, out baseRect) || baseRect.Width <= 0 || baseRect.Height <= 0)
            {
                baseRect = targetRect;
                _componentCustomBaseRects[nodeId] = baseRect;
            }

            NodeCustomTransform baseTrans;
            if (!_componentCustomBaseTransforms.TryGetValue(nodeId, out baseTrans) || baseTrans == null)
            {
                baseTrans = targetTransform != null ? targetTransform.Clone() : new NodeCustomTransform();
                _componentCustomBaseTransforms[nodeId] = baseTrans;
            }

            var fitted = (GraphicsPath)basePath.Clone();
            if (baseRect.Width > 0.001f && baseRect.Height > 0.001f &&
                (Math.Abs(targetRect.X - baseRect.X) > 0.001f ||
                 Math.Abs(targetRect.Y - baseRect.Y) > 0.001f ||
                 Math.Abs(targetRect.Width - baseRect.Width) > 0.001f ||
                 Math.Abs(targetRect.Height - baseRect.Height) > 0.001f))
            {
                using (var m = new Matrix())
                {
                    m.Translate(targetRect.X, targetRect.Y);
                    m.Scale(targetRect.Width / baseRect.Width, targetRect.Height / baseRect.Height);
                    m.Translate(-baseRect.X, -baseRect.Y);
                    fitted.Transform(m);
                }
            }

            if (targetTransform != null && baseTrans != null)
            {
                float deltaRot = targetTransform.Rotation - baseTrans.Rotation;
                if (Math.Abs(deltaRot) > 0.01f)
                {
                    PointF center = new PointF(targetRect.X + targetRect.Width / 2f, targetRect.Y + targetRect.Height / 2f);
                    using (var mRot = new Matrix())
                    {
                        mRot.RotateAt(deltaRot, center);
                        fitted.Transform(mRot);
                    }
                }
            }
            return fitted;
        }

        public void PreserveComponentCustomPaths()
        {
            if (_renderItems != null && _componentCustomPaths != null && _componentCustomPaths.Count > 0)
            {
                foreach (var item in _renderItems)
                {
                    if (item != null && !string.IsNullOrEmpty(item.NodeId) && _componentCustomPaths.ContainsKey(item.NodeId))
                    {
                        if (item.VectorPath != null)
                        {
                            try { item.VectorPath.Dispose(); } catch { }
                        }
                        item.VectorPath = CreateFittedCustomPath(item.NodeId, item.TargetRect, item.Transform);
                    }
                }
            }
        }

        public void PreserveAndTranslateComponentCustomPaths(string movingNodeId, GraphicsPath startMovingPath, float moveDx, float moveDy)
        {
            PreserveComponentCustomPaths();
        }

        private void ApplyTransformAndRedraw()
        {
            _renderItems = IdsLayoutEngine.ComputeLayout(_ast);
            PreserveComponentCustomPaths();
            if (_canvasSurface != null)
            {
                _canvasSurface.Invalidate();
            }
            UpdateLegibilityStrip();
            RebuildLayersList();
        }

        public void SetToolMode(EditorToolMode mode)
        {
            _toolMode = mode;
            if (_canvasSurface != null)
            {
                _canvasSurface.ResetInteractionStates();
                if (mode == EditorToolMode.Lasso || mode == EditorToolMode.LassoCut)
                {
                    _canvasSurface.ClearCustomStrokeSelection();
                }
                else if (mode == EditorToolMode.Select)
                {
                    if (_canvasSurface.SelectedCustomStrokeIndices.Count > 0)
                    {
                        _selectedNodeId = null;
                        if (_treeAst != null) _treeAst.SelectedNode = null;
                        if (_lvLayers != null) _lvLayers.SelectedIndices.Clear();
                    }
                }
            }
            if (_toolMode != EditorToolMode.Pen && _activePenPoints.Count > 0)
            {
                _activePenPoints.Clear();
            }
            if (_toolMode != EditorToolMode.DirectSelect)
            {
                _selectedAnchorIndex = -1;
            }
            UpdateToolButtons();
            if (_canvasSurface != null)
            {
                _canvasSurface.Cursor = (_toolMode == EditorToolMode.Hand) ? Cursors.Hand : Cursors.Default;
                _canvasSurface.Invalidate();
                try { _canvasSurface.Focus(); } catch { }
            }
        }

        public void UpdateToolButtons()
        {
            Button[] toolBtns = { _btnToolSelect, _btnToolDirectSelect, _btnToolPen, _btnToolHand, _btnToolZoom, _btnToolLasso, _btnToolCut };
            EditorToolMode[] modes = { EditorToolMode.Select, EditorToolMode.DirectSelect, EditorToolMode.Pen, EditorToolMode.Hand, EditorToolMode.Zoom, EditorToolMode.Lasso, EditorToolMode.LassoCut };

            for (int i = 0; i < toolBtns.Length; i++)
            {
                if (toolBtns[i] == null) continue;
                bool isActive = (_toolMode == modes[i]);
                if (isActive)
                {
                    toolBtns[i].BackColor = Color.FromArgb(37, 99, 235); // Royal Blue
                    toolBtns[i].ForeColor = Color.White;
                    toolBtns[i].Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                    toolBtns[i].FlatAppearance.BorderColor = Color.FromArgb(29, 78, 216);
                }
                else
                {
                    toolBtns[i].BackColor = Color.FromArgb(241, 245, 249);
                    toolBtns[i].ForeColor = Color.FromArgb(30, 41, 59);
                    toolBtns[i].Font = new Font("Segoe UI", 9F, FontStyle.Regular);
                    toolBtns[i].FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                }
            }

            if (_btnSnapGrid != null)
            {
                _btnSnapGrid.BackColor = _snapToGridEnabled ? Color.FromArgb(219, 234, 254) : Color.FromArgb(241, 245, 249);
                _btnSnapGrid.ForeColor = _snapToGridEnabled ? Color.FromArgb(29, 78, 216) : Color.FromArgb(30, 41, 59);
                _btnSnapGrid.Font = new Font("Segoe UI", 9F, _snapToGridEnabled ? FontStyle.Bold : FontStyle.Regular);
            }
            if (_btnSmartGuides != null)
            {
                _btnSmartGuides.BackColor = _smartGuidesEnabled ? Color.FromArgb(252, 231, 243) : Color.FromArgb(241, 245, 249);
                _btnSmartGuides.ForeColor = _smartGuidesEnabled ? Color.FromArgb(190, 24, 93) : Color.FromArgb(30, 41, 59);
                _btnSmartGuides.Font = new Font("Segoe UI", 9F, _smartGuidesEnabled ? FontStyle.Bold : FontStyle.Regular);
            }

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            if (_lblToolStatus != null)
            {
                switch (_toolMode)
                {
                    case EditorToolMode.Select:
                        _lblToolStatus.Text = isVi ? "↖ Free Transformer (Q): Bounding Box kéo dãn 8 điểm, xoay & di chuyển trực tiếp" : "↖ Free Transformer (Q): Bounding Box scale, rotate & direct drag";
                        break;
                    case EditorToolMode.DirectSelect:
                        _lblToolStatus.Text = isVi ? "⇱ Direct Selection (A): Kéo từng điểm neo (Anchor Points) của vector nét" : "⇱ Direct Selection (A): Drag individual vector anchor points";
                        break;
                    case EditorToolMode.Pen:
                        _lblToolStatus.Text = isVi ? "✒ Pen Tool (P): Click vẽ nét vector, Double-click hoặc Enter kết thúc" : "✒ Pen Tool (P): Click to draw vector, Double-click or Enter to finish";
                        break;
                    case EditorToolMode.Hand:
                        _lblToolStatus.Text = isVi ? "✋ Bàn tay vuốt tự do (V / H): Kéo vuốt di chuyển toàn bộ Canvas" : "✋ Hand / Free Pan Tool (V / H): Drag left mouse to pan canvas";
                        break;
                    case EditorToolMode.Zoom:
                        _lblToolStatus.Text = isVi ? "🔍 Zoom Tool (Z): Click phóng to (+), Alt+Click thu nhỏ (-)" : "🔍 Zoom Tool (Z): Click zoom in (+), Alt+Click zoom out (-)";
                        break;
                    case EditorToolMode.Lasso:
                        _lblToolStatus.Text = isVi ? "🪢 Lasso Tool (L): Kéo chuột khoanh vòng tự do để chọn linh kiện & nét" : "🪢 Lasso Tool (L): Drag freehand loop to select components & strokes";
                        break;
                    case EditorToolMode.LassoCut:
                        _lblToolStatus.Text = isVi ? "✂ Lasso Cut / Knife (C): Kéo đường cắt xuyên qua nét để chia tách thành các đoạn độc lập" : "✂ Knife Tool (C): Drag cutting line across strokes to slice them into separate segments";
                        break;
                }
            }

            if (_canvasSurface != null)
            {
                _canvasSurface.Invalidate();
            }
        }

        public void RebuildLayersList()
        {
            if (_lvLayers == null) return;
            _lvLayers.BeginUpdate();
            _lvLayers.Items.Clear();

            if (_renderItems != null)
            {
                for (int i = 0; i < _renderItems.Count; i++)
                {
                    var item = _renderItems[i];
                    bool isVis = item.Transform == null || item.Transform.IsVisible;
                    bool isLock = item.Transform != null && item.Transform.IsLocked;
                    int z = item.Transform != null ? item.Transform.ZIndex : 0;

                    string status = (isVis ? "👁️" : "🚫") + " " + (isLock ? "🔒" : "🔓");
                    var lvi = new ListViewItem(status);
                    lvi.SubItems.Add(item.Char);
                    lvi.SubItems.Add("Slot " + i + " (" + item.NodeId + ")");
                    lvi.SubItems.Add(z.ToString());
                    lvi.Tag = item.NodeId;
                    if (_selectedNodeIds.Contains(item.NodeId) || item.NodeId == _selectedNodeId)
                    {
                        lvi.Selected = true;
                    }
                    _lvLayers.Items.Add(lvi);
                }
            }
            _lvLayers.EndUpdate();
        }

        private void SetupCanvasContextMenu()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            _canvasContextMenu = new ContextMenuStrip();
            _canvasContextMenu.Font = new Font("Segoe UI", 9F);

            _canvasContextMenu.Items.Add(isVi ? "✂️ Cắt (Cut)" : "✂️ Cut", null, (s, e) => ContextCut());
            _canvasContextMenu.Items.Add(isVi ? "📋 Sao chép (Copy)" : "📋 Copy", null, (s, e) => ContextCopy());
            _canvasContextMenu.Items.Add(isVi ? "📄 Dán (Paste)" : "📄 Paste", null, (s, e) => ContextPaste());
            _canvasContextMenu.Items.Add(isVi ? "📑 Nhân bản (Duplicate)" : "📑 Duplicate", null, (s, e) => ContextDuplicate());
            _canvasContextMenu.Items.Add(isVi ? "🗑️ Xóa bộ kiện (Delete)" : "🗑️ Delete", null, (s, e) => ContextDelete());

            _canvasContextMenu.Items.Add(new ToolStripSeparator());

            var mArrange = (ToolStripMenuItem)_canvasContextMenu.Items.Add(isVi ? "Thứ tự lớp (Arrange)" : "Arrange");
            mArrange.DropDownItems.Add(isVi ? "⬆️ Lên trên cùng (Bring to Front)" : "⬆️ Bring to Front", null, (s, e) => ContextBringToFront());
            mArrange.DropDownItems.Add(isVi ? "🔼 Lên một lớp (Bring Forward)" : "🔼 Bring Forward", null, (s, e) => ContextBringForward());
            mArrange.DropDownItems.Add(isVi ? "🔽 Xuống một lớp (Send Backward)" : "🔽 Send Backward", null, (s, e) => ContextSendBackward());
            mArrange.DropDownItems.Add(isVi ? "⬇️ Xuống dưới cùng (Send to Back)" : "⬇️ Send to Back", null, (s, e) => ContextSendToBack());

            _canvasContextMenu.Items.Add(new ToolStripSeparator());

            var mTransform = (ToolStripMenuItem)_canvasContextMenu.Items.Add(isVi ? "Biến đổi (Transform)" : "Transform");
            mTransform.DropDownItems.Add(isVi ? "🎯 Căn giữa ô vuông (Align Center)" : "🎯 Align Center", null, (s, e) => ContextAlignCenter());
            mTransform.DropDownItems.Add(isVi ? "↔️ Lật ngang (Flip Horizontal)" : "↔️ Flip Horizontal", null, (s, e) => ContextFlipH());
            mTransform.DropDownItems.Add(isVi ? "↕️ Lật dọc (Flip Vertical)" : "↕️ Flip Vertical", null, (s, e) => ContextFlipV());
            mTransform.DropDownItems.Add(isVi ? "🔄 Xoay 90° CW" : "🔄 Rotate 90° CW", null, (s, e) => ContextRotate(90));
            mTransform.DropDownItems.Add(isVi ? "↺ Xoay 90° CCW" : "↺ Rotate 90° CCW", null, (s, e) => ContextRotate(-90));
            mTransform.DropDownItems.Add(isVi ? "↺ Đặt lại biến đổi (Reset Transform)" : "↺ Reset Transform", null, (s, e) => ContextResetTransform());

            _canvasContextMenu.Items.Add(new ToolStripSeparator());

            _canvasContextMenu.Items.Add(isVi ? "🔒 Khóa / Mở khóa (Lock)" : "🔒 Lock / Unlock", null, (s, e) => ContextToggleLock());
            _canvasContextMenu.Items.Add(isVi ? "👁️ Ẩn / Hiện (Visibility)" : "👁️ Toggle Visibility", null, (s, e) => ContextToggleVisibility());

            _canvasContextMenu.Items.Add(new ToolStripSeparator());
            _canvasContextMenu.Items.Add(isVi ? "↺ Đặt lại góc nhìn (Reset View)" : "↺ Reset View", null, (s, e) => ZoomReset());
        }

        private void ContextCut()
        {
            if (_canvasSurface != null && _canvasSurface.HasSelectedAnchors)
            {
                _canvasSurface.CutSelectedAnchors();
                return;
            }
            ContextCopy();
            ContextDelete();
        }

        private void ContextCopy()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null && !node.IsOperator)
            {
                _clipboardComponent = Tuple.Create(node.Char, node.Transform != null ? node.Transform.Clone() : new NodeCustomTransform());
            }
        }

        private void ContextPaste()
        {
            if (_clipboardComponent == null || string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null && !node.IsOperator)
            {
                node.Transform = _clipboardComponent.Item2.Clone();
                node.Transform.OffsetX += 20;
                node.Transform.OffsetY += 20;
                _transformsMap[node.Id] = node.Transform;
                ApplyTransformAndRedraw();
                RebuildInspector();
                RebuildLayersList();
            }
        }

        private void ContextDuplicate()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null && !node.IsOperator)
            {
                var t = node.Transform != null ? node.Transform : new NodeCustomTransform();
                t.OffsetX = Math.Min(400, t.OffsetX + 25);
                t.OffsetY = Math.Min(400, t.OffsetY + 25);
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildInspector();
                RebuildLayersList();
            }
        }

        private void ContextDelete()
        {
            if (_canvasSurface != null && _canvasSurface.HasSelectedAnchors)
            {
                _canvasSurface.DeleteSelectedAnchors();
                return;
            }
            if (_selectedNodeIds.Count > 1)
            {
                PushUndo("Xóa nhóm linh kiện");
                foreach (string id in new List<string>(_selectedNodeIds))
                {
                    var n = FindNodeById(_ast, id);
                    if (n != null && !n.IsOperator)
                    {
                        var t = n.Transform ?? new NodeCustomTransform();
                        t.IsVisible = false;
                        n.Transform = t;
                        _transformsMap[n.Id] = t;
                        RemoveComponentCustomPath(n.Id);
                    }
                }
                _selectedNodeIds.Clear();
                _selectedNodeId = null;
                if (_canvasSurface != null) _canvasSurface.ClearAnchorSelection(true);
                ApplyTransformAndRedraw();
                RebuildInspector();
                RebuildLayersList();
                return;
            }
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null && !node.IsOperator)
            {
                PushUndo("Xóa linh kiện");
                var t = node.Transform ?? new NodeCustomTransform();
                t.IsVisible = false;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                RemoveComponentCustomPath(node.Id);
                _selectedNodeId = null;
                if (_canvasSurface != null)
                {
                    _canvasSurface.ClearAnchorSelection(true);
                }
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildInspector();
                RebuildLayersList();
            }
        }

        private void ContextBringToFront()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null)
            {
                int maxZ = 0;
                foreach (var it in _renderItems)
                {
                    if (it.Transform != null && it.Transform.ZIndex > maxZ) maxZ = it.Transform.ZIndex;
                }
                var t = node.Transform ?? new NodeCustomTransform();
                t.ZIndex = maxZ + 1;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildLayersList();
            }
        }

        private void ContextSendToBack()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null)
            {
                int minZ = 0;
                foreach (var it in _renderItems)
                {
                    if (it.Transform != null && it.Transform.ZIndex < minZ) minZ = it.Transform.ZIndex;
                }
                var t = node.Transform ?? new NodeCustomTransform();
                t.ZIndex = minZ - 1;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildLayersList();
            }
        }

        private void ContextBringForward()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null)
            {
                var t = node.Transform ?? new NodeCustomTransform();
                t.ZIndex++;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildLayersList();
            }
        }

        private void ContextSendBackward()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null)
            {
                var t = node.Transform ?? new NodeCustomTransform();
                t.ZIndex--;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildLayersList();
            }
        }

        private void ContextAlignCenter()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null)
            {
                var t = node.Transform ?? new NodeCustomTransform();
                t.OffsetX = 0;
                t.OffsetY = 0;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildInspector();
            }
        }

        private void ContextFlipH()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null)
            {
                var t = node.Transform ?? new NodeCustomTransform();
                t.ScaleX = -t.ScaleX;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildInspector();
            }
        }

        private void ContextFlipV()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null)
            {
                var t = node.Transform ?? new NodeCustomTransform();
                t.ScaleY = -t.ScaleY;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildInspector();
            }
        }

        private void ContextRotate(int deg)
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null)
            {
                var t = node.Transform ?? new NodeCustomTransform();
                int r = t.Rotation + deg;
                while (r > 180) r -= 360;
                while (r < -180) r += 360;
                t.Rotation = r;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildInspector();
            }
        }

        private void ContextResetTransform()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            _transformsMap.Remove(_selectedNodeId);
            RecomputeAll();
        }

        private void ContextToggleLock()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null)
            {
                var t = node.Transform ?? new NodeCustomTransform();
                t.IsLocked = !t.IsLocked;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildLayersList();
                RebuildInspector();
            }
        }

        private void ContextToggleVisibility()
        {
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null)
            {
                var t = node.Transform ?? new NodeCustomTransform();
                t.IsVisible = !t.IsVisible;
                node.Transform = t;
                _transformsMap[node.Id] = t;
                ApplyTransformAndRedraw();
                RebuildLayersList();
                RebuildInspector();
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        private static Control GetFocusedControl()
        {
            try
            {
                IntPtr handle = GetFocus();
                if (handle != IntPtr.Zero)
                {
                    return Control.FromHandle(handle);
                }
            }
            catch { }
            return null;
        }

        private IdsNode FindFirstLeafNode(IdsNode node)
        {
            if (node == null) return null;
            if (!node.IsOperator) return node;
            if (node.Children != null)
            {
                foreach (var c in node.Children)
                {
                    var leaf = FindFirstLeafNode(c);
                    if (leaf != null) return leaf;
                }
            }
            return node;
        }

        public bool HandleArrowKeyNudge(Keys keyData)
        {
            if (_ast == null) return false;

            var sel = FindNodeById(_ast, _selectedNodeId);
            if (sel == null)
            {
                sel = FindFirstLeafNode(_ast);
                if (sel != null)
                {
                    _selectedNodeId = sel.Id;
                    RebuildInspector();
                    SelectTreeNodeById(_treeAst.Nodes, sel.Id);
                }
            }
            if (sel == null) return false;

            Keys keyCode = keyData & Keys.KeyCode;
            bool isShift = (keyData & Keys.Shift) == Keys.Shift;
            int step = isShift ? 10 : 1;
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            if (sel.IsOperator)
            {
                var t = sel.Transform ?? new NodeCustomTransform();
                float curRatio = t.SplitRatio ?? 0.5f;
                float ratioStep = isShift ? 0.05f : 0.01f;

                if (keyCode == Keys.Left || keyCode == Keys.Down)
                {
                    curRatio = Math.Max(0.15f, curRatio - ratioStep);
                }
                else if (keyCode == Keys.Right || keyCode == Keys.Up)
                {
                    curRatio = Math.Min(0.85f, curRatio + ratioStep);
                }
                else
                {
                    return false;
                }

                t.SplitRatio = (float)Math.Round(curRatio, 2);
                _transformsMap[sel.Id] = t;
                ApplyTransformAndRedraw();

                int pct = (int)Math.Round(t.SplitRatio.Value * 100);
                if (_trackSplitRatio != null)
                {
                    _trackSplitRatio.Value = pct;
                }
                if (_lblSplitRatio != null)
                {
                    _lblSplitRatio.Text = string.Format(isVi ? "Tỷ lệ phân chia khung: {0}% : {1}%" : "Frame Split Ratio: {0}% : {1}%",
                        pct, 100 - pct);
                }
                return true;
            }
            else
            {
                int dx = 0;
                int dy = 0;

                if (keyCode == Keys.Left) dx = -step;
                else if (keyCode == Keys.Right) dx = step;
                else if (keyCode == Keys.Up) dy = -step;
                else if (keyCode == Keys.Down) dy = step;

                if ((_toolMode == EditorToolMode.DirectSelect || _toolMode == EditorToolMode.Lasso) && _canvasSurface != null && _canvasSurface.HasSelectedAnchors)
                {
                    if (_canvasSurface.NudgeSelectedAnchors(dx, dy))
                    {
                        return true;
                    }
                }

                if (_selectedNodeIds.Count > 1)
                {
                    PushUndo("Dịch chuyển nhóm linh kiện");
                    foreach (string id in _selectedNodeIds)
                    {
                        var n = FindNodeById(_ast, id);
                        if (n != null && (n.Transform == null || !n.Transform.IsLocked))
                        {
                            var tNode = n.Transform ?? new NodeCustomTransform();
                            tNode.OffsetX = Math.Max(-400, Math.Min(400, tNode.OffsetX + dx));
                            tNode.OffsetY = Math.Max(-400, Math.Min(400, tNode.OffsetY + dy));
                            n.Transform = tNode;
                            _transformsMap[id] = tNode;
                        }
                    }
                    ApplyTransformAndRedraw();
                    return true;
                }

                var t = sel.Transform ?? new NodeCustomTransform();
                int newX = Math.Max(-200, Math.Min(200, t.OffsetX + dx));
                int newY = Math.Max(-200, Math.Min(200, t.OffsetY + dy));

                if (newX == t.OffsetX && newY == t.OffsetY)
                {
                    return true;
                }

                t.OffsetX = newX;
                t.OffsetY = newY;
                _transformsMap[sel.Id] = t;

                ApplyTransformAndRedraw();

                if (_trackOffsetX != null) _trackOffsetX.Value = t.OffsetX;
                if (_lblOffsetX != null) _lblOffsetX.Text = string.Format("{0} {1}", isVi ? "Dịch trục X (Ngang, px):" : "Offset X (px):", t.OffsetX);

                if (_trackOffsetY != null) _trackOffsetY.Value = t.OffsetY;
                if (_lblOffsetY != null) _lblOffsetY.Text = string.Format("{0} {1}", isVi ? "Dịch trục Y (Dọc, px):" : "Offset Y (px):", t.OffsetY);

                return true;
            }
        }

        private Dictionary<string, GraphicsPath> GetComponentVectorPathsSnapshot()
        {
            var dict = new Dictionary<string, GraphicsPath>();
            if (_componentCustomPaths != null)
            {
                foreach (var kvp in _componentCustomPaths)
                {
                    if (kvp.Value != null)
                    {
                        dict[kvp.Key] = (GraphicsPath)kvp.Value.Clone();
                    }
                }
            }
            if (_renderItems != null)
            {
                foreach (var it in _renderItems)
                {
                    if (it != null && !string.IsNullOrEmpty(it.NodeId) && it.VectorPath != null)
                    {
                        if (!dict.ContainsKey(it.NodeId))
                        {
                            dict[it.NodeId] = (GraphicsPath)it.VectorPath.Clone();
                        }
                    }
                }
            }
            return dict;
        }

        public void PushUndo(string actionName)
        {
            var snap = IdsComposerSnapshot.Create(
                actionName,
                _txtIdsInput != null ? _txtIdsInput.Text : "",
                _ast,
                _transformsMap,
                _customStrokes,
                _selectedNodeId,
                GetComponentVectorPathsSnapshot(),
                new Dictionary<string, RectangleF>(_componentCustomBaseRects),
                new Dictionary<string, NodeCustomTransform>(_componentCustomBaseTransforms)
            );
            _undoRedo.Push(snap);
            UpdateUndoRedoButtons();
        }

        public void PerformUndo()
        {
            if (!_undoRedo.CanUndo) return;
            var cur = IdsComposerSnapshot.Create(
                "Hiện tại",
                _txtIdsInput != null ? _txtIdsInput.Text : "",
                _ast,
                _transformsMap,
                _customStrokes,
                _selectedNodeId,
                GetComponentVectorPathsSnapshot(),
                new Dictionary<string, RectangleF>(_componentCustomBaseRects),
                new Dictionary<string, NodeCustomTransform>(_componentCustomBaseTransforms)
            );
            var prev = _undoRedo.Undo(cur);
            if (prev != null)
            {
                RestoreSnapshot(prev);
            }
            UpdateUndoRedoButtons();
        }

        public void PerformRedo()
        {
            if (!_undoRedo.CanRedo) return;
            var cur = IdsComposerSnapshot.Create(
                "Hiện tại",
                _txtIdsInput != null ? _txtIdsInput.Text : "",
                _ast,
                _transformsMap,
                _customStrokes,
                _selectedNodeId,
                GetComponentVectorPathsSnapshot(),
                new Dictionary<string, RectangleF>(_componentCustomBaseRects),
                new Dictionary<string, NodeCustomTransform>(_componentCustomBaseTransforms)
            );
            var next = _undoRedo.Redo(cur);
            if (next != null)
            {
                RestoreSnapshot(next);
            }
            UpdateUndoRedoButtons();
        }

        private void RestoreSnapshot(IdsComposerSnapshot snap)
        {
            if (snap == null) return;

            if (_txtIdsInput != null && snap.IdsFormula != null && _txtIdsInput.Text != snap.IdsFormula)
            {
                _txtIdsInput.Text = snap.IdsFormula;
            }

            _ast = snap.Ast != null ? snap.Ast.Clone() : null;

            _transformsMap.Clear();
            if (snap.TransformsMap != null)
            {
                foreach (var kvp in snap.TransformsMap)
                {
                    _transformsMap[kvp.Key] = kvp.Value != null ? kvp.Value.Clone() : new NodeCustomTransform();
                }
            }

            foreach (var p in _customStrokes) { try { p.Dispose(); } catch { } }
            _customStrokes.Clear();
            if (snap.CustomStrokes != null)
            {
                foreach (var p in snap.CustomStrokes)
                {
                    if (p != null) _customStrokes.Add((GraphicsPath)p.Clone());
                }
            }

            _selectedNodeId = snap.SelectedNodeId;

            if (_ast != null)
            {
                _renderItems = IdsLayoutEngine.ComputeLayout(_ast);
            PreserveComponentCustomPaths();
            }
            else
            {
                _renderItems.Clear();
            }

            ClearAllComponentCustomPaths();
            if (snap.ComponentVectorPaths != null)
            {
                foreach (var kvp in snap.ComponentVectorPaths)
                {
                    if (kvp.Value != null)
                    {
                        RectangleF bRect = RectangleF.Empty;
                        if (snap.ComponentBaseRects != null) snap.ComponentBaseRects.TryGetValue(kvp.Key, out bRect);
                        NodeCustomTransform bTrans = null;
                        if (snap.ComponentBaseTransforms != null) snap.ComponentBaseTransforms.TryGetValue(kvp.Key, out bTrans);
                        SetComponentCustomPath(kvp.Key, kvp.Value, bRect, bTrans);
                    }
                }
            }

            PreserveComponentCustomPaths();

            RebuildInspector();
            RebuildLayersList();
            if (_canvasSurface != null)
            {
                _canvasSurface.ResetInteractionStates();
                _canvasSurface.ClearCustomStrokeSelection();
                _canvasSurface.Invalidate();
            }
        }

        public void UpdateUndoRedoButtons()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            if (_btnUndo != null)
            {
                _btnUndo.Enabled = _undoRedo.CanUndo;
                string action = _undoRedo.CurrentUndoAction;
                _btnUndo.Text = isVi ? "↩ Hoàn tác" : "↩ Undo";
                string ttUndo = string.IsNullOrEmpty(action)
                    ? (isVi ? "Hoàn tác (Ctrl+Z)" : "Undo (Ctrl+Z)")
                    : (isVi ? "Hoàn tác: " + action + " (Ctrl+Z)" : "Undo: " + action + " (Ctrl+Z)");
                _mainToolTip.SetToolTip(_btnUndo, ttUndo);
            }
            if (_btnRedo != null)
            {
                _btnRedo.Enabled = _undoRedo.CanRedo;
                string action = _undoRedo.CurrentRedoAction;
                _btnRedo.Text = isVi ? "↪ Làm lại" : "↪ Redo";
                string ttRedo = string.IsNullOrEmpty(action)
                    ? (isVi ? "Làm lại (Ctrl+Y)" : "Redo (Ctrl+Y)")
                    : (isVi ? "Làm lại: " + action + " (Ctrl+Y)" : "Redo: " + action + " (Ctrl+Y)");
                _mainToolTip.SetToolTip(_btnRedo, ttRedo);
            }
        }

        public GraphicsPath EnsureVectorPath(GlyphRenderData item)
        {
            if (item == null) return null;
            if (item.Transform != null && !item.Transform.IsVisible) return null;
            if (item.VectorPath == null && _componentCustomPaths.ContainsKey(item.NodeId))
            {
                item.VectorPath = CreateFittedCustomPath(item.NodeId, item.TargetRect, item.Transform);
            }
            if (item.VectorPath == null && !string.IsNullOrEmpty(item.Char) && _activeFontFamily != null)
            {
                item.VectorPath = IdsGlyphRenderer.CreateComponentVectorPath(item.Char, _activeFontFamily, item.TargetRect, item.Transform);
            }
            return item.VectorPath;
        }

        public void EnsureAllComponentVectorPaths()
        {
            if (_renderItems == null) return;
            foreach (var item in _renderItems)
            {
                if (item != null && item.Transform != null && !item.Transform.IsVisible) continue;
                EnsureVectorPath(item);
            }
        }

        public void PerformAutoArrangeAndJoin()
        {
            if (_customStrokes == null || _customStrokes.Count < 2)
            {
                if (_canvasSurface != null)
                {
                    _canvasSurface.ShowHudMessage("⚡ Hàn nối (Ctrl+J): Cần ít nhất 2 nét vẽ tự do để hàn nối!");
                }
                return;
            }

            var snap = IdsComposerSnapshot.Create(
                "Hàn nối tự động",
                _txtIdsInput != null ? _txtIdsInput.Text : "",
                _ast,
                _transformsMap,
                _customStrokes,
                _selectedNodeId,
                GetComponentVectorPathsSnapshot()
            );

            PointF joinedPt;
            string msg;
            bool ok = IdsVectorGeometryService.TryAutoArrangeAndJoin(_customStrokes, 180f, out joinedPt, out msg);
            if (ok)
            {
                _undoRedo.Push(snap);
                if (_canvasSurface != null)
                {
                    _canvasSurface.ShowAutoJoinPulse(joinedPt, msg);
                }
            }
            else
            {
                snap.Dispose();
                if (_canvasSurface != null)
                {
                    _canvasSurface.ShowHudMessage(msg);
                }
            }
            UpdateUndoRedoButtons();
            RebuildLayersList();
            if (_canvasSurface != null) _canvasSurface.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Control focused = GetFocusedControl();
            if (!(focused is TextBoxBase) && !(focused is TreeView))
            {
                Keys code = keyData & Keys.KeyCode;
                if ((keyData & Keys.Control) == Keys.Control)
                {
                    if (code == Keys.Z && (keyData & Keys.Shift) == Keys.Shift) { PerformRedo(); return true; }
                    if (code == Keys.Z) { PerformUndo(); return true; }
                    if (code == Keys.Y) { PerformRedo(); return true; }
                    if (code == Keys.J) { PerformAutoArrangeAndJoin(); return true; }
                    if (code == Keys.C) { ContextCopy(); return true; }
                    if (code == Keys.V) { ContextPaste(); return true; }
                    if (code == Keys.D) { ContextDuplicate(); return true; }
                    if (code == Keys.S && (keyData & Keys.Shift) == Keys.Shift) { OnOpenFontEditorDialog(null, null); return true; }
                }
                else
                {
                    if (code == Keys.Q) { SetToolMode(EditorToolMode.Select); return true; }
                    if (code == Keys.V || code == Keys.H) { SetToolMode(EditorToolMode.Hand); return true; }
                    if (code == Keys.A) { SetToolMode(EditorToolMode.DirectSelect); return true; }
                    if (code == Keys.P) { SetToolMode(EditorToolMode.Pen); return true; }
                    if (code == Keys.Z) { SetToolMode(EditorToolMode.Zoom); return true; }
                    if (code == Keys.L) { SetToolMode(EditorToolMode.Lasso); return true; }
                    if (code == Keys.C) { SetToolMode(EditorToolMode.LassoCut); return true; }
                    if (code == Keys.Delete) { ContextDelete(); return true; }
                    if (code == Keys.Escape)
                    {
                        if (_toolMode == EditorToolMode.Pen && _activePenPoints.Count > 0)
                        {
                            _activePenPoints.Clear();
                            if (_canvasSurface != null) _canvasSurface.Invalidate();
                            return true;
                        }
                        if (_canvasSurface != null)
                        {
                            _canvasSurface.ClearActiveSelections();
                            return true;
                        }
                    }
                }
            }

            Keys keyCode = keyData & Keys.KeyCode;
            if (keyCode == Keys.Left || keyCode == Keys.Right || keyCode == Keys.Up || keyCode == Keys.Down)
            {
                if (focused is TextBoxBase || focused is TreeView)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }
                var cb = focused as ComboBox;
                if (cb != null && cb.DroppedDown)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }
                var tb = focused as SmoothTrackBar;
                if (tb != null && tb != _trackOffsetX && tb != _trackOffsetY && tb != _trackSplitRatio)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }

                if (HandleArrowKeyNudge(keyData))
                {
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public IdsComposerControl()
        {
            DoubleBuffered = true;
            _activeFontFamily = IdsGlyphRenderer.ResolveFontFamily();
            _currentPreset = IdsPresetService.Presets.Count > 0 ? IdsPresetService.Presets[0] : null;
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            _currentPresetName = _currentPreset != null ? _currentPreset.GetDisplayName(isVi) : (isVi ? "Chữ Trời (𡗶)" : "Heaven (𡗶)");

            LocalizationManager.LanguageChanged += OnLanguageChanged;

            InitializeComponent();
            RecomputeAll();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                LocalizationManager.LanguageChanged -= OnLanguageChanged;
            }
            base.Dispose(disposing);
        }

        private static readonly string[] FontPresetsVi = new[] {
            "SimSun (Tống thể - 宋体)",
            "MingLiU (Minh thể - 明體)",
            "Microsoft YaHei (Hắc thể - 黑体)",
            "KaiTi (Khải thư - 楷体)",
            "Nom Na Tong (Hán Nôm Việt Nam)",
            "SimSun-ExtB (CJK Ext B-F)"
        };

        private static readonly string[] FontPresetsEn = new[] {
            "SimSun (Song / Songti - 宋体)",
            "MingLiU (Ming / Mingti - 明體)",
            "Microsoft YaHei (Hei / Heiti - 黑体)",
            "KaiTi (Kai / Kaiti - 楷体)",
            "Nom Na Tong (Vietnamese Han Nom)",
            "SimSun-ExtB (CJK Ext B-F)"
        };

        private void PopulateFontPresets(bool isVi)
        {
            if (_cbFontPreset == null) return;
            int sel = _cbFontPreset.SelectedIndex;
            _cbFontPreset.BeginUpdate();
            _cbFontPreset.Items.Clear();
            _cbFontPreset.Items.AddRange(isVi ? FontPresetsVi : FontPresetsEn);
            _cbFontPreset.SelectedIndex = (sel >= 0 && sel < 6) ? sel : 0;
            _cbFontPreset.EndUpdate();
        }

        private void PopulateGridOptions(bool isVi)
        {
            if (_cbGrid == null) return;
            int sel = _cbGrid.SelectedIndex;
            _cbGrid.BeginUpdate();
            _cbGrid.Items.Clear();
            _cbGrid.Items.AddRange(new object[] {
                isVi ? "Mễ Tự Cách" : "Mizige",
                isVi ? "Cửu Cung Cách" : "Jiugong",
                isVi ? "Không lưới" : "None"
            });
            _cbGrid.SelectedIndex = (sel >= 0 && sel < 3) ? sel : 0;
            _cbGrid.EndUpdate();
        }

        private void PopulateThemeOptions(bool isVi)
        {
            if (_cbTheme == null) return;
            int sel = _cbTheme.SelectedIndex;
            _cbTheme.BeginUpdate();
            _cbTheme.Items.Clear();
            _cbTheme.Items.AddRange(new object[] {
                isVi ? "Giấy Xuyến Cổ" : "Classic Paper",
                isVi ? "Ấn Triện Chu Sa" : "Seal Red",
                isVi ? "Mực Tối Hoàng Kim" : "Dark Gold",
                isVi ? "Bản Vẽ Kỹ Thuật" : "Blueprint"
            });
            _cbTheme.SelectedIndex = (sel >= 0 && sel < 4) ? sel : 0;
            _cbTheme.EndUpdate();
        }

        private void OnLanguageChanged()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            // 1. Top Bar
            if (_currentPreset != null)
            {
                _currentPresetName = _currentPreset.GetDisplayName(isVi);
            }
            if (_lblPresetName != null)
            {
                _lblPresetName.Text = string.IsNullOrEmpty(_currentPresetName) ? "" : "★ " + _currentPresetName;
            }

            if (_btnBeautify != null) _btnBeautify.Text = isVi ? "✨ Làm đẹp bố cục" : "✨ Beautify IDS";
            if (_btnUndoBeautify != null) _btnUndoBeautify.Text = isVi ? "↺ Khôi phục gốc" : "↺ Undo Beautify";
            if (_btnOpenPresets != null) _btnOpenPresets.Text = isVi ? "📖 Mẫu chữ sẵn" : "📖 Presets";
            if (_btnSaveDict != null) _btnSaveDict.Text = isVi ? "💾 Lưu từ điển" : "💾 Save Glyph";
            if (_btnOpenDict != null) _btnOpenDict.Text = isVi ? "📚 Sổ bộ PUA" : "📚 PUA Registry";
            if (_btnExport != null) _btnExport.Text = isVi ? "📤 Xuất SVG / PNG" : "📤 Export SVG/PNG";
            if (_btnInjectFont != null) _btnInjectFont.Text = isVi ? "💉 Lưu vào Font..." : "💉 Save to Font...";
            if (_btnTestFont != null) _btnTestFont.Text = isVi ? "🧪 Test Font..." : "🧪 Test Font...";
            UpdateUndoRedoButtons();
            AdjustTopBarHeight();

            if (_lblGrid != null) _lblGrid.Text = isVi ? "Lưới:" : "Grid:";
            PopulateGridOptions(isVi);

            if (_lblTheme != null) _lblTheme.Text = isVi ? "Chủ đề:" : "Theme:";
            PopulateThemeOptions(isVi);

            if (_chkSmartRadicals != null) _chkSmartRadicals.Text = isVi ? "Bộ thủ thông minh" : "Smart Radicals";
            if (_chkAspectGuard != null) _chkAspectGuard.Text = isVi ? "Chống méo nét" : "Aspect Guard";

            // 2. Left Tabs & Font Stack
            if (_tabFont != null) _tabFont.Text = isVi ? "Phông chữ" : "Font Stack";
            if (_tabAst != null) _tabAst.Text = isVi ? "Cấu trúc AST" : "AST Tree";
            if (_tabLayers != null) _tabLayers.Text = isVi ? "Lớp (Layers)" : "Layers";
            if (_lblFontPresets != null) _lblFontPresets.Text = isVi ? "Chọn phông CJK hệ thống:" : "System CJK Font Preset:";
            PopulateFontPresets(isVi);
            if (_btnOpenCustomFont != null) _btnOpenCustomFont.Text = isVi ? "📂 Mở file font (.ttf, .otf)..." : "📂 Open Font File...";
            if (_lblFontStatus != null && _activeFontFamily != null)
            {
                _lblFontStatus.Text = (isVi ? "Đang dùng font: " : "Active font: ") + _activeFontFamily.Name;
            }

            // 3. Center Keyboard & Editor Toolbar
            if (_lblFormulaPrompt != null) _lblFormulaPrompt.Text = isVi ? "Công thức IDS:" : "IDS Formula:";
            if (_btnClearInput != null) _btnClearInput.Text = isVi ? "🧹 Xóa" : "🧹 Clear";
            if (_btnCopyFormula != null) _btnCopyFormula.Text = isVi ? "📋 Sao chép" : "📋 Copy";
            if (_lblIdcPrompt != null) _lblIdcPrompt.Text = isVi ? "Toán tử IDC:" : "IDC Operators:";
            if (_lblRadPrompt != null) _lblRadPrompt.Text = isVi ? "Bộ thủ:" : "Radicals:";

            if (_btnSnapGrid != null) _btnSnapGrid.Text = isVi ? "⌗ Lưới" : "⌗ Grid";
            if (_btnSmartGuides != null) _btnSmartGuides.Text = isVi ? "⫿ Gióng" : "⫿ Guides";
            if (_btnResetZoom != null) _btnResetZoom.Text = isVi ? "↺ Đặt lại" : "↺ Reset";
            UpdateToolButtons();

            // 4. Rebuild AST Tree, Inspector, Layers & Legibility Strip
            RebuildAstTree();
            RebuildInspector();
            RebuildLayersList();
            UpdateLegibilityStrip();
        }

        private void InitializeComponent()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(248, 250, 252);

            // 1. Studio Header Toolbar (Native Windows Style - Auto Wrapping & Responsive)
            _topBar = new Panel
            {
                Dock = DockStyle.Top,
                Padding = new Padding(6, 4, 6, 4),
                BackColor = Color.FromArgb(245, 246, 248)
            };

            var topBarBorder = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(218, 222, 229)
            };
            _topBar.Controls.Add(topBarBorder);

            _topFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                Margin = new Padding(0)
            };

            _topBar.Resize += (s, e) => AdjustTopBarHeight();
            _topFlow.Layout += (s, e) => AdjustTopBarHeight();

            _lblPresetName = new Label
            {
                Text = string.IsNullOrEmpty(_currentPresetName) ? "" : "★ " + _currentPresetName,
                AutoSize = true,
                Margin = new Padding(4, 7, 10, 0),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };

            _btnBeautify = CreateHeaderButton(isVi ? "✨ Làm đẹp bố cục" : "✨ Beautify IDS", OnBeautifyIds, Color.FromArgb(124, 58, 237));
            _btnUndoBeautify = CreateHeaderButton(isVi ? "↺ Khôi phục gốc" : "↺ Undo Beautify", OnUndoBeautify);
            _btnUndoBeautify.Visible = false;

            _btnUndo = CreateHeaderButton(isVi ? "↩ Hoàn tác" : "↩ Undo", (s, e) => PerformUndo());
            _btnUndo.Enabled = false;
            _mainToolTip.SetToolTip(_btnUndo, isVi ? "Hoàn tác (Ctrl+Z)" : "Undo (Ctrl+Z)");
            _btnRedo = CreateHeaderButton(isVi ? "↪ Làm lại" : "↪ Redo", (s, e) => PerformRedo());
            _btnRedo.Enabled = false;
            _mainToolTip.SetToolTip(_btnRedo, isVi ? "Làm lại (Ctrl+Y)" : "Redo (Ctrl+Y)");

            _btnOpenPresets = CreateHeaderButton(isVi ? "📖 Mẫu chữ sẵn" : "📖 Presets", OnOpenPresets);
            _btnSaveDict = CreateHeaderButton(isVi ? "💾 Lưu từ điển" : "💾 Save Glyph", OnOpenSaveDialog);
            _btnOpenDict = CreateHeaderButton(isVi ? "📚 Sổ bộ PUA" : "📚 PUA Registry", OnOpenDictDialog);
            _btnExport = CreateHeaderButton(isVi ? "📤 Xuất SVG / PNG" : "📤 Export SVG/PNG", OnOpenExportDialog, Color.FromArgb(0, 102, 204));
            _btnInjectFont = CreateHeaderButton(isVi ? "💉 Lưu vào Font..." : "💉 Save to Font...", OnOpenFontEditorDialog, Color.FromArgb(180, 50, 50));
            _btnTestFont = CreateHeaderButton(isVi ? "🧪 Test Font..." : "🧪 Test Font...", (s, e) => { if (MainForm.Instance != null) MainForm.Instance.SwitchToFontTestTab(); }, Color.FromArgb(16, 120, 80));

            var sep1 = CreateHeaderSep();

            _lblGrid = new Label { Text = isVi ? "Lưới:" : "Grid:", AutoSize = true, Margin = new Padding(4, 7, 4, 0), Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(51, 65, 85) };
            _cbGrid = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(0, 4, 8, 0) };
            PopulateGridOptions(isVi);
            _cbGrid.SelectedIndex = 0;
            _cbGrid.SelectedIndexChanged += (s, e) =>
            {
                _gridType = (IdsCanvasGridType)_cbGrid.SelectedIndex;
                _canvasSurface.Invalidate();
            };

            _lblTheme = new Label { Text = isVi ? "Chủ đề:" : "Theme:", AutoSize = true, Margin = new Padding(4, 7, 4, 0), Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(51, 65, 85) };
            _cbTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Margin = new Padding(0, 4, 8, 0) };
            PopulateThemeOptions(isVi);
            _cbTheme.SelectedIndex = 0;
            _cbTheme.SelectedIndexChanged += (s, e) =>
            {
                _colorTheme = (IdsColorTheme)_cbTheme.SelectedIndex;
                _canvasSurface.Invalidate();
                UpdateLegibilityStrip();
            };

            var sep2 = CreateHeaderSep();

            _chkBBoxes = new CheckBox
            {
                Text = "BBoxes",
                AutoSize = true,
                Margin = new Padding(4, 7, 8, 0),
                ForeColor = Color.FromArgb(30, 41, 59),
                Checked = _showBBoxes
            };
            _chkBBoxes.CheckedChanged += (s, e) =>
            {
                _showBBoxes = _chkBBoxes.Checked;
                _canvasSurface.Invalidate();
            };

            _chkSmartRadicals = new CheckBox
            {
                Text = isVi ? "Bộ thủ thông minh" : "Smart Radicals",
                AutoSize = true,
                Margin = new Padding(4, 7, 8, 0),
                ForeColor = Color.FromArgb(30, 41, 59),
                Checked = _smartRadicalsEnabled
            };
            _chkSmartRadicals.CheckedChanged += (s, e) =>
            {
                _smartRadicalsEnabled = _chkSmartRadicals.Checked;
                RecomputeAll();
            };

            _chkAspectGuard = new CheckBox
            {
                Text = isVi ? "Chống méo nét" : "Aspect Guard",
                AutoSize = true,
                Margin = new Padding(4, 7, 8, 0),
                ForeColor = Color.FromArgb(30, 41, 59),
                Checked = _aspectGuardEnabled
            };
            _chkAspectGuard.CheckedChanged += (s, e) =>
            {
                _aspectGuardEnabled = _chkAspectGuard.Checked;
                ApplyTransformAndRedraw();
            };

            _topFlow.Controls.AddRange(new Control[] {
                _lblPresetName, _btnBeautify, _btnUndoBeautify, _btnUndo, _btnRedo, _btnOpenPresets, _btnSaveDict, _btnOpenDict, _btnExport, _btnInjectFont, _btnTestFont,
                sep1, _lblGrid, _cbGrid, _lblTheme, _cbTheme, sep2,
                _chkBBoxes, _chkSmartRadicals, _chkAspectGuard
            });
            _topBar.Controls.Add(_topFlow);
            AdjustTopBarHeight();

            // 2. Main 3-Column Split
            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1
            };

            // Left Column (Tabs: Font Stack & AST Tree)
            _tabLeft = new TabControl { Dock = DockStyle.Fill };
            _tabFont = new TabPage(isVi ? "Phông chữ" : "Font Stack");
            _tabAst = new TabPage(isVi ? "Cấu trúc AST" : "AST Tree");

            // Left Tab 1: Font
            var fontPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            _lblFontPresets = new Label { Text = isVi ? "Chọn phông CJK hệ thống:" : "System CJK Font Preset:", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _cbFontPreset = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Height = 26 };
            PopulateFontPresets(isVi);
            _cbFontPreset.SelectedIndex = 0;
            _cbFontPreset.SelectedIndexChanged += (s, e) =>
            {
                switch (_cbFontPreset.SelectedIndex)
                {
                    case 0: _activeFontFamily = IdsGlyphRenderer.ResolveFontFamily("SimSun"); break;
                    case 1: _activeFontFamily = IdsGlyphRenderer.ResolveFontFamily("MingLiU"); break;
                    case 2: _activeFontFamily = IdsGlyphRenderer.ResolveFontFamily("Microsoft YaHei"); break;
                    case 3: _activeFontFamily = IdsGlyphRenderer.ResolveFontFamily("KaiTi"); break;
                    case 4: _activeFontFamily = IdsGlyphRenderer.ResolveFontFamily("Nom Na Tong"); break;
                    case 5: _activeFontFamily = IdsGlyphRenderer.ResolveFontFamily("SimSun-ExtB"); break;
                }
                bool curVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                _lblFontStatus.Text = (curVi ? "Đang dùng font: " : "Active font: ") + _activeFontFamily.Name;
                _canvasSurface.Invalidate();
                UpdateLegibilityStrip();
            };

            _btnOpenCustomFont = new Button { Text = isVi ? "📂 Mở file font (.ttf, .otf)..." : "📂 Open Font File...", Dock = DockStyle.Top, Height = 30, Margin = new Padding(0, 8, 0, 0) };
            _btnOpenCustomFont.Click += (s, e) =>
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Font Files (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            var pfc = new System.Drawing.Text.PrivateFontCollection();
                            byte[] fontBytes = System.IO.File.ReadAllBytes(ofd.FileName);
                            IntPtr fontBuffer = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(fontBytes.Length);
                            System.Runtime.InteropServices.Marshal.Copy(fontBytes, 0, fontBuffer, fontBytes.Length);
                            pfc.AddMemoryFont(fontBuffer, fontBytes.Length);
                            if (pfc.Families.Length > 0)
                            {
                                _activeFontFamily = pfc.Families[0];
                                bool curVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                                _lblFontStatus.Text = (curVi ? "Đã nạp tệp: " : "Loaded file: ") + _activeFontFamily.Name;
                                _canvasSurface.Invalidate();
                                UpdateLegibilityStrip();
                            }
                        }
                        catch (Exception ex)
                        {
                            bool curVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                            MessageBox.Show((curVi ? "Lỗi đọc font: " : "Error reading font: ") + ex.Message, curVi ? "Lỗi" : "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            };

            _lblFontStatus = new Label
            {
                Text = (isVi ? "Đang dùng font: " : "Active font: ") + _activeFontFamily.Name,
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(0, 8, 0, 0),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = Color.FromArgb(71, 85, 105)
            };

            fontPnl.Controls.Add(_lblFontStatus);
            fontPnl.Controls.Add(_btnOpenCustomFont);
            fontPnl.Controls.Add(_cbFontPreset);
            fontPnl.Controls.Add(_lblFontPresets);
            _tabFont.Controls.Add(fontPnl);

            // Left Tab 2: AST Tree
            _treeAst = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F),
                ShowLines = true,
                HideSelection = false
            };
            _treeAst.AfterSelect += (s, e) =>
            {
                if (e.Node != null && e.Node.Tag != null)
                {
                    var node = (IdsNode)e.Node.Tag;
                    _selectedNodeId = node.Id;
                    if (_canvasSurface != null) { _canvasSurface.ClearCustomStrokeSelection(); _canvasSurface.Invalidate(); }
                    RebuildInspector();
                }
            };
            _tabAst.Controls.Add(_treeAst);

            // Left Tab 3: Layers
            _tabLayers = new TabPage(isVi ? "Lớp (Layers)" : "Layers");
            var layersPnl = new Panel { Dock = DockStyle.Fill };
            _lvLayers = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Segoe UI", 9F)
            };
            _lvLayers.Columns.Add(isVi ? "Trạng thái" : "Status", 75);
            _lvLayers.Columns.Add(isVi ? "Ký tự" : "Char", 50);
            _lvLayers.Columns.Add(isVi ? "Tên lớp" : "Layer", 95);
            _lvLayers.Columns.Add("Z", 35);
            _lvLayers.SelectedIndexChanged += (s, e) =>
            {
                if (_lvLayers.SelectedItems.Count > 1)
                {
                    _selectedNodeIds.Clear();
                    foreach (ListViewItem item in _lvLayers.SelectedItems)
                    {
                        string id = item.Tag as string;
                        if (!string.IsNullOrEmpty(id)) _selectedNodeIds.Add(id);
                    }
                    _selectedNodeId = _lvLayers.SelectedItems[_lvLayers.SelectedItems.Count - 1].Tag as string;
                    if (_canvasSurface != null) _canvasSurface.ClearCustomStrokeSelection();
                    RebuildInspector();
                    if (_canvasSurface != null) _canvasSurface.Invalidate();
                }
                else if (_lvLayers.SelectedItems.Count == 1)
                {
                    string id = _lvLayers.SelectedItems[0].Tag as string;
                    if (!string.IsNullOrEmpty(id))
                    {
                        _selectedNodeIds.Clear();
                        _selectedNodeIds.Add(id);
                        _selectedNodeId = id;
                        if (_canvasSurface != null) _canvasSurface.ClearCustomStrokeSelection();
                        RebuildInspector();
                        SelectTreeNodeById(_treeAst.Nodes, id);
                        if (_canvasSurface != null) _canvasSurface.Invalidate();
                    }
                }
            };

            var layersBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                Padding = new Padding(2),
                BackColor = Color.FromArgb(245, 246, 248)
            };
            var btnVis = new Button { Text = "👁️", Width = 36, Height = 26, Cursor = Cursors.Hand };
            btnVis.Click += (s, e) => ContextToggleVisibility();
            var btnLock = new Button { Text = "🔒", Width = 36, Height = 26, Cursor = Cursors.Hand };
            btnLock.Click += (s, e) => ContextToggleLock();
            var btnUp = new Button { Text = "⬆️", Width = 36, Height = 26, Cursor = Cursors.Hand };
            btnUp.Click += (s, e) => ContextBringForward();
            var btnDown = new Button { Text = "⬇️", Width = 36, Height = 26, Cursor = Cursors.Hand };
            btnDown.Click += (s, e) => ContextSendBackward();
            layersBar.Controls.AddRange(new Control[] { btnVis, btnLock, btnUp, btnDown });

            layersPnl.Controls.Add(_lvLayers);
            layersPnl.Controls.Add(layersBar);
            _tabLayers.Controls.Add(layersPnl);

            _tabLeft.TabPages.Add(_tabFont);
            _tabLeft.TabPages.Add(_tabAst);
            _tabLeft.TabPages.Add(_tabLayers);
            _mainSplit.Panel1.Controls.Add(_tabLeft);

            // Right Split: Center (Canvas + Keyboard) and Right (Inspector)
            _rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2
            };

            // Center Column Panel
            var centerPnl = new Panel { Dock = DockStyle.Fill };

            // Center Top: Fast Keyboard & Input
            var kbPnl = CreateKeyboardPanel();
            centerPnl.Controls.Add(kbPnl);

            // Center Bottom: Legibility Strip
            _pnlLegibility = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 108,
                Padding = new Padding(8, 6, 8, 4),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            var legBorderTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = Color.FromArgb(226, 232, 240)
            };
            _pnlLegibility.Controls.Add(legBorderTop);
            centerPnl.Controls.Add(_pnlLegibility);

            // Center Middle: Canvas Container with Zoom Toolbar
            _pnlCanvasContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 245, 249) };
            _pnlCanvasContainer.MouseEnter += (s, e) =>
            {
                if (!_pnlCanvasContainer.Focused && _pnlCanvasContainer.CanFocus)
                {
                    _pnlCanvasContainer.Focus();
                }
            };

            // Illustrator Studio Editor Toolbar
            var pnlToolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(6, 4, 6, 4),
                BackColor = Color.FromArgb(245, 246, 248)
            };

            var toolbarBorder = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(218, 222, 229)
            };
            pnlToolbar.Controls.Add(toolbarBorder);

            // Left tools flow
            var toolsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };

            Func<string, string, string, EditorToolMode, Button> createToolBtn = (text, ttVi, ttEn, mode) =>
            {
                var btn = new Button
                {
                    Text = text,
                    Width = 36,
                    Height = 26,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(241, 245, 249),
                    ForeColor = Color.FromArgb(30, 41, 59),
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                    Margin = new Padding(1, 0, 1, 0),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                btn.FlatAppearance.BorderSize = 1;
                var tt = new ToolTip();
                tt.SetToolTip(btn, isVi ? ttVi : ttEn);
                btn.Click += (s, e) => SetToolMode(mode);
                return btn;
            };

            _btnToolSelect = createToolBtn("↖ Q", "Free Transformer (Q) - Chọn & kéo hộp bao 8 điểm, xoay", "Free Transformer (Q) - Bounding Box Scale & Rotate", EditorToolMode.Select);
            _btnToolDirectSelect = createToolBtn("⇱ A", "Direct Selection Tool (A) - Kéo chỉnh từng điểm neo vector nét", "Direct Selection Tool (A) - Edit vector anchor points", EditorToolMode.DirectSelect);
            _btnToolPen = createToolBtn("✒ P", "Pen Tool (P) - Vẽ nét vector tùy chỉnh (Click thêm điểm, Enter/Dbl-click xong)", "Pen Tool (P) - Draw custom vector strokes", EditorToolMode.Pen);
            _btnToolHand = createToolBtn("✋ V", "Bàn tay vuốt tự do (V / H) - Kéo vuốt di chuyển toàn bộ Canvas", "Hand / Free Pan Tool (V / H) - Pan canvas", EditorToolMode.Hand);
            _btnToolZoom = createToolBtn("🔍 Z", "Zoom Tool (Z) - Click phóng to, Alt+Click thu nhỏ", "Zoom Tool (Z) - Zoom tool", EditorToolMode.Zoom);
            _btnToolLasso = createToolBtn("🪢 L", "Lasso Tool (L) - Khoanh vùng tự do chọn linh kiện & nét", "Lasso Tool (L) - Freehand selection loop", EditorToolMode.Lasso);
            _btnToolCut = createToolBtn("✂ C", "Lasso Cut / Knife Tool (C) - Cắt xuyên nét chữ thành các đoạn độc lập", "Knife Tool (C) - Cut strokes into separate segments", EditorToolMode.LassoCut);

            var sepTools = new Panel
            {
                Width = 1,
                Height = 20,
                BackColor = Color.FromArgb(203, 213, 225),
                Margin = new Padding(4, 3, 4, 0)
            };

            _btnSnapGrid = new Button
            {
                Text = isVi ? "⌗ Lưới" : "⌗ Grid",
                Width = 58,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(1, 0, 1, 0),
                Cursor = Cursors.Hand
            };
            _btnSnapGrid.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            _btnSnapGrid.FlatAppearance.BorderSize = 1;
            new ToolTip().SetToolTip(_btnSnapGrid, isVi ? "Bám lưới (Snap to Grid)" : "Snap to Grid");
            _btnSnapGrid.Click += (s, e) =>
            {
                _snapToGridEnabled = !_snapToGridEnabled;
                UpdateToolButtons();
            };

            _btnSmartGuides = new Button
            {
                Text = isVi ? "⫿ Gióng" : "⫿ Guides",
                Width = 62,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(252, 231, 243),
                ForeColor = Color.FromArgb(190, 24, 93),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(1, 0, 1, 0),
                Cursor = Cursors.Hand
            };
            _btnSmartGuides.FlatAppearance.BorderColor = Color.FromArgb(244, 114, 182);
            _btnSmartGuides.FlatAppearance.BorderSize = 1;
            new ToolTip().SetToolTip(_btnSmartGuides, isVi ? "Đường gióng thông minh (Smart Guides)" : "Smart Guides Alignment");
            _btnSmartGuides.Click += (s, e) =>
            {
                _smartGuidesEnabled = !_smartGuidesEnabled;
                UpdateToolButtons();
            };

            _btnAutoJoin = new Button
            {
                Text = isVi ? "🔗 Ghép nối (Ctrl+J)" : "🔗 Auto-Join (Ctrl+J)",
                AutoSize = true,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(240, 253, 244),
                ForeColor = Color.FromArgb(22, 101, 52),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(2, 0, 1, 0),
                Cursor = Cursors.Hand
            };
            _btnAutoJoin.FlatAppearance.BorderColor = Color.FromArgb(134, 239, 172);
            _btnAutoJoin.FlatAppearance.BorderSize = 1;
            new ToolTip().SetToolTip(_btnAutoJoin, isVi ? "Automatic Arrange & Auto-Join (Ctrl+J): Tự động bắt dính đầu mút gần nhất, gióng thẳng hàng và hàn liền nét cắt" : "Auto-Join & Arrange (Ctrl+J): Snap and weld closest cut endpoints");
            _btnAutoJoin.Click += (s, e) => PerformAutoArrangeAndJoin();

            toolsFlow.Controls.AddRange(new Control[] {
                _btnToolSelect, _btnToolDirectSelect, _btnToolPen, _btnToolHand, _btnToolZoom,
                _btnToolLasso, _btnToolCut,
                sepTools, _btnSnapGrid, _btnSmartGuides, _btnAutoJoin
            });

            // Right zoom controls
            var zoomControlsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            _btnZoomOut = new Button
            {
                Text = "➖",
                Width = 30,
                Height = 26,
                FlatStyle = FlatStyle.System
            };
            _btnZoomOut.Click += (s, e) => ZoomOut();

            _lblZoomVal = new Label
            {
                Text = "100%",
                Width = 46,
                Height = 26,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            _btnZoomIn = new Button
            {
                Text = "➕",
                Width = 30,
                Height = 26,
                FlatStyle = FlatStyle.System
            };
            _btnZoomIn.Click += (s, e) => ZoomIn();

            _btnResetZoom = new Button
            {
                Text = isVi ? "↺ Đặt lại" : "↺ Reset",
                Width = 62,
                Height = 26,
                FlatStyle = FlatStyle.System
            };
            _btnResetZoom.Click += (s, e) => ZoomReset();

            zoomControlsFlow.Controls.AddRange(new Control[] { _btnZoomOut, _lblZoomVal, _btnZoomIn, _btnResetZoom });

            // Middle status
            _lblToolStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Padding = new Padding(8, 0, 8, 0),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(71, 85, 105),
                Text = isVi ? "↖ Selection (V): Bounding Box kéo dãn 8 điểm, xoay & di chuyển trực tiếp" : "↖ Selection (V): Bounding Box scale, rotate & direct drag"
            };

            pnlToolbar.Controls.Add(_lblToolStatus);
            pnlToolbar.Controls.Add(toolsFlow);
            pnlToolbar.Controls.Add(zoomControlsFlow);
            toolsFlow.BringToFront();
            zoomControlsFlow.BringToFront();
            _lblToolStatus.BringToFront();

            _canvasSurface = new CanvasSurface(this);
            _canvasSurface.Dock = DockStyle.Fill;

            SetupCanvasContextMenu();
            _canvasSurface.ContextMenuStrip = _canvasContextMenu;

            UpdateToolButtons();

            _pnlCanvasContainer.Controls.Add(pnlToolbar);
            _pnlCanvasContainer.Controls.Add(_canvasSurface);
            pnlToolbar.SendToBack();
            _canvasSurface.BringToFront();

            centerPnl.Controls.Add(_pnlCanvasContainer);
            kbPnl.SendToBack();
            _pnlLegibility.SendToBack();
            _pnlCanvasContainer.BringToFront();

            _rightSplit.Panel1.Controls.Add(centerPnl);

            // Right Column: Node Inspector
            _pnlInspector = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                AutoScroll = true,
                BackColor = Color.FromArgb(250, 250, 250)
            };
            _rightSplit.Panel2.Controls.Add(_pnlInspector);

            _mainSplit.Panel2.Controls.Add(_rightSplit);
            Controls.Add(_mainSplit);
            Controls.Add(_topBar);

            SafeAdjustSplitters();
        }

        private void SafeAdjustSplitters()
        {
            try
            {
                if (_mainSplit != null && _mainSplit.Width > 350)
                {
                    int targetLeft = 260;
                    int minLeft = _mainSplit.Panel1MinSize;
                    int maxLeft = _mainSplit.Width - _mainSplit.Panel2MinSize;
                    if (targetLeft < minLeft) targetLeft = minLeft;
                    if (targetLeft > maxLeft) targetLeft = maxLeft;
                    if (targetLeft >= minLeft && targetLeft <= maxLeft)
                    {
                        _mainSplit.SplitterDistance = targetLeft;
                    }
                }
            }
            catch { }

            try
            {
                if (_rightSplit != null && _rightSplit.Width > 450)
                {
                    int targetCenter = _rightSplit.Width - 320;
                    int minCenter = _rightSplit.Panel1MinSize;
                    int maxCenter = _rightSplit.Width - _rightSplit.Panel2MinSize;
                    if (targetCenter < minCenter) targetCenter = minCenter;
                    if (targetCenter > maxCenter) targetCenter = maxCenter;
                    if (targetCenter >= minCenter && targetCenter <= maxCenter)
                    {
                        _rightSplit.SplitterDistance = targetCenter;
                    }
                }
            }
            catch { }
        }

        private void AdjustTopBarHeight()
        {
            if (_topBar == null || _topFlow == null || _topBar.IsDisposed || _topFlow.IsDisposed) return;
            int availableWidth = _topBar.ClientSize.Width - _topBar.Padding.Horizontal;
            if (availableWidth <= 0) return;
            int prefH = _topFlow.GetPreferredSize(new Size(availableWidth, 0)).Height;
            int newH = prefH + _topBar.Padding.Vertical + 1;
            if (_topBar.Height != newH && newH >= 34)
            {
                _topBar.Height = newH;
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            SafeAdjustSplitters();
            AdjustTopBarHeight();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            SafeAdjustSplitters();
            AdjustTopBarHeight();
        }

        private Panel CreateKeyboardPanel()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            var pnl = new Panel
            {
                Dock = DockStyle.Top,
                Height = 106,
                Padding = new Padding(8, 4, 8, 4),
                BackColor = Color.FromArgb(250, 251, 252)
            };

            var borderBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(226, 232, 240)
            };
            pnl.Controls.Add(borderBottom);

            // Row 1: Formula Input & Actions
            var row1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _lblFormulaPrompt = new Label
            {
                Text = isVi ? "Công thức IDS:" : "IDS Formula:",
                AutoSize = true,
                Margin = new Padding(0, 5, 6, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            _txtIdsInput = new TextBox
            {
                Width = 230,
                Font = new Font("SimSun", 12F),
                Text = _idsString,
                Margin = new Padding(0, 2, 6, 0)
            };
            _txtIdsInput.TextChanged += (s, e) =>
            {
                if (_idsString != _txtIdsInput.Text)
                {
                    _idsString = _txtIdsInput.Text;
                    _currentPreset = null;
                    _currentPresetName = "";
                    _lblPresetName.Text = "";
                    RecomputeAll();
                }
            };

            _btnClearInput = new Button
            {
                Text = isVi ? "🧹 Xóa" : "🧹 Clear",
                Width = 65,
                Height = 26,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(0, 1, 6, 0)
            };
            _btnClearInput.Click += (s, e) =>
            {
                _txtIdsInput.Text = "";
            };

            _btnCopyFormula = new Button
            {
                Text = isVi ? "📋 Sao chép" : "📋 Copy",
                Width = 80,
                Height = 26,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(0, 1, 6, 0)
            };
            _btnCopyFormula.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_txtIdsInput.Text))
                {
                    try { Clipboard.SetText(_txtIdsInput.Text); } catch { }
                }
            };

            row1.Controls.AddRange(new Control[] { _lblFormulaPrompt, _txtIdsInput, _btnClearInput, _btnCopyFormula });

            // Row 2: 12 IDC Operators
            var row2 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0),
                Padding = new Padding(0, 1, 0, 1)
            };

            _lblIdcPrompt = new Label
            {
                Text = isVi ? "Toán tử IDC:" : "IDC Operators:",
                AutoSize = true,
                Margin = new Padding(0, 5, 4, 0),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105)
            };
            row2.Controls.Add(_lblIdcPrompt);

            string[] opSymbols = new[] { "⿰", "⿱", "⿲", "⿳", "⿴", "⿵", "⿶", "⿷", "⿸", "⿹", "⿺", "⿻" };
            var fontOp = new Font("SimSun", 12F, FontStyle.Bold);

            foreach (var op in opSymbols)
            {
                var btn = new Button
                {
                    Text = op,
                    Width = 30,
                    Height = 26,
                    Font = fontOp,
                    Margin = new Padding(1, 0, 1, 0),
                    FlatStyle = FlatStyle.System
                };
                btn.Click += (s, e) => InsertTextIntoFormula(((Button)s).Text);
                row2.Controls.Add(btn);
            }

            // Row 3: 18 Quick Radicals
            var row3 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0),
                Padding = new Padding(0, 1, 0, 1)
            };

            _lblRadPrompt = new Label
            {
                Text = isVi ? "Bộ thủ:" : "Radicals:",
                AutoSize = true,
                Margin = new Padding(0, 5, 4, 0),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105)
            };
            row3.Controls.Add(_lblRadPrompt);

            string[] quickRads = new[] { "氵", "亻", "扌", "忄", "艹", "辶", "囗", "門", "木", "火", "土", "金", "日", "月", "女", "口", "言", "心" };
            var fontRad = new Font("SimSun", 10.5F, FontStyle.Regular);
            foreach (var rad in quickRads)
            {
                var btn = new Button
                {
                    Text = rad,
                    Width = 26,
                    Height = 26,
                    Font = fontRad,
                    Margin = new Padding(1, 0, 1, 0),
                    FlatStyle = FlatStyle.System
                };
                btn.Click += (s, e) => InsertTextIntoFormula(((Button)s).Text);
                row3.Controls.Add(btn);
            }

            pnl.Controls.Add(row3);
            pnl.Controls.Add(row2);
            pnl.Controls.Add(row1);
            row3.BringToFront();
            row2.BringToFront();
            row1.BringToFront();

            return pnl;
        }

        private void InsertTextIntoFormula(string text)
        {
            int sel = _txtIdsInput.SelectionStart;
            string cur = _txtIdsInput.Text;
            _txtIdsInput.Text = cur.Insert(sel, text);
            _txtIdsInput.SelectionStart = sel + text.Length;
            _txtIdsInput.Focus();
        }

        private Button CreateHeaderButton(string text, EventHandler onClick, Color? backColor = null)
        {
            bool isCustom = backColor.HasValue;
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 28,
                Font = new Font("Segoe UI", 9F, isCustom ? FontStyle.Bold : FontStyle.Regular),
                FlatStyle = isCustom ? FlatStyle.Flat : FlatStyle.System,
                BackColor = backColor ?? SystemColors.Control,
                ForeColor = isCustom ? Color.White : Color.FromArgb(30, 41, 59),
                Margin = new Padding(2, 3, 3, 2),
                Cursor = Cursors.Hand
            };
            if (isCustom)
            {
                btn.FlatAppearance.BorderSize = 0;
            }
            btn.Click += onClick;
            return btn;
        }

        private Panel CreateHeaderSep()
        {
            return new Panel
            {
                Width = 1,
                Height = 20,
                BackColor = Color.FromArgb(218, 222, 229),
                Margin = new Padding(6, 8, 6, 0)
            };
        }

        public void RecomputeAll()
        {
            // 1. Parse AST
            _ast = IdsParserService.ParseIds(_idsString, _smartRadicalsEnabled, _transformsMap);

            // Check parser flags (Fix Bug B3: Visual feedback for trailing tokens and missing operands)
            if (_lblFormulaPrompt != null)
            {
                bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                if (_ast != null && _ast.HasTrailingTokens)
                {
                    _lblFormulaPrompt.Text = isVi ? "Công thức IDS (⚠️ Dư token cuối):" : "IDS Formula (⚠️ Trailing tokens):";
                    _lblFormulaPrompt.ForeColor = Color.FromArgb(220, 38, 38);
                }
                else if (_ast != null && _ast.HasPlaceholders)
                {
                    _lblFormulaPrompt.Text = isVi ? "Công thức IDS (⚠️ Thiếu thành phần):" : "IDS Formula (⚠️ Missing operand):";
                    _lblFormulaPrompt.ForeColor = Color.FromArgb(217, 119, 6);
                }
                else if (_ast != null && !string.IsNullOrEmpty(_ast.EnclosureWarning))
                {
                    _lblFormulaPrompt.Text = isVi ? string.Format("Công thức IDS (⚠️ '{0}' không phải bộ thủ bao bọc):", _ast.EnclosureWarning)
                                                  : string.Format("IDS Formula (⚠️ '{0}' is not an enclosing radical):", _ast.EnclosureWarning);
                    _lblFormulaPrompt.ForeColor = Color.FromArgb(217, 119, 6);
                }
                else
                {
                    _lblFormulaPrompt.Text = isVi ? "Công thức IDS:" : "IDS Formula:";
                    _lblFormulaPrompt.ForeColor = Color.FromArgb(30, 41, 59);
                }
            }

            // Default selection to root if not selected
            if (string.IsNullOrEmpty(_selectedNodeId))
            {
                _selectedNodeId = _ast.Id;
            }

            // 2. Compute spatial layout
            _renderItems = IdsLayoutEngine.ComputeLayout(_ast);
            PreserveComponentCustomPaths();

            // 3. Update AST TreeView
            RebuildAstTree();

            // 4. Update Inspector & Layers
            RebuildInspector();
            RebuildLayersList();

            // 5. Invalidate canvas & legibility strip
            if (_canvasSurface != null) _canvasSurface.Invalidate();
            UpdateLegibilityStrip();
        }

        private void RebuildAstTree()
        {
            _treeAst.BeginUpdate();
            _treeAst.Nodes.Clear();

            if (_ast != null)
            {
                var root = CreateTreeNode(_ast);
                _treeAst.Nodes.Add(root);
                root.ExpandAll();

                // Highlight selected node in tree
                SelectTreeNodeById(_treeAst.Nodes, _selectedNodeId);
            }

            _treeAst.EndUpdate();
        }

        private TreeNode CreateTreeNode(IdsNode node)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            string label = node.IsOperator
                ? string.Format(isVi ? "Toán tử [{0}]" : "Operator [{0}]", node.Operator)
                : string.Format(isVi ? "Ký tự '{0}'" : "Component '{0}'", node.Char);

            var tn = new TreeNode(label) { Tag = node };
            if (node.Children != null)
            {
                foreach (var c in node.Children)
                {
                    tn.Nodes.Add(CreateTreeNode(c));
                }
            }
            return tn;
        }

        private bool SelectTreeNodeById(TreeNodeCollection nodes, string id)
        {
            foreach (TreeNode n in nodes)
            {
                var node = n.Tag as IdsNode;
                if (node != null && node.Id == id)
                {
                    _treeAst.SelectedNode = n;
                    return true;
                }
                if (SelectTreeNodeById(n.Nodes, id)) return true;
            }
            return false;
        }

        private IdsNode FindNodeById(IdsNode node, string id)
        {
            if (node == null) return null;
            if (node.Id == id) return node;
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    var found = FindNodeById(child, id);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private void RebuildInspector()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            _pnlInspector.SuspendLayout();
            _pnlInspector.Controls.Clear();

            if (_selectedNodeIds.Count > 1)
            {
                BuildMultiSelectionInspector();
                _pnlInspector.ResumeLayout();
                return;
            }

            var sel = FindNodeById(_ast, _selectedNodeId) ?? _ast;
            if (sel == null)
            {
                _pnlInspector.ResumeLayout();
                return;
            }

            var t = sel.Transform ?? new NodeCustomTransform();

            // 1. Header Box (Always at the top)
            var pnlHead = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(0, 2, 0, 4),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var lblTitle = new Label
            {
                Text = sel.IsOperator
                    ? (isVi ? "Toán tử: " + sel.Operator : "Operator: " + sel.Operator)
                    : (isVi ? "Ký tự nét: " + sel.Char : "Component: " + sel.Char),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Dock = DockStyle.Left,
                AutoSize = true,
                Padding = new Padding(0, 4, 0, 0),
                ForeColor = Color.FromArgb(15, 23, 42)
            };

            var btnReset = new Button
            {
                Text = isVi ? "↺ Đặt lại" : "↺ Reset",
                Width = 75,
                Height = 26,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.System
            };
            btnReset.Click += (s, e) =>
            {
                _transformsMap.Remove(sel.Id);
                RecomputeAll();
            };

            pnlHead.Controls.Add(lblTitle);
            pnlHead.Controls.Add(btnReset);

            var borderHead = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(226, 232, 240)
            };
            pnlHead.Controls.Add(borderHead);

            var rows = new List<Control>();
            rows.Add(pnlHead);

            // 2. Optical Beautify Action Box & Report in Inspector
            var pnlBeautifyCard = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(0, 4, 0, 6),
                BackColor = Color.Transparent
            };

            var btnInspBeautify = new Button
            {
                Text = isVi ? "✨ Làm đẹp bố cục (Auto-Beautify)" : "✨ Beautify Layout (Auto-Beautify)",
                Dock = DockStyle.Top,
                Height = 30,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnInspBeautify.FlatAppearance.BorderSize = 0;
            btnInspBeautify.Click += OnBeautifyIds;
            pnlBeautifyCard.Controls.Add(btnInspBeautify);

            if (_backupTransformsMap.Count > 0 || _lastBeautifyReport.Count > 0)
            {
                var btnInspUndo = new Button
                {
                    Text = isVi ? "↺ Hoàn tác (Khôi phục gốc)" : "↺ Undo (Revert to Original)",
                    Dock = DockStyle.Top,
                    Height = 26,
                    FlatStyle = FlatStyle.System,
                    Margin = new Padding(0, 4, 0, 2)
                };
                btnInspUndo.Click += OnUndoBeautify;
                pnlBeautifyCard.Controls.Add(btnInspUndo);
            }

            if (_lastBeautifyReport.Count > 0)
            {
                var pnlReport = new Panel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Padding = new Padding(6),
                    Margin = new Padding(0, 4, 0, 4),
                    BackColor = Color.FromArgb(245, 243, 255),
                    BorderStyle = BorderStyle.FixedSingle
                };

                var lblReportTitle = new Label
                {
                    Text = isVi ? "📋 Báo cáo Tinh chỉnh Quang học:" : "📋 Optical Refinement Report:",
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(109, 40, 217)
                };

                var txtReportContent = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Text = string.Join("\r\n", _lastBeautifyReport),
                    Dock = DockStyle.Top,
                    Height = Math.Min(260, Math.Max(70, _lastBeautifyReport.Count * 22 + 12)),
                    BackColor = Color.FromArgb(245, 243, 255),
                    ForeColor = Color.FromArgb(76, 29, 149),
                    BorderStyle = BorderStyle.None,
                    Font = new Font("Segoe UI", 8.5F)
                };

                pnlReport.Controls.Add(txtReportContent);
                pnlReport.Controls.Add(lblReportTitle);
                pnlBeautifyCard.Controls.Add(pnlReport);
            }

            var sepBeautify = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = Color.FromArgb(226, 232, 240),
                Margin = new Padding(0, 6, 0, 6)
            };
            pnlBeautifyCard.Controls.Add(sepBeautify);

            rows.Add(pnlBeautifyCard);

            if (sel.IsOperator)
            {
                _trackOffsetX = null;
                _lblOffsetX = null;
                _trackOffsetY = null;
                _lblOffsetY = null;

                // Operator: Split Ratio Slider
                var pnlSplit = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(0, 4, 0, 0) };
                float curRatio = t.SplitRatio ?? 0.5f;

                var lblSplit = new Label
                {
                    Text = string.Format(isVi ? "Tỷ lệ phân chia khung: {0}% : {1}%" : "Frame Split Ratio: {0}% : {1}%",
                        Math.Round(curRatio * 100), 100 - Math.Round(curRatio * 100)),
                    Dock = DockStyle.Top,
                    Height = 20,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(30, 41, 59)
                };

                var track = new SmoothTrackBar
                {
                    Dock = DockStyle.Top,
                    Minimum = 15,
                    Maximum = 85,
                    Value = (int)Math.Round(curRatio * 100),
                    TickFrequency = 10,
                    Height = 32
                };
                track.ValueChanged += (s, e) =>
                {
                    t.SplitRatio = track.Value / 100f;
                    _transformsMap[sel.Id] = t;
                    lblSplit.Text = string.Format(isVi ? "Tỷ lệ phân chia khung: {0}% : {1}%" : "Frame Split Ratio: {0}% : {1}%",
                        track.Value, 100 - track.Value);
                    ApplyTransformAndRedraw();
                };

                _trackSplitRatio = track;
                _lblSplitRatio = lblSplit;

                pnlSplit.Controls.Add(track);
                pnlSplit.Controls.Add(lblSplit);
                rows.Add(pnlSplit);
            }
            else
            {
                _trackSplitRatio = null;
                _lblSplitRatio = null;

                // Leaf Node Controls
                // 1. Offset X & Offset Y
                rows.Add(CreateSliderGroup(
                    isVi ? "Dịch trục X (Ngang, px):" : "Offset X (px):",
                    -400, 400, t.OffsetX,
                    val => { t.OffsetX = val; _transformsMap[sel.Id] = t; ApplyTransformAndRedraw(); },
                    out _trackOffsetX, out _lblOffsetX));

                rows.Add(CreateSliderGroup(
                    isVi ? "Dịch trục Y (Dọc, px):" : "Offset Y (px):",
                    -400, 400, t.OffsetY,
                    val => { t.OffsetY = val; _transformsMap[sel.Id] = t; ApplyTransformAndRedraw(); },
                    out _trackOffsetY, out _lblOffsetY));

                // 2. Scale X & Scale Y
                rows.Add(CreateFloatSliderGroup(
                    isVi ? "Độ co giãn ngang (Scale X):" : "Scale X:",
                    30, 250, (int)Math.Round(t.ScaleX * 100),
                    val => { t.ScaleX = val / 100f; _transformsMap[sel.Id] = t; ApplyTransformAndRedraw(); },
                    v => string.Format("{0:F2}x", v / 100f)));

                rows.Add(CreateFloatSliderGroup(
                    isVi ? "Độ co giãn dọc (Scale Y):" : "Scale Y:",
                    30, 250, (int)Math.Round(t.ScaleY * 100),
                    val => { t.ScaleY = val / 100f; _transformsMap[sel.Id] = t; ApplyTransformAndRedraw(); },
                    v => string.Format("{0:F2}x", v / 100f)));

                // 3. Stroke Dilation (Bù độ đậm nét)
                rows.Add(CreateFloatSliderGroup(
                    isVi ? "Bù độ đậm nét (Stroke Dilate):" : "Stroke Dilate:",
                    -50, 150, (int)Math.Round(t.StrokeWeight * 10),
                    val => { t.StrokeWeight = val / 10f; _transformsMap[sel.Id] = t; ApplyTransformAndRedraw(); },
                    v => string.Format("{0:F1} pt", v / 10f)));

                // 4. Rotation (Xoay nét)
                rows.Add(CreateSliderGroup(
                    isVi ? "Xoay nét (Độ °):" : "Rotation (°):",
                    -180, 180, t.Rotation,
                    val => { t.Rotation = val; _transformsMap[sel.Id] = t; ApplyTransformAndRedraw(); }));

                // 5. Slice Mask buttons
                var pnlSlice = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(0, 4, 0, 0) };
                var lblSlice = new Label
                {
                    Text = isVi ? "Cắt lấy một nửa (Slice Mask):" : "Slice Mask (Half Crop):",
                    Dock = DockStyle.Top,
                    Height = 20,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(30, 41, 59)
                };
                var flowSlice = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false
                };

                string[] maskLabels = isVi
                    ? new[] { "Nguyên", "Trái", "Phải", "Trên", "Dưới" }
                    : new[] { "Whole", "Left", "Right", "Top", "Bottom" };
                SliceMaskType[] masks = new[] { SliceMaskType.None, SliceMaskType.LeftHalf, SliceMaskType.RightHalf, SliceMaskType.TopHalf, SliceMaskType.BottomHalf };

                for (int i = 0; i < masks.Length; i++)
                {
                    int mIdx = i;
                    bool isSelected = (t.SliceMask == masks[i]);
                    var btnMask = new Button
                    {
                        Text = maskLabels[i],
                        Width = 54,
                        Height = 26,
                        Margin = new Padding(1),
                        FlatStyle = isSelected ? FlatStyle.Flat : FlatStyle.System,
                        BackColor = isSelected ? Color.FromArgb(219, 234, 254) : SystemColors.Control,
                        ForeColor = isSelected ? Color.FromArgb(29, 78, 216) : Color.FromArgb(30, 41, 59),
                        Font = new Font("Segoe UI", 8F, isSelected ? FontStyle.Bold : FontStyle.Regular)
                    };
                    if (isSelected)
                    {
                        btnMask.FlatAppearance.BorderColor = Color.FromArgb(59, 130, 246);
                    }
                    btnMask.Click += (s, e) =>
                    {
                        t.SliceMask = masks[mIdx];
                        _transformsMap[sel.Id] = t;
                        ApplyTransformAndRedraw();
                        RebuildInspector();
                    };
                    flowSlice.Controls.Add(btnMask);
                }

                pnlSlice.Controls.Add(flowSlice);
                pnlSlice.Controls.Add(lblSlice);
                rows.Add(pnlSlice);

                // 6. Aspect Guard Checkbox
                var pnlGuard = new Panel { Dock = DockStyle.Top, Height = 35 };
                var chkGuard = new CheckBox
                {
                    Text = isVi ? "Bảo vệ tỷ lệ nét mỏng (Aspect Guard)" : "Protect Aspect Ratio (Aspect Guard)",
                    Checked = !t.DisableAspectGuard,
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    ForeColor = Color.FromArgb(30, 41, 59)
                };
                chkGuard.CheckedChanged += (s, e) =>
                {
                    t.DisableAspectGuard = !chkGuard.Checked;
                    _transformsMap[sel.Id] = t;
                    ApplyTransformAndRedraw();
                };
                pnlGuard.Controls.Add(chkGuard);
                rows.Add(pnlGuard);
            }

            // Add all rows in top-down order
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                _pnlInspector.Controls.Add(rows[i]);
            }

            _pnlInspector.ResumeLayout();
        }

        private void BuildMultiSelectionInspector()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            var rows = new List<Control>();

            // Collect component characters
            var charList = new List<string>();
            foreach (var id in _selectedNodeIds)
            {
                var n = FindNodeById(_ast, id);
                if (n != null && !n.IsOperator && !string.IsNullOrEmpty(n.Char))
                {
                    charList.Add(n.Char);
                }
            }
            string charsSummary = charList.Count > 0 ? string.Join(", ", charList.ToArray()) : "";

            // 1. Header Box
            var pnlHead = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(0, 2, 0, 4),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var lblTitle = new Label
            {
                Text = isVi
                    ? string.Format("Nhóm ({0}): {1}", _selectedNodeIds.Count, charsSummary)
                    : string.Format("Group ({0}): {1}", _selectedNodeIds.Count, charsSummary),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Dock = DockStyle.Left,
                AutoSize = true,
                Padding = new Padding(0, 4, 0, 0),
                ForeColor = Color.FromArgb(14, 165, 233)
            };

            var btnResetGroup = new Button
            {
                Text = isVi ? "↺ Đặt lại nhóm" : "↺ Reset Group",
                Width = 95,
                Height = 26,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.System
            };
            btnResetGroup.Click += (s, e) =>
            {
                PushUndo("Đặt lại nhóm linh kiện");
                foreach (var id in _selectedNodeIds)
                {
                    _transformsMap.Remove(id);
                    RemoveComponentCustomPath(id);
                }
                RecomputeAll();
                RebuildInspector();
            };

            pnlHead.Controls.Add(lblTitle);
            pnlHead.Controls.Add(btnResetGroup);

            var borderHead = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(226, 232, 240)
            };
            pnlHead.Controls.Add(borderHead);
            rows.Add(pnlHead);

            // 2. Group Shared Sliders (Scale X, Scale Y, Stroke Dilate, Rotation)
            rows.Add(CreateFloatSliderGroup(
                isVi ? "Tỉ lệ ngang chung (Scale X):" : "Group Scale X:",
                10, 300, 100,
                val =>
                {
                    float factor = val / 100f;
                    foreach (var id in _selectedNodeIds)
                    {
                        var n = FindNodeById(_ast, id);
                        if (n != null)
                        {
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.ScaleX = (float)Math.Round(factor, 2);
                            n.Transform = t;
                            _transformsMap[id] = t;
                        }
                    }
                    ApplyTransformAndRedraw();
                },
                v => v + "%"));

            rows.Add(CreateFloatSliderGroup(
                isVi ? "Tỉ lệ dọc chung (Scale Y):" : "Group Scale Y:",
                10, 300, 100,
                val =>
                {
                    float factor = val / 100f;
                    foreach (var id in _selectedNodeIds)
                    {
                        var n = FindNodeById(_ast, id);
                        if (n != null)
                        {
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.ScaleY = (float)Math.Round(factor, 2);
                            n.Transform = t;
                            _transformsMap[id] = t;
                        }
                    }
                    ApplyTransformAndRedraw();
                },
                v => v + "%"));

            rows.Add(CreateFloatSliderGroup(
                isVi ? "Bù nét chung (Stroke Dilate):" : "Group Stroke Dilate:",
                -50, 50, 0,
                val =>
                {
                    float dilate = val / 10f;
                    foreach (var id in _selectedNodeIds)
                    {
                        var n = FindNodeById(_ast, id);
                        if (n != null)
                        {
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.StrokeWeight = dilate;
                            n.Transform = t;
                            _transformsMap[id] = t;
                        }
                    }
                    ApplyTransformAndRedraw();
                },
                v => (v / 10f).ToString("+0.0;-0.0;0") + " px"));

            rows.Add(CreateSliderGroup(
                isVi ? "Xoay chung (Rotate):" : "Group Rotate:",
                -180, 180, 0,
                val =>
                {
                    foreach (var id in _selectedNodeIds)
                    {
                        var n = FindNodeById(_ast, id);
                        if (n != null)
                        {
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.Rotation = val;
                            n.Transform = t;
                            _transformsMap[id] = t;
                        }
                    }
                    ApplyTransformAndRedraw();
                }));

            // 3. Alignment Panel (Căn chỉnh nhóm)
            var pnlAlign = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(0, 4, 0, 0) };
            var lblAlign = new Label
            {
                Text = isVi ? "Căn chỉnh vị trí nhóm (Alignment):" : "Group Alignment:",
                Dock = DockStyle.Top,
                Height = 18,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            var flowAlign = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };

            Action<string, Action> addAlignBtn = (txt, action) =>
            {
                var b = new Button
                {
                    Text = txt,
                    Width = 52,
                    Height = 24,
                    Margin = new Padding(1),
                    FlatStyle = FlatStyle.System,
                    Font = new Font("Segoe UI", 8F)
                };
                b.Click += (s, e) => action();
                flowAlign.Controls.Add(b);
            };

            addAlignBtn(isVi ? "⇤ Trái" : "⇤ Left", () =>
            {
                float minLeft = float.MaxValue;
                foreach (var id in _selectedNodeIds)
                {
                    var it = _renderItems.Find(x => x.NodeId == id);
                    if (it != null) minLeft = Math.Min(minLeft, it.StageBounds.Left);
                }
                if (minLeft < float.MaxValue)
                {
                    PushUndo("Canh trái nhóm");
                    foreach (var id in _selectedNodeIds)
                    {
                        var it = _renderItems.Find(x => x.NodeId == id);
                        var n = FindNodeById(_ast, id);
                        if (it != null && n != null)
                        {
                            float delta = minLeft - it.StageBounds.Left;
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.OffsetX = (int)Math.Max(-400, Math.Min(400, Math.Round(t.OffsetX + delta)));
                            n.Transform = t;
                            _transformsMap[id] = t;
                        }
                    }
                    ApplyTransformAndRedraw();
                }
            });

            addAlignBtn(isVi ? "⇹ Giữa X" : "⇹ Center X", () =>
            {
                float totalX = 0f; int count = 0;
                foreach (var id in _selectedNodeIds)
                {
                    var it = _renderItems.Find(x => x.NodeId == id);
                    if (it != null) { totalX += (it.StageBounds.Left + it.StageBounds.Right) / 2f; count++; }
                }
                if (count > 0)
                {
                    float avgX = totalX / count;
                    PushUndo("Canh giữa ngang nhóm");
                    foreach (var id in _selectedNodeIds)
                    {
                        var it = _renderItems.Find(x => x.NodeId == id);
                        var n = FindNodeById(_ast, id);
                        if (it != null && n != null)
                        {
                            float itCenterX = (it.StageBounds.Left + it.StageBounds.Right) / 2f;
                            float delta = avgX - itCenterX;
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.OffsetX = (int)Math.Max(-400, Math.Min(400, Math.Round(t.OffsetX + delta)));
                            n.Transform = t;
                            _transformsMap[id] = t;
                        }
                    }
                    ApplyTransformAndRedraw();
                }
            });

            addAlignBtn(isVi ? "⇥ Phải" : "⇥ Right", () =>
            {
                float maxRight = float.MinValue;
                foreach (var id in _selectedNodeIds)
                {
                    var it = _renderItems.Find(x => x.NodeId == id);
                    if (it != null) maxRight = Math.Max(maxRight, it.StageBounds.Right);
                }
                if (maxRight > float.MinValue)
                {
                    PushUndo("Canh phải nhóm");
                    foreach (var id in _selectedNodeIds)
                    {
                        var it = _renderItems.Find(x => x.NodeId == id);
                        var n = FindNodeById(_ast, id);
                        if (it != null && n != null)
                        {
                            float delta = maxRight - it.StageBounds.Right;
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.OffsetX = (int)Math.Max(-400, Math.Min(400, Math.Round(t.OffsetX + delta)));
                            n.Transform = t;
                            _transformsMap[id] = t;
                        }
                    }
                    ApplyTransformAndRedraw();
                }
            });

            addAlignBtn(isVi ? "⤒ Trên" : "⤒ Top", () =>
            {
                float minTop = float.MaxValue;
                foreach (var id in _selectedNodeIds)
                {
                    var it = _renderItems.Find(x => x.NodeId == id);
                    if (it != null) minTop = Math.Min(minTop, it.StageBounds.Top);
                }
                if (minTop < float.MaxValue)
                {
                    PushUndo("Canh trên nhóm");
                    foreach (var id in _selectedNodeIds)
                    {
                        var it = _renderItems.Find(x => x.NodeId == id);
                        var n = FindNodeById(_ast, id);
                        if (it != null && n != null)
                        {
                            float delta = minTop - it.StageBounds.Top;
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.OffsetY = (int)Math.Max(-400, Math.Min(400, Math.Round(t.OffsetY + delta)));
                            n.Transform = t;
                            _transformsMap[id] = t;
                        }
                    }
                    ApplyTransformAndRedraw();
                }
            });

            addAlignBtn(isVi ? "⇳ Giữa Y" : "⇳ Center Y", () =>
            {
                float totalY = 0f; int count = 0;
                foreach (var id in _selectedNodeIds)
                {
                    var it = _renderItems.Find(x => x.NodeId == id);
                    if (it != null) { totalY += (it.StageBounds.Top + it.StageBounds.Bottom) / 2f; count++; }
                }
                if (count > 0)
                {
                    float avgY = totalY / count;
                    PushUndo("Canh giữa dọc nhóm");
                    foreach (var id in _selectedNodeIds)
                    {
                        var it = _renderItems.Find(x => x.NodeId == id);
                        var n = FindNodeById(_ast, id);
                        if (it != null && n != null)
                        {
                            float itCenterY = (it.StageBounds.Top + it.StageBounds.Bottom) / 2f;
                            float delta = avgY - itCenterY;
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.OffsetY = (int)Math.Max(-400, Math.Min(400, Math.Round(t.OffsetY + delta)));
                            n.Transform = t;
                            _transformsMap[id] = t;
                        }
                    }
                    ApplyTransformAndRedraw();
                }
            });

            addAlignBtn(isVi ? "⤓ Dưới" : "⤓ Bottom", () =>
            {
                float maxBottom = float.MinValue;
                foreach (var id in _selectedNodeIds)
                {
                    var it = _renderItems.Find(x => x.NodeId == id);
                    if (it != null) maxBottom = Math.Max(maxBottom, it.StageBounds.Bottom);
                }
                if (maxBottom > float.MinValue)
                {
                    PushUndo("Canh dưới nhóm");
                    foreach (var id in _selectedNodeIds)
                    {
                        var it = _renderItems.Find(x => x.NodeId == id);
                        var n = FindNodeById(_ast, id);
                        if (it != null && n != null)
                        {
                            float delta = maxBottom - it.StageBounds.Bottom;
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.OffsetY = (int)Math.Max(-400, Math.Min(400, Math.Round(t.OffsetY + delta)));
                            n.Transform = t;
                            _transformsMap[id] = t;
                        }
                    }
                    ApplyTransformAndRedraw();
                }
            });

            addAlignBtn(isVi ? "⬌ Đều X" : "⬌ Dist H", () =>
            {
                var sorted = new List<GlyphRenderData>();
                foreach (var id in _selectedNodeIds)
                {
                    var it = _renderItems.Find(x => x.NodeId == id);
                    if (it != null) sorted.Add(it);
                }
                if (sorted.Count >= 3)
                {
                    sorted.Sort((a, b) => a.StageBounds.Left.CompareTo(b.StageBounds.Left));
                    float spanLeft = sorted[0].StageBounds.Left;
                    float spanRight = sorted[sorted.Count - 1].StageBounds.Right;
                    float totalWidths = 0f;
                    foreach (var item in sorted) totalWidths += item.StageBounds.Width;
                    float gap = (spanRight - spanLeft - totalWidths) / (sorted.Count - 1);

                    PushUndo("Phân bố ngang đều nhóm");
                    float curLeft = spanLeft;
                    foreach (var item in sorted)
                    {
                        var n = FindNodeById(_ast, item.NodeId);
                        if (n != null)
                        {
                            float delta = curLeft - item.StageBounds.Left;
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.OffsetX = (int)Math.Max(-400, Math.Min(400, Math.Round(t.OffsetX + delta)));
                            n.Transform = t;
                            _transformsMap[item.NodeId] = t;
                        }
                        curLeft += item.StageBounds.Width + gap;
                    }
                    ApplyTransformAndRedraw();
                }
            });

            addAlignBtn(isVi ? "⬍ Đều Y" : "⬍ Dist V", () =>
            {
                var sorted = new List<GlyphRenderData>();
                foreach (var id in _selectedNodeIds)
                {
                    var it = _renderItems.Find(x => x.NodeId == id);
                    if (it != null) sorted.Add(it);
                }
                if (sorted.Count >= 3)
                {
                    sorted.Sort((a, b) => a.StageBounds.Top.CompareTo(b.StageBounds.Top));
                    float spanTop = sorted[0].StageBounds.Top;
                    float spanBottom = sorted[sorted.Count - 1].StageBounds.Bottom;
                    float totalHeights = 0f;
                    foreach (var item in sorted) totalHeights += item.StageBounds.Height;
                    float gap = (spanBottom - spanTop - totalHeights) / (sorted.Count - 1);

                    PushUndo("Phân bố dọc đều nhóm");
                    float curTop = spanTop;
                    foreach (var item in sorted)
                    {
                        var n = FindNodeById(_ast, item.NodeId);
                        if (n != null)
                        {
                            float delta = curTop - item.StageBounds.Top;
                            var t = n.Transform ?? new NodeCustomTransform();
                            t.OffsetY = (int)Math.Max(-400, Math.Min(400, Math.Round(t.OffsetY + delta)));
                            n.Transform = t;
                            _transformsMap[item.NodeId] = t;
                        }
                        curTop += item.StageBounds.Height + gap;
                    }
                    ApplyTransformAndRedraw();
                }
            });

            pnlAlign.Controls.Add(flowAlign);
            pnlAlign.Controls.Add(lblAlign);
            rows.Add(pnlAlign);

            // 4. Batch Actions Panel
            var pnlBatch = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 4, 0, 0) };
            var flowBatch = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            Action<string, Action> addBatchBtn = (txt, act) =>
            {
                var b = new Button
                {
                    Text = txt,
                    Width = 52,
                    Height = 26,
                    Margin = new Padding(1),
                    FlatStyle = FlatStyle.System,
                    Font = new Font("Segoe UI", 8F)
                };
                b.Click += (s, e) => act();
                flowBatch.Controls.Add(b);
            };

            addBatchBtn(isVi ? "⇄ Lật X" : "⇄ Flip X", () =>
            {
                PushUndo("Lật ngang nhóm linh kiện");
                foreach (var id in _selectedNodeIds)
                {
                    var n = FindNodeById(_ast, id);
                    if (n != null)
                    {
                        var t = n.Transform ?? new NodeCustomTransform();
                        t.ScaleX = -t.ScaleX;
                        n.Transform = t;
                        _transformsMap[id] = t;
                    }
                }
                ApplyTransformAndRedraw();
            });

            addBatchBtn(isVi ? "⇅ Lật Y" : "⇅ Flip Y", () =>
            {
                PushUndo("Lật dọc nhóm linh kiện");
                foreach (var id in _selectedNodeIds)
                {
                    var n = FindNodeById(_ast, id);
                    if (n != null)
                    {
                        var t = n.Transform ?? new NodeCustomTransform();
                        t.ScaleY = -t.ScaleY;
                        n.Transform = t;
                        _transformsMap[id] = t;
                    }
                }
                ApplyTransformAndRedraw();
            });

            addBatchBtn(isVi ? "🔒 Khóa" : "🔒 Lock", () =>
            {
                PushUndo("Khóa/Mở khóa nhóm");
                bool anyUnlocked = false;
                foreach (var id in _selectedNodeIds)
                {
                    var n = FindNodeById(_ast, id);
                    if (n != null && (n.Transform == null || !n.Transform.IsLocked)) { anyUnlocked = true; break; }
                }
                foreach (var id in _selectedNodeIds)
                {
                    var n = FindNodeById(_ast, id);
                    if (n != null)
                    {
                        var t = n.Transform ?? new NodeCustomTransform();
                        t.IsLocked = anyUnlocked;
                        n.Transform = t;
                        _transformsMap[id] = t;
                    }
                }
                ApplyTransformAndRedraw();
                RebuildLayersList();
            });

            addBatchBtn(isVi ? "👁️ Ẩn" : "👁️ Hide", () =>
            {
                PushUndo("Ẩn/Hiện nhóm");
                bool anyVis = false;
                foreach (var id in _selectedNodeIds)
                {
                    var n = FindNodeById(_ast, id);
                    if (n != null && (n.Transform == null || n.Transform.IsVisible)) { anyVis = true; break; }
                }
                foreach (var id in _selectedNodeIds)
                {
                    var n = FindNodeById(_ast, id);
                    if (n != null)
                    {
                        var t = n.Transform ?? new NodeCustomTransform();
                        t.IsVisible = !anyVis;
                        n.Transform = t;
                        _transformsMap[id] = t;
                    }
                }
                ApplyTransformAndRedraw();
                RebuildLayersList();
            });

            addBatchBtn(isVi ? "🗑️ Xóa" : "🗑️ Delete", () =>
            {
                ContextDelete();
            });

            pnlBatch.Controls.Add(flowBatch);
            rows.Add(pnlBatch);

            // Add all rows in top-down order
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                _pnlInspector.Controls.Add(rows[i]);
            }
        }

        private Panel CreateSliderGroup(string title, int min, int max, int curVal, Action<int> onValChanged, out SmoothTrackBar outTrack, out Label outLbl)
        {
            var pnl = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(0, 2, 0, 2) };
            var lbl = new Label
            {
                Text = string.Format("{0} {1}", title, curVal),
                Dock = DockStyle.Top,
                Height = 18,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            var track = new SmoothTrackBar
            {
                Dock = DockStyle.Top,
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, curVal)),
                TickFrequency = Math.Max(1, (max - min) / 8),
                Height = 32
            };
            track.ValueChanged += (s, e) =>
            {
                lbl.Text = string.Format("{0} {1}", title, track.Value);
                onValChanged(track.Value);
            };

            pnl.Controls.Add(track);
            pnl.Controls.Add(lbl);

            outTrack = track;
            outLbl = lbl;
            return pnl;
        }

        private Panel CreateSliderGroup(string title, int min, int max, int curVal, Action<int> onValChanged)
        {
            SmoothTrackBar dummyTrack;
            Label dummyLbl;
            return CreateSliderGroup(title, min, max, curVal, onValChanged, out dummyTrack, out dummyLbl);
        }

        private Panel CreateFloatSliderGroup(string title, int min, int max, int curVal, Action<int> onValChanged, Func<int, string> formatVal)
        {
            var pnl = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(0, 2, 0, 2) };
            var lbl = new Label
            {
                Text = string.Format("{0} {1}", title, formatVal(curVal)),
                Dock = DockStyle.Top,
                Height = 18,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            var track = new SmoothTrackBar
            {
                Dock = DockStyle.Top,
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, curVal)),
                TickFrequency = Math.Max(1, (max - min) / 6),
                Height = 32
            };
            track.ValueChanged += (s, e) =>
            {
                lbl.Text = string.Format("{0} {1}", title, formatVal(track.Value));
                onValChanged(track.Value);
            };

            pnl.Controls.Add(track);
            pnl.Controls.Add(lbl);
            return pnl;
        }

        private void UpdateLegibilityStrip()
        {
            if (_pnlLegibility == null) return;
            _pnlLegibility.SuspendLayout();
            foreach (Control ctrl in _pnlLegibility.Controls)
            {
                var pnl = ctrl as Panel;
                if (pnl != null)
                {
                    foreach (Control child in pnl.Controls)
                    {
                        var pb = child as PictureBox;
                        if (pb != null && pb.Image != null)
                        {
                            try { pb.Image.Dispose(); } catch { }
                            pb.Image = null;
                        }
                    }
                }
            }
            _pnlLegibility.Controls.Clear();

            // Re-add top border
            var legBorder = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = Color.FromArgb(226, 232, 240)
            };
            _pnlLegibility.Controls.Add(legBorder);

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            var themeCfg = IdsCanvasThemeConfig.GetTheme(_colorTheme);

            int[] sizes = new[] { 16, 24, 32, 48, 64 };

            var lblStripTitle = new Label
            {
                Text = isVi ? "Kiểm thử độ rõ nét đa cỡ (Legibility Strip):" : "Legibility Strip (Multi-scale preview):",
                AutoSize = true,
                Location = new Point(12, 6),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105)
            };
            _pnlLegibility.Controls.Add(lblStripTitle);

            int left = 12;
            foreach (int sz in sizes)
            {
                int colW = Math.Max(sz + 10, 48);
                var colPnl = new Panel
                {
                    Location = new Point(left, 24),
                    Size = new Size(colW, 80),
                    BackColor = Color.Transparent
                };

                var bmp = IdsGlyphRenderer.RenderHighResBitmap(_renderItems, _activeFontFamily, sz, themeCfg.InkColor, themeCfg.BgColor, _customStrokes);
                var pb = new PictureBox
                {
                    Image = bmp,
                    Size = new Size(sz, sz),
                    Location = new Point((colW - sz) / 2, (64 - sz) / 2),
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = themeCfg.BgColor
                };

                var lblSz = new Label
                {
                    Text = sz + "px",
                    Size = new Size(colW, 14),
                    Location = new Point(0, 66),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Regular),
                    ForeColor = Color.FromArgb(100, 116, 139)
                };

                colPnl.Controls.Add(pb);
                colPnl.Controls.Add(lblSz);
                _pnlLegibility.Controls.Add(colPnl);

                left += colW + 12;
            }

            _pnlLegibility.ResumeLayout();
        }

        private void OnBeautifyIds(object sender, EventArgs e)
        {
            PushUndo("Làm đẹp bố cục");
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            // Make sure AST is parsed from current input
            if (_ast == null)
            {
                _ast = IdsParserService.ParseIds(_idsString, _smartRadicalsEnabled, _transformsMap);
            }

            // Disable button during background solver execution to prevent concurrent re-entrancy
            if (_btnBeautify != null) _btnBeautify.Enabled = false;

            string ids = _idsString;
            IdsNode ast = _ast;
            FontFamily font = _activeFontFamily;
            var currentTransforms = new Dictionary<string, NodeCustomTransform>();
            foreach (var kvp in _transformsMap)
            {
                currentTransforms[kvp.Key] = kvp.Value.Clone();
            }

            _lastBeautifyReport.Clear();
            _lastBeautifyReport.Add(isVi
                ? "⏳ Khởi chạy Engine Tìm Kiếm Chuyên Sâu, Đối Chiếu & Giải Phẫu Thực Nghiệm..."
                : "⏳ Launching Deep Exhaustive Search & A-Z Cross-Reference Engine...");
            RebuildInspector();

            Action<string> progressReport = statusMsg =>
            {
                if (this.IsDisposed || !this.IsHandleCreated) return;
                try
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        if (_lblFontStatus != null) _lblFontStatus.Text = statusMsg;
                        _lastBeautifyReport.Add(statusMsg);
                        if (_lastBeautifyReport.Count > 14)
                        {
                            _lastBeautifyReport.RemoveAt(0);
                        }
                        RebuildInspector();
                    });
                }
                catch { }
            };

            System.Threading.Tasks.Task.Run(() =>
            {
                return IdsOpticalBeautifier.BeautifyThisIds(ids, ast, font, currentTransforms, isVi, null, progressReport);
            }).ContinueWith(task =>
            {
                if (this.IsDisposed || !this.IsHandleCreated) return;
                this.BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        if (_lblFontStatus != null && _activeFontFamily != null)
                        {
                            _lblFontStatus.Text = (isVi ? "Đang dùng font: " : "Active font: ") + _activeFontFamily.Name;
                        }

                        if (task.IsFaulted || task.Result == null || !task.Result.Success)
                        {
                            string err = (task.IsFaulted && task.Exception != null)
                                ? (task.Exception.InnerException != null ? task.Exception.InnerException.Message : task.Exception.Message)
                                : (task.Result != null ? task.Result.ErrorMessage : "Unknown error");

                            MessageBox.Show(
                                this,
                                err,
                                isVi ? "Cảnh báo kiểm định bố cục IDS" : "IDS Grammar Validation",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                            return;
                        }

                        var result = task.Result;
                        _lastBeautifyReport = result.ReportBulletPoints;

                        // Idempotent lock: If already optimal, do not push to undo stack
                        if (result.IsAlreadyOptimal)
                        {
                            RecomputeAll();
                            RebuildInspector();
                            return;
                        }

                        // Save backup for undo
                        _backupTransformsMap.Clear();
                        foreach (var kvp in _transformsMap)
                        {
                            _backupTransformsMap[kvp.Key] = kvp.Value.Clone();
                        }

                        _transformsMap = result.BeautifiedTransforms;
                        if (_btnUndoBeautify != null) _btnUndoBeautify.Visible = true;

                        RecomputeAll();
                        RebuildInspector();
                    }
                    finally
                    {
                        if (_btnBeautify != null) _btnBeautify.Enabled = true;
                    }
                });
            });
        }

        private void OnUndoBeautify(object sender, EventArgs e)
        {
            if (_backupTransformsMap.Count > 0)
            {
                _transformsMap = new Dictionary<string, NodeCustomTransform>();
                foreach (var kvp in _backupTransformsMap)
                {
                    _transformsMap[kvp.Key] = kvp.Value.Clone();
                }
            }
            else
            {
                _transformsMap.Clear();
            }

            _lastBeautifyReport.Clear();
            if (_btnUndoBeautify != null) _btnUndoBeautify.Visible = false;

            RecomputeAll();
        }

        private void OnOpenPresets(object s, EventArgs e)
        {
            using (var dlg = new IdsPresetGalleryDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedPreset != null)
                {
                    bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                    _currentPreset = dlg.SelectedPreset;
                    _idsString = dlg.SelectedPreset.Ids;
                    _currentPresetName = dlg.SelectedPreset.GetDisplayName(isVi);
                    _lblPresetName.Text = "★ " + _currentPresetName;
                    _txtIdsInput.Text = _idsString;
                    _transformsMap.Clear();
                    _selectedNodeId = null;
                    RecomputeAll();
                }
            }
        }

        private void OnOpenSaveDialog(object s, EventArgs e)
        {
            using (var dlg = new IdsDictionaryDialog(_idsString, _ast, _renderItems, _activeFontFamily, _colorTheme, true))
            {
                dlg.ShowDialog(this);
            }
        }

        private void OnOpenDictDialog(object s, EventArgs e)
        {
            using (var dlg = new IdsDictionaryDialog(_idsString, _ast, _renderItems, _activeFontFamily, _colorTheme, false))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.LoadedGlyph != null)
                {
                    _currentPreset = null;
                    _idsString = dlg.LoadedGlyph.IdsString;
                    _currentPresetName = dlg.LoadedGlyph.Name;
                    _lblPresetName.Text = "★ " + _currentPresetName;
                    _txtIdsInput.Text = _idsString;
                    _transformsMap.Clear();
                    _selectedNodeId = null;
                    RecomputeAll();
                }
            }
        }

        private void OnOpenExportDialog(object s, EventArgs e)
        {
            using (var dlg = new IdsExportDialog(_renderItems, _activeFontFamily, _colorTheme))
            {
                dlg.ShowDialog(this);
            }
        }

        private void OnOpenFontEditorDialog(object s, EventArgs e)
        {
            if (_renderItems == null || _renderItems.Count == 0)
            {
                bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                MessageBox.Show(
                    isVi ? "Vui lòng nhập công thức IDS hoặc chọn một mẫu chữ trước khi lưu vào Font!" : "Please compose an IDS glyph before saving to font!",
                    isVi ? "Chưa có ký tự" : "No glyph",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (var combinedPath = IdsGlyphRenderer.BuildCombinedVectorPath(_renderItems, _activeFontFamily))
            {
                string targetChar = "";
                if (_currentPreset != null && !string.IsNullOrEmpty(_currentPreset.ResultChar))
                {
                    targetChar = _currentPreset.ResultChar;
                }

                using (var dlg = new FontEditorDialog(
                    combinedPath,
                    new RectangleF(0, 0, 1000, 1000),
                    _idsString,
                    targetChar,
                    _activeFontFamily))
                {
                    dlg.ShowDialog(this);
                }
            }
        }

        // Inner custom double-buffered canvas surface
        private class CanvasSurface : Control
        {
            private readonly IdsComposerControl _parent;

            // Pan state
            private bool _isPanning = false;
            private Point _panStartMouse;
            private float _panStartOriginX;
            private float _panStartOriginY;

            // Direct manipulation state
            private bool _isManipulating = false;
            private BoundingHandle _activeHandle = BoundingHandle.None;
            private PointF _dragStartMouseStage;
            private NodeCustomTransform _startTransform;
            private RectangleF _startStageBounds;
            private PointF _startCenter;
            private float _startAngleDeg;
            private string _hudMessage = null;
            private GraphicsPath _dragStartCustomVectorPath = null;

            // Multi-selection transformation state
            private readonly Dictionary<string, NodeCustomTransform> _startMultiTransforms = new Dictionary<string, NodeCustomTransform>();
            private readonly Dictionary<string, PointF> _startMultiCenters = new Dictionary<string, PointF>();
            private readonly Dictionary<string, RectangleF> _startMultiBounds = new Dictionary<string, RectangleF>();
            private readonly Dictionary<string, GraphicsPath> _startMultiCustomPaths = new Dictionary<string, GraphicsPath>();
            private RectangleF _startGroupBounds = RectangleF.Empty;
            private PointF _startGroupCenter = PointF.Empty;

            // Direct & Lasso selection state (Vector Anchor Points)
            private bool _isDraggingAnchor = false;
            private int _activeAnchorIndex = -1;
            private PointF _anchorDragStartStage;
            private readonly HashSet<int> _selectedAnchorIndices = new HashSet<int>();

            // Selection Transformer (Photoshop / Illustrator Style)
            private bool _isTransformingSelection = false;
            private BoundingHandle _activeSelectionHandle = BoundingHandle.None;
            private PointF _selectionDragStartStage = PointF.Empty;
            private Point _selectionDragStartMouse = Point.Empty;
            private RectangleF _selectionStartBounds = RectangleF.Empty;
            private PointF _selectionStartCenter = PointF.Empty;
            private float _selectionStartAngleDeg = 0f;
            private float _selectionTransformRotation = 0f;
            private readonly Dictionary<int, PointF> _startAnchorPoints = new Dictionary<int, PointF>();
            private readonly Dictionary<int, PointF[]> _startCustomStrokePoints = new Dictionary<int, PointF[]>();
            private readonly HashSet<int> _selectedCustomStrokeIndices = new HashSet<int>();

            // Selection Marquee Box (Direct Selection Tool A)
            private bool _isBoxSelecting = false;
            private Point _boxSelectStart;
            private Point _boxSelectCurrent;

            // Lasso selection tool state
            private readonly List<PointF> _lassoPoints = new List<PointF>();
            private bool _isLassoing = false;

            // Lasso cut (Knife) tool state
            private readonly List<PointF> _cutPoints = new List<PointF>();
            private bool _isCutting = false;

            // Auto-join pulse animation
            private PointF _pulseCenter = PointF.Empty;
            private float _pulseRadius = 0f;
            private Timer _pulseTimer = null;

            public CanvasSurface(IdsComposerControl parent)
            {
                _parent = parent;
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
                Cursor = Cursors.Default;

                _pulseTimer = new Timer { Interval = 30 };
                _pulseTimer.Tick += (s, e) =>
                {
                    _pulseRadius += 3.5f;
                    if (_pulseRadius > 35f)
                    {
                        _pulseTimer.Stop();
                        _pulseRadius = 0f;
                        _pulseCenter = PointF.Empty;
                    }
                    Invalidate();
                };
            }

            public void ShowAutoJoinPulse(PointF stagePt, string message)
            {
                _pulseCenter = stagePt;
                _pulseRadius = 5f;
                _hudMessage = message;
                if (_pulseTimer != null)
                {
                    _pulseTimer.Stop();
                    _pulseTimer.Start();
                }
                Invalidate();
            }

            public void ShowHudMessage(string message)
            {
                _hudMessage = message;
                Invalidate();
            }

            public void ResetInteractionStates()
            {
                _isPanning = false;
                _isManipulating = false;
                _activeHandle = BoundingHandle.None;
                _isDraggingAnchor = false;
                _isTransformingSelection = false;
                _activeSelectionHandle = BoundingHandle.None;
                _isBoxSelecting = false;
                _isLassoing = false;
                _lassoPoints.Clear();
                _isCutting = false;
                _cutPoints.Clear();
                _hudMessage = null;
                try { Capture = false; } catch { }
            }

            public void ClearActiveSelections()
            {
                _selectedAnchorIndices.Clear();
                _selectedCustomStrokeIndices.Clear();
                _activeAnchorIndex = -1;
                _parent._selectedAnchorIndex = -1;
                _selectionTransformRotation = 0f;
                _hudMessage = "Đã hủy vùng chọn (Escape)";
                Invalidate();
            }

            protected override void OnLostFocus(EventArgs e)
            {
                base.OnLostFocus(e);
                ResetInteractionStates();
                Invalidate();
            }

            protected override void OnLeave(EventArgs e)
            {
                base.OnLeave(e);
                ResetInteractionStates();
                Invalidate();
            }

            public bool HasSelectedAnchors { get { return _selectedAnchorIndices.Count > 0 || _activeAnchorIndex >= 0; } }
            public HashSet<int> SelectedAnchorIndices { get { return _selectedAnchorIndices; } }
            public HashSet<int> SelectedCustomStrokeIndices { get { return _selectedCustomStrokeIndices; } }

            public void ClearCustomStrokeSelection()
            {
                _selectedCustomStrokeIndices.Clear();
                _selectedAnchorIndices.Clear();
                _activeAnchorIndex = -1;
                _selectionTransformRotation = 0f;
            }

            public int HitTestCustomStrokes(Point mousePt, RectangleF stageRect)
            {
                if (_parent._customStrokes == null || _parent._customStrokes.Count == 0) return -1;
                PointF ptStage = ScreenToStage(mousePt, stageRect);
                float penWidth = 24f;
                using (var pen = new Pen(Color.Black, penWidth))
                {
                    for (int k = _parent._customStrokes.Count - 1; k >= 0; k--)
                    {
                        var path = _parent._customStrokes[k];
                        if (path == null || path.PointCount < 2) continue;
                        RectangleF b = path.GetBounds();
                        b.Inflate(24f, 24f);
                        if (!b.Contains(ptStage)) continue;
                        try
                        {
                            if (path.IsOutlineVisible(ptStage, pen) || path.IsVisible(ptStage))
                            {
                                return k;
                            }
                        }
                        catch { }
                    }
                }
                return -1;
            }

            public RectangleF GetSelectedRegionStageBounds()
            {
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                int count = 0;

                if (_selectedAnchorIndices.Count > 0 && !string.IsNullOrEmpty(_parent._selectedNodeId))
                {
                    var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                    if (item != null && (item.Transform == null || item.Transform.IsVisible))
                    {
                        var path = _parent.EnsureVectorPath(item);
                        if (path != null && path.PointCount > 0)
                        {
                            var pts = path.PathPoints;
                            foreach (int idx in _selectedAnchorIndices)
                            {
                                if (idx >= 0 && idx < pts.Length)
                                {
                                    var p = pts[idx];
                                    if (p.X < minX) minX = p.X;
                                    if (p.Y < minY) minY = p.Y;
                                    if (p.X > maxX) maxX = p.X;
                                    if (p.Y > maxY) maxY = p.Y;
                                    count++;
                                }
                            }
                        }
                    }
                }

                if (_selectedCustomStrokeIndices.Count > 0)
                {
                    foreach (int sIdx in _selectedCustomStrokeIndices)
                    {
                        if (sIdx >= 0 && sIdx < _parent._customStrokes.Count)
                        {
                            var stroke = _parent._customStrokes[sIdx];
                            if (stroke != null && stroke.PointCount > 0)
                            {
                                var pts = stroke.PathPoints;
                                foreach (var p in pts)
                                {
                                    if (p.X < minX) minX = p.X;
                                    if (p.Y < minY) minY = p.Y;
                                    if (p.X > maxX) maxX = p.X;
                                    if (p.Y > maxY) maxY = p.Y;
                                    count++;
                                }
                            }
                        }
                    }
                }

                if (count == 0) return RectangleF.Empty;

                float w = Math.Max(16f, maxX - minX);
                float h = Math.Max(16f, maxY - minY);
                return new RectangleF(minX - 8f, minY - 8f, w + 16f, h + 16f);
            }

            public bool HasActiveSelectionTransformer
            {
                get
                {
                    if (_parent._toolMode != EditorToolMode.DirectSelect &&
                        _parent._toolMode != EditorToolMode.Lasso &&
                        _parent._toolMode != EditorToolMode.Select)
                    {
                        return false;
                    }
                    return (_selectedAnchorIndices.Count > 0 || _selectedCustomStrokeIndices.Count > 0) &&
                           !GetSelectedRegionStageBounds().IsEmpty;
                }
            }

            private BoundingHandle HitTestSelectionTransformerHandles(Point mousePt, RectangleF stageRect)
            {
                if (!HasActiveSelectionTransformer) return BoundingHandle.None;

                RectangleF stageBox = GetSelectedRegionStageBounds();
                if (stageBox.IsEmpty) return BoundingHandle.None;

                PointF centerStage = new PointF(stageBox.X + stageBox.Width / 2f, stageBox.Y + stageBox.Height / 2f);
                PointF pStage = ScreenToStage(mousePt, stageRect);
                PointF unrot = RotatePoint(pStage, centerStage, -_selectionTransformRotation);

                float scale = stageRect.Width / 1000f;
                float halfW = (stageBox.Width * scale) / 2f;
                float halfH = (stageBox.Height * scale) / 2f;
                float localX = (unrot.X - centerStage.X) * scale;
                float localY = (unrot.Y - centerStage.Y) * scale;
                float stemLenScreen = 24f;

                // 1. Rotation handle check
                float rotDistSq = localX * localX + (localY - (-halfH - stemLenScreen)) * (localY - (-halfH - stemLenScreen));
                if (rotDistSq <= 81f)
                {
                    return BoundingHandle.Rotate;
                }

                // 2. 8 Resize handles
                PointF[] handles = new PointF[]
                {
                    new PointF(-halfW, -halfH),
                    new PointF(0, -halfH),
                    new PointF(halfW, -halfH),
                    new PointF(halfW, 0),
                    new PointF(halfW, halfH),
                    new PointF(0, halfH),
                    new PointF(-halfW, halfH),
                    new PointF(-halfW, 0)
                };

                BoundingHandle[] types = new BoundingHandle[]
                {
                    BoundingHandle.TopLeft,
                    BoundingHandle.TopCenter,
                    BoundingHandle.TopRight,
                    BoundingHandle.MiddleRight,
                    BoundingHandle.BottomRight,
                    BoundingHandle.BottomCenter,
                    BoundingHandle.BottomLeft,
                    BoundingHandle.MiddleLeft
                };

                const float handleHitRadius = 10f;
                for (int i = 0; i < 8; i++)
                {
                    float dX = localX - handles[i].X;
                    float dY = localY - handles[i].Y;
                    if (dX * dX + dY * dY <= handleHitRadius * handleHitRadius)
                    {
                        return types[i];
                    }
                }

                // 3. Body check (inside the box) - only for Select and DirectSelect tools
                // In Lasso tool mode, clicking inside the box should draw a new Lasso instead of moving the body
                if (_parent._toolMode != EditorToolMode.Lasso)
                {
                    if (localX >= -halfW && localX <= halfW && localY >= -halfH && localY <= halfH)
                    {
                        return BoundingHandle.Body;
                    }
                }

                return BoundingHandle.None;
            }

            private void ApplySelectionTranslation(float dx, float dy)
            {
                if (_startAnchorPoints.Count > 0 && !string.IsNullOrEmpty(_parent._selectedNodeId))
                {
                    var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                    if (item != null && item.VectorPath != null)
                    {
                        var pts = item.VectorPath.PathPoints;
                        var types = item.VectorPath.PathTypes;
                        foreach (var kvp in _startAnchorPoints)
                        {
                            if (kvp.Key >= 0 && kvp.Key < pts.Length)
                            {
                                pts[kvp.Key] = new PointF(kvp.Value.X + dx, kvp.Value.Y + dy);
                            }
                        }
                        item.VectorPath = new GraphicsPath(pts, types);
                        _parent.SetComponentCustomPath(item.NodeId, item.VectorPath, item.TargetRect, item.Transform);
                    }
                }

                if (_startCustomStrokePoints.Count > 0)
                {
                    foreach (var kvp in _startCustomStrokePoints)
                    {
                        if (kvp.Key >= 0 && kvp.Key < _parent._customStrokes.Count)
                        {
                            var stroke = _parent._customStrokes[kvp.Key];
                            if (stroke != null)
                            {
                                var sPts = (PointF[])kvp.Value.Clone();
                                for (int p = 0; p < sPts.Length; p++)
                                {
                                    sPts[p] = new PointF(sPts[p].X + dx, sPts[p].Y + dy);
                                }
                                _parent._customStrokes[kvp.Key] = new GraphicsPath(sPts, stroke.PathTypes);
                            }
                        }
                    }
                }
            }

            private void ApplySelectionRotation(float angleDeg, PointF center)
            {
                if (_startAnchorPoints.Count > 0 && !string.IsNullOrEmpty(_parent._selectedNodeId))
                {
                    var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                    if (item != null && item.VectorPath != null)
                    {
                        var pts = item.VectorPath.PathPoints;
                        var types = item.VectorPath.PathTypes;
                        foreach (var kvp in _startAnchorPoints)
                        {
                            if (kvp.Key >= 0 && kvp.Key < pts.Length)
                            {
                                pts[kvp.Key] = RotatePoint(kvp.Value, center, angleDeg);
                            }
                        }
                        item.VectorPath = new GraphicsPath(pts, types);
                _parent._componentCustomPaths[item.NodeId] = (GraphicsPath)item.VectorPath.Clone();
                        _parent._componentCustomPaths[item.NodeId] = (GraphicsPath)item.VectorPath.Clone();
                    }
                }

                if (_startCustomStrokePoints.Count > 0)
                {
                    foreach (var kvp in _startCustomStrokePoints)
                    {
                        if (kvp.Key >= 0 && kvp.Key < _parent._customStrokes.Count)
                        {
                            var stroke = _parent._customStrokes[kvp.Key];
                            if (stroke != null)
                            {
                                var sPts = (PointF[])kvp.Value.Clone();
                                for (int p = 0; p < sPts.Length; p++)
                                {
                                    sPts[p] = RotatePoint(sPts[p], center, angleDeg);
                                }
                                _parent._customStrokes[kvp.Key] = new GraphicsPath(sPts, stroke.PathTypes);
                            }
                        }
                    }
                }
            }

            private void ApplySelectionScale(float sx, float sy, PointF center)
            {
                if (_startAnchorPoints.Count > 0 && !string.IsNullOrEmpty(_parent._selectedNodeId))
                {
                    var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                    if (item != null && item.VectorPath != null)
                    {
                        var pts = item.VectorPath.PathPoints;
                        var types = item.VectorPath.PathTypes;
                        foreach (var kvp in _startAnchorPoints)
                        {
                            if (kvp.Key >= 0 && kvp.Key < pts.Length)
                            {
                                pts[kvp.Key] = new PointF(center.X + (kvp.Value.X - center.X) * sx, center.Y + (kvp.Value.Y - center.Y) * sy);
                            }
                        }
                        item.VectorPath = new GraphicsPath(pts, types);
                _parent._componentCustomPaths[item.NodeId] = (GraphicsPath)item.VectorPath.Clone();
                    }
                }

                if (_startCustomStrokePoints.Count > 0)
                {
                    foreach (var kvp in _startCustomStrokePoints)
                    {
                        if (kvp.Key >= 0 && kvp.Key < _parent._customStrokes.Count)
                        {
                            var stroke = _parent._customStrokes[kvp.Key];
                            if (stroke != null)
                            {
                                var sPts = (PointF[])kvp.Value.Clone();
                                for (int p = 0; p < sPts.Length; p++)
                                {
                                    sPts[p] = new PointF(center.X + (kvp.Value[p].X - center.X) * sx, center.Y + (kvp.Value[p].Y - center.Y) * sy);
                                }
                                _parent._customStrokes[kvp.Key] = new GraphicsPath(sPts, stroke.PathTypes);
                            }
                        }
                    }
                }
            }

            public bool NudgeSelectedAnchors(int dx, int dy)
            {
                if (!HasSelectedAnchors || string.IsNullOrEmpty(_parent._selectedNodeId)) return false;
                var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                if (item == null || (item.Transform != null && !item.Transform.IsVisible)) return false;
                var path = _parent.EnsureVectorPath(item);
                if (path == null) return false;

                _parent.PushUndo("Dịch chuyển điểm neo");
                var pts = path.PathPoints;
                var types = path.PathTypes;

                if (_selectedAnchorIndices.Count > 0)
                {
                    foreach (int idx in _selectedAnchorIndices)
                    {
                        if (idx >= 0 && idx < pts.Length)
                        {
                            pts[idx] = new PointF(pts[idx].X + dx, pts[idx].Y + dy);
                        }
                    }
                }
                else if (_activeAnchorIndex >= 0 && _activeAnchorIndex < pts.Length)
                {
                    pts[_activeAnchorIndex] = new PointF(pts[_activeAnchorIndex].X + dx, pts[_activeAnchorIndex].Y + dy);
                }

                item.VectorPath = new GraphicsPath(pts, types);
                _parent._componentCustomPaths[item.NodeId] = (GraphicsPath)item.VectorPath.Clone();
                _hudMessage = string.Format("Dịch chuyển {0} điểm neo ({1:+0;-0;0}, {2:+0;-0;0})",
                    _selectedAnchorIndices.Count > 0 ? _selectedAnchorIndices.Count : 1, dx, dy);
                Invalidate();
                return true;
            }

            public void ClearAnchorSelection(bool clearSelectedNode = false)
            {
                _selectedAnchorIndices.Clear();
                _selectedCustomStrokeIndices.Clear();
                _activeAnchorIndex = -1;
                _parent._selectedAnchorIndex = -1;
                _selectionTransformRotation = 0f;
                _isTransformingSelection = false;
                _isDraggingAnchor = false;
                _isBoxSelecting = false;
                _isLassoing = false;
                _isCutting = false;
                _lassoPoints.Clear();
                _cutPoints.Clear();
                if (clearSelectedNode)
                {
                    _parent._selectedNodeId = null;
                }
            }

            public void DeleteSelectedAnchors()
            {
                if (!HasSelectedAnchors) return;

                _parent.PushUndo("Xóa điểm neo / nét đã khoanh");
                int count = 0;

                // 1. Xóa các điểm neo được chọn trong linh kiện đang kích hoạt (bảo toàn các nét còn lại)
                if (_selectedAnchorIndices.Count > 0 && !string.IsNullOrEmpty(_parent._selectedNodeId))
                {
                    var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                    if (item != null && (item.Transform == null || item.Transform.IsVisible))
                    {
                        var path = _parent.EnsureVectorPath(item);
                        if (path != null)
                        {
                            var updatedPath = IdsVectorGeometryService.RemoveAnchorPoints(path, _selectedAnchorIndices);
                            if (updatedPath == null || updatedPath.PointCount == 0)
                            {
                                if (updatedPath != null) { try { updatedPath.Dispose(); } catch { } }
                                if (item.VectorPath != null) { try { item.VectorPath.Dispose(); } catch { } }
                                item.VectorPath = null;
                                _parent._componentCustomPaths.Remove(item.NodeId);

                                var node = _parent.FindNodeById(_parent._ast, item.NodeId);
                                if (node != null)
                                {
                                    var t = node.Transform ?? new NodeCustomTransform();
                                    t.IsVisible = false;
                                    node.Transform = t;
                                    _parent._transformsMap[node.Id] = t;
                                }
                                if (item.Transform != null) item.Transform.IsVisible = false;
                                _parent._selectedNodeId = null;
                                ClearAnchorSelection(true);
                                _parent.ApplyTransformAndRedraw();
                                _parent.RebuildInspector();
                                _parent.RebuildLayersList();
                            }
                            else
                            {
                                if (item.VectorPath != null) { try { item.VectorPath.Dispose(); } catch { } }
                                item.VectorPath = updatedPath;
                                _parent._componentCustomPaths[item.NodeId] = (GraphicsPath)updatedPath.Clone();
                            }
                            count += _selectedAnchorIndices.Count;
                        }
                    }
                }

                // 2. Xóa các nét vẽ tự do được khoanh chọn
                if (_selectedCustomStrokeIndices.Count > 0)
                {
                    var sorted = new List<int>(_selectedCustomStrokeIndices);
                    sorted.Sort();
                    for (int i = sorted.Count - 1; i >= 0; i--)
                    {
                        int idx = sorted[i];
                        if (idx >= 0 && idx < _parent._customStrokes.Count)
                        {
                            _parent._customStrokes[idx].Dispose();
                            _parent._customStrokes.RemoveAt(idx);
                            count++;
                        }
                    }
                }

                _selectedAnchorIndices.Clear();
                _selectedCustomStrokeIndices.Clear();
                _activeAnchorIndex = -1;
                _parent._selectedAnchorIndex = -1;

                _hudMessage = string.Format("✂ Đã cắt / xóa thành công {0} điểm neo / nét khoanh chọn!", count);
                Invalidate();
            }

            public void CutSelectedAnchors()
            {
                if (!HasSelectedAnchors) return;

                int newStrokeIndex = -1;
                if (_selectedAnchorIndices.Count > 0 && !string.IsNullOrEmpty(_parent._selectedNodeId))
                {
                    var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                    if (item != null && (item.Transform == null || item.Transform.IsVisible))
                    {
                        var path = _parent.EnsureVectorPath(item);
                        if (path != null)
                        {
                            var extracted = IdsVectorGeometryService.ExtractAnchorPoints(path, _selectedAnchorIndices);
                            if (extracted != null && extracted.PointCount >= 2)
                            {
                                using (var m = new Matrix())
                                {
                                    m.Translate(35f, -35f);
                                    extracted.Transform(m);
                                }
                                _parent._customStrokes.Add(extracted);
                                newStrokeIndex = _parent._customStrokes.Count - 1;
                            }
                        }
                    }
                }

                DeleteSelectedAnchors();
                if (newStrokeIndex >= 0)
                {
                    _parent._selectedNodeId = null;
                    if (_parent._treeAst != null) _parent._treeAst.SelectedNode = null;
                    if (_parent._lvLayers != null) _parent._lvLayers.SelectedIndices.Clear();
                    _selectedCustomStrokeIndices.Clear();
                    _selectedCustomStrokeIndices.Add(newStrokeIndex);
                    _parent.SetToolMode(EditorToolMode.Select);
                }
                _hudMessage = "✂ Đã cắt phần khoanh chọn ra thành nét độc lập!";
                Invalidate();
            }

            // Coordinate transformations
            private RectangleF GetStageScreenRect()
            {
                float minDim = Math.Min(Width, Height);
                float stageSize = minDim * _parent._zoom;
                float stageX = (Width - stageSize) / 2f + _parent._panX;
                float stageY = (Height - stageSize) / 2f + _parent._panY;
                return new RectangleF(stageX, stageY, stageSize, stageSize);
            }

            private PointF StageToScreen(PointF p, RectangleF stageRect)
            {
                float scale = stageRect.Width / 1000f;
                return new PointF(stageRect.X + p.X * scale, stageRect.Y + p.Y * scale);
            }

            private PointF ScreenToStage(PointF p, RectangleF stageRect)
            {
                float scale = 1000f / stageRect.Width;
                return new PointF((p.X - stageRect.X) * scale, (p.Y - stageRect.Y) * scale);
            }

            private static PointF RotatePoint(PointF pt, PointF center, float angleDeg)
            {
                if (Math.Abs(angleDeg) < 0.001f) return pt;
                double rad = angleDeg * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);
                float dx = pt.X - center.X;
                float dy = pt.Y - center.Y;
                return new PointF(
                    (float)(center.X + dx * cos - dy * sin),
                    (float)(center.Y + dx * sin + dy * cos)
                );
            }

            // Hit testing
            private RectangleF GetSelectedNodesStageBounds()
            {
                if (_parent._selectedNodeIds.Count == 0) return RectangleF.Empty;
                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;
                int count = 0;

                foreach (string id in _parent._selectedNodeIds)
                {
                    var item = _parent._renderItems.Find(x => x.NodeId == id);
                    if (item != null && (item.Transform == null || item.Transform.IsVisible))
                    {
                        minX = Math.Min(minX, item.StageBounds.Left);
                        minY = Math.Min(minY, item.StageBounds.Top);
                        maxX = Math.Max(maxX, item.StageBounds.Right);
                        maxY = Math.Max(maxY, item.StageBounds.Bottom);
                        count++;
                    }
                }

                if (count == 0 || minX > maxX || minY > maxY) return RectangleF.Empty;
                return new RectangleF(minX, minY, maxX - minX, maxY - minY);
            }

            private BoundingHandle HitTestGroupBoundingHandles(Point mousePt, RectangleF groupBounds, RectangleF stageRect)
            {
                if (groupBounds.IsEmpty) return BoundingHandle.None;

                PointF centerStage = new PointF(groupBounds.X + groupBounds.Width / 2f, groupBounds.Y + groupBounds.Height / 2f);
                PointF pStage = ScreenToStage(mousePt, stageRect);

                float scale = stageRect.Width / 1000f;
                float halfW = (groupBounds.Width * scale) / 2f;
                float halfH = (groupBounds.Height * scale) / 2f;
                float localX = (pStage.X - centerStage.X) * scale;
                float localY = (pStage.Y - centerStage.Y) * scale;
                float stemLenScreen = 22f;

                // 1. Rotation handle check
                float rotDistSq = localX * localX + (localY - (-halfH - stemLenScreen)) * (localY - (-halfH - stemLenScreen));
                if (rotDistSq <= 64f)
                {
                    return BoundingHandle.Rotate;
                }

                // 2. 8 Scale handles check
                const float hTol = 7f;
                if (Math.Abs(localX - (-halfW)) <= hTol && Math.Abs(localY - (-halfH)) <= hTol) return BoundingHandle.TopLeft;
                if (Math.Abs(localX) <= hTol && Math.Abs(localY - (-halfH)) <= hTol) return BoundingHandle.TopCenter;
                if (Math.Abs(localX - halfW) <= hTol && Math.Abs(localY - (-halfH)) <= hTol) return BoundingHandle.TopRight;
                if (Math.Abs(localX - halfW) <= hTol && Math.Abs(localY) <= hTol) return BoundingHandle.MiddleRight;
                if (Math.Abs(localX - halfW) <= hTol && Math.Abs(localY - halfH) <= hTol) return BoundingHandle.BottomRight;
                if (Math.Abs(localX) <= hTol && Math.Abs(localY - halfH) <= hTol) return BoundingHandle.BottomCenter;
                if (Math.Abs(localX - (-halfW)) <= hTol && Math.Abs(localY - halfH) <= hTol) return BoundingHandle.BottomLeft;
                if (Math.Abs(localX - (-halfW)) <= hTol && Math.Abs(localY) <= hTol) return BoundingHandle.MiddleLeft;

                // 3. Body check
                if (Math.Abs(localX) <= halfW && Math.Abs(localY) <= halfH)
                {
                    return BoundingHandle.Body;
                }

                return BoundingHandle.None;
            }

            private void StartGroupManipulation(Point mousePt, RectangleF stageRect, RectangleF grpBounds, BoundingHandle handle)
            {
                _parent.PushUndo("Biến dạng nhóm linh kiện");
                _isManipulating = true;
                _activeHandle = handle;
                _dragStartMouseStage = ScreenToStage(mousePt, stageRect);
                _startGroupBounds = grpBounds;
                _startGroupCenter = new PointF(grpBounds.X + grpBounds.Width / 2f, grpBounds.Y + grpBounds.Height / 2f);
                _startAngleDeg = (float)(Math.Atan2(_dragStartMouseStage.Y - _startGroupCenter.Y, _dragStartMouseStage.X - _startGroupCenter.X) * 180.0 / Math.PI);

                _startMultiTransforms.Clear();
                _startMultiCenters.Clear();
                _startMultiBounds.Clear();
                foreach (var p in _startMultiCustomPaths.Values) { try { p.Dispose(); } catch { } }
                _startMultiCustomPaths.Clear();

                foreach (string id in _parent._selectedNodeIds)
                {
                    var node = _parent.FindNodeById(_parent._ast, id);
                    var item = _parent._renderItems.Find(x => x.NodeId == id);
                    if (node != null && item != null)
                    {
                        _startMultiTransforms[id] = node.Transform != null ? node.Transform.Clone() : new NodeCustomTransform();
                        _startMultiCenters[id] = new PointF(item.StageBounds.X + item.StageBounds.Width / 2f, item.StageBounds.Y + item.StageBounds.Height / 2f);
                        _startMultiBounds[id] = item.StageBounds;

                        if (_parent._componentCustomPaths.ContainsKey(id))
                        {
                            _startMultiCustomPaths[id] = (GraphicsPath)_parent._componentCustomPaths[id].Clone();
                        }
                        else if (item.VectorPath != null)
                        {
                            _startMultiCustomPaths[id] = (GraphicsPath)item.VectorPath.Clone();
                        }
                    }
                }

                Capture = true;
                Invalidate();
            }

            private void DrawMultiSelectionBoundingBox(Graphics g, RectangleF stageRect)
            {
                if (_parent._selectedNodeIds.Count <= 1) return;

                float scale = stageRect.Width / 1000f;

                // 1. Draw individual dashed outlines around each selected component
                using (var dashPen = new Pen(Color.FromArgb(14, 165, 233), 1.2f))
                {
                    dashPen.DashStyle = DashStyle.Dash;
                    dashPen.DashPattern = new float[] { 3, 3 };

                    foreach (string id in _parent._selectedNodeIds)
                    {
                        var item = _parent._renderItems.Find(x => x.NodeId == id);
                        if (item != null && (item.Transform == null || item.Transform.IsVisible))
                        {
                            PointF sp = StageToScreen(new PointF(item.StageBounds.X, item.StageBounds.Y), stageRect);
                            g.DrawRectangle(dashPen, sp.X, sp.Y, item.StageBounds.Width * scale, item.StageBounds.Height * scale);
                        }
                    }
                }

                // 2. Compute group aggregate bounds
                RectangleF groupBounds = GetSelectedNodesStageBounds();
                if (groupBounds.IsEmpty) return;

                PointF centerStage = new PointF(groupBounds.X + groupBounds.Width / 2f, groupBounds.Y + groupBounds.Height / 2f);
                PointF centerScreen = StageToScreen(centerStage, stageRect);

                float boxW = groupBounds.Width * scale;
                float boxH = groupBounds.Height * scale;
                float halfW = boxW / 2f;
                float halfH = boxH / 2f;
                float stemLen = 22f;

                var state = g.Save();
                g.TranslateTransform(centerScreen.X, centerScreen.Y);

                // Group fill tint (Subtle cyan)
                using (var fillBrush = new SolidBrush(Color.FromArgb(12, 14, 165, 233)))
                {
                    g.FillRectangle(fillBrush, -halfW, -halfH, boxW, boxH);
                }

                // Group solid outline (Cyan)
                using (var boxPen = new Pen(Color.FromArgb(14, 165, 233), 1.8f))
                {
                    g.DrawRectangle(boxPen, -halfW, -halfH, boxW, boxH);
                }

                // Stem line to rotation handle
                using (var stemPen = new Pen(Color.FromArgb(14, 165, 233), 1.3f))
                {
                    g.DrawLine(stemPen, 0, -halfH, 0, -halfH - stemLen);
                }

                // Rotation handle (White circle with cyan border)
                float rotR = 4.5f;
                using (var brush = new SolidBrush(Color.White))
                using (var pen = new Pen(Color.FromArgb(14, 165, 233), 1.8f))
                {
                    g.FillEllipse(brush, -rotR, -halfH - stemLen - rotR, rotR * 2, rotR * 2);
                    g.DrawEllipse(pen, -rotR, -halfH - stemLen - rotR, rotR * 2, rotR * 2);
                }

                // 8 Resize handles (White squares with cyan border)
                PointF[] handlePositions = new PointF[]
                {
                    new PointF(-halfW, -halfH),
                    new PointF(0, -halfH),
                    new PointF(halfW, -halfH),
                    new PointF(halfW, 0),
                    new PointF(halfW, halfH),
                    new PointF(0, halfH),
                    new PointF(-halfW, halfH),
                    new PointF(-halfW, 0)
                };

                float hSize = 7f;
                float hHalf = hSize / 2f;
                using (var fillBrush = new SolidBrush(Color.White))
                using (var borderPen = new Pen(Color.FromArgb(14, 165, 233), 1.5f))
                {
                    foreach (var hp in handlePositions)
                    {
                        g.FillRectangle(fillBrush, hp.X - hHalf, hp.Y - hHalf, hSize, hSize);
                        g.DrawRectangle(borderPen, hp.X - hHalf, hp.Y - hHalf, hSize, hSize);
                    }
                }

                g.Restore(state);
            }

            private BoundingHandle HitTestBoundingHandles(Point mousePt, GlyphRenderData item, RectangleF stageRect)
            {
                if (item == null || item.Transform == null || !item.Transform.IsVisible) return BoundingHandle.None;

                PointF centerStage = new PointF(item.StageBounds.X + item.StageBounds.Width / 2f, item.StageBounds.Y + item.StageBounds.Height / 2f);
                PointF pStage = ScreenToStage(mousePt, stageRect);
                PointF unrot = RotatePoint(pStage, centerStage, -item.Transform.Rotation);

                float scale = stageRect.Width / 1000f;
                float halfW = (item.StageBounds.Width * scale) / 2f;
                float halfH = (item.StageBounds.Height * scale) / 2f;
                float localX = (unrot.X - centerStage.X) * scale;
                float localY = (unrot.Y - centerStage.Y) * scale;
                float stemLenScreen = 22f;

                // 1. Rotation handle check
                float rotDistSq = localX * localX + (localY - (-halfH - stemLenScreen)) * (localY - (-halfH - stemLenScreen));
                if (rotDistSq <= 64f)
                {
                    return BoundingHandle.Rotate;
                }

                // 2. 8 Scale handles check (7x7 px square, hit radius ~7px)
                const float hTol = 7f;
                if (Math.Abs(localX - (-halfW)) <= hTol && Math.Abs(localY - (-halfH)) <= hTol) return BoundingHandle.TopLeft;
                if (Math.Abs(localX) <= hTol && Math.Abs(localY - (-halfH)) <= hTol) return BoundingHandle.TopCenter;
                if (Math.Abs(localX - halfW) <= hTol && Math.Abs(localY - (-halfH)) <= hTol) return BoundingHandle.TopRight;
                if (Math.Abs(localX - halfW) <= hTol && Math.Abs(localY) <= hTol) return BoundingHandle.MiddleRight;
                if (Math.Abs(localX - halfW) <= hTol && Math.Abs(localY - halfH) <= hTol) return BoundingHandle.BottomRight;
                if (Math.Abs(localX) <= hTol && Math.Abs(localY - halfH) <= hTol) return BoundingHandle.BottomCenter;
                if (Math.Abs(localX - (-halfW)) <= hTol && Math.Abs(localY - halfH) <= hTol) return BoundingHandle.BottomLeft;
                if (Math.Abs(localX - (-halfW)) <= hTol && Math.Abs(localY) <= hTol) return BoundingHandle.MiddleLeft;

                // 3. Body check
                if (Math.Abs(localX) <= halfW && Math.Abs(localY) <= halfH)
                {
                    return BoundingHandle.Body;
                }

                return BoundingHandle.None;
            }

            private int HitTestAnchorPoint(Point mousePt, GlyphRenderData item, RectangleF stageRect)
            {
                if (item == null || (item.Transform != null && !item.Transform.IsVisible)) return -1;
                var path = _parent.EnsureVectorPath(item);
                if (path == null || path.PointCount == 0) return -1;
                var pts = path.PathPoints;
                float thresholdSq = 64f;

                for (int i = 0; i < pts.Length; i++)
                {
                    PointF sp = StageToScreen(pts[i], stageRect);
                    float dx = mousePt.X - sp.X;
                    float dy = mousePt.Y - sp.Y;
                    if (dx * dx + dy * dy <= thresholdSq)
                    {
                        return i;
                    }
                }
                return -1;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                if (!Focused && CanFocus) Focus();
                if (_parent._toolMode == EditorToolMode.Hand || (ModifierKeys & Keys.Space) == Keys.Space)
                {
                    Cursor = Cursors.Hand;
                }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    if (e.Delta > 0) _parent.ZoomIn();
                    else if (e.Delta < 0) _parent.ZoomOut();
                }
                else
                {
                    if (e.Delta > 0) { _parent._panY += 40f; Invalidate(); }
                    else if (e.Delta < 0) { _parent._panY -= 40f; Invalidate(); }
                }
            }

            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Left:
                    case Keys.Right:
                    case Keys.Up:
                    case Keys.Down:
                    case Keys.Enter:
                    case Keys.Escape:
                        return true;
                }
                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if ((e.Modifiers & Keys.Control) == Keys.Control)
                {
                    if (e.KeyCode == Keys.Z && (e.Modifiers & Keys.Shift) == Keys.Shift) { _parent.PerformRedo(); e.Handled = true; return; }
                    if (e.KeyCode == Keys.Z) { _parent.PerformUndo(); e.Handled = true; return; }
                    if (e.KeyCode == Keys.Y) { _parent.PerformRedo(); e.Handled = true; return; }
                    if (e.KeyCode == Keys.J) { _parent.PerformAutoArrangeAndJoin(); e.Handled = true; return; }
                    if (e.KeyCode == Keys.X)
                    {
                        if (HasSelectedAnchors) { CutSelectedAnchors(); e.Handled = true; return; }
                        else { _parent.ContextCut(); e.Handled = true; return; }
                    }
                }
                if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
                {
                    if (HasSelectedAnchors) { DeleteSelectedAnchors(); e.Handled = true; return; }
                    else { _parent.ContextDelete(); e.Handled = true; return; }
                }
                if (e.Modifiers == Keys.None)
                {
                    if (e.KeyCode == Keys.Q) { _parent.SetToolMode(EditorToolMode.Select); e.Handled = true; return; }
                    if (e.KeyCode == Keys.V || e.KeyCode == Keys.H) { _parent.SetToolMode(EditorToolMode.Hand); e.Handled = true; return; }
                    if (e.KeyCode == Keys.A) { _parent.SetToolMode(EditorToolMode.DirectSelect); e.Handled = true; return; }
                    if (e.KeyCode == Keys.P) { _parent.SetToolMode(EditorToolMode.Pen); e.Handled = true; return; }
                    if (e.KeyCode == Keys.Z) { _parent.SetToolMode(EditorToolMode.Zoom); e.Handled = true; return; }
                    if (e.KeyCode == Keys.L) { _parent.SetToolMode(EditorToolMode.Lasso); e.Handled = true; return; }
                    if (e.KeyCode == Keys.C) { _parent.SetToolMode(EditorToolMode.LassoCut); e.Handled = true; return; }
                }
                if (_parent._toolMode == EditorToolMode.Pen && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return))
                {
                    CommitPenStroke();
                    e.Handled = true;
                    return;
                }
                if (e.KeyCode == Keys.Escape)
                {
                    if (_parent._toolMode == EditorToolMode.Pen && _parent._activePenPoints.Count > 0)
                    {
                        _parent._activePenPoints.Clear();
                        Invalidate();
                        e.Handled = true;
                        return;
                    }
                    if (HasActiveSelectionTransformer || HasSelectedAnchors)
                    {
                        _selectedAnchorIndices.Clear();
                        _selectedCustomStrokeIndices.Clear();
                        _activeAnchorIndex = -1;
                        _parent._selectedAnchorIndex = -1;
                        _selectionTransformRotation = 0f;
                        _hudMessage = "Đã hủy vùng chọn (Escape)";
                        Invalidate();
                        e.Handled = true;
                        return;
                    }
                }
                if (_parent.HandleArrowKeyNudge(e.KeyData))
                {
                    e.Handled = true;
                }
            }

            private void CommitPenStroke()
            {
                if (_parent._activePenPoints.Count >= 2)
                {
                    _parent.PushUndo("Vẽ nét bút (Pen Tool)");
                    var path = new GraphicsPath();
                    path.AddLines(_parent._activePenPoints.ToArray());
                    _parent._customStrokes.Add(path);
                    _parent._activePenPoints.Clear();
                    _parent.RebuildLayersList();
                    Invalidate();
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Focus();
                var stageRect = GetStageScreenRect();

                // Middle click -> canvas pan
                if (e.Button == MouseButtons.Middle)
                {
                    _isPanning = true;
                    _panStartMouse = e.Location;
                    _panStartOriginX = _parent._panX;
                    _panStartOriginY = _parent._panY;
                    Cursor = Cursors.SizeAll;
                    Capture = true;
                    return;
                }

                // Right click -> select component under cursor and show context menu
                if (e.Button == MouseButtons.Right)
                {
                    string rHit = IdsGlyphRenderer.HitTestNodeId(e.Location, ClientRectangle, _parent._renderItems, _parent._zoom, _parent._panX, _parent._panY);
                    if (!string.IsNullOrEmpty(rHit))
                    {
                        _parent._selectedNodeId = rHit;
                        _parent.RebuildInspector();
                        _parent.SelectTreeNodeById(_parent._treeAst.Nodes, rHit);
                        _parent.RebuildLayersList();
                        Invalidate();
                    }
                    return;
                }

                if (e.Button == MouseButtons.Left)
                {
                    // Kiểm tra thao tác trên Selection Transformer (Photoshop / Illustrator Free Transform)
                    if (HasActiveSelectionTransformer)
                    {
                        BoundingHandle selHandle = HitTestSelectionTransformerHandles(e.Location, stageRect);
                        if (selHandle != BoundingHandle.None)
                        {
                            _isTransformingSelection = true;
                            _activeSelectionHandle = selHandle;
                            _selectionDragStartStage = ScreenToStage(e.Location, stageRect);
                            _selectionDragStartMouse = e.Location;
                            _selectionStartBounds = GetSelectedRegionStageBounds();
                            _selectionStartCenter = new PointF(_selectionStartBounds.X + _selectionStartBounds.Width / 2f, _selectionStartBounds.Y + _selectionStartBounds.Height / 2f);
                            _selectionStartAngleDeg = (float)(Math.Atan2(_selectionDragStartStage.Y - _selectionStartCenter.Y, _selectionDragStartStage.X - _selectionStartCenter.X) * 180.0 / Math.PI);

                            _startAnchorPoints.Clear();
                            if (_selectedAnchorIndices.Count > 0 && !string.IsNullOrEmpty(_parent._selectedNodeId))
                            {
                                var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                                if (item != null && item.VectorPath != null)
                                {
                                    var pts = item.VectorPath.PathPoints;
                                    foreach (int idx in _selectedAnchorIndices)
                                    {
                                        if (idx >= 0 && idx < pts.Length)
                                        {
                                            _startAnchorPoints[idx] = pts[idx];
                                        }
                                    }
                                }
                            }

                            _startCustomStrokePoints.Clear();
                            foreach (int sIdx in _selectedCustomStrokeIndices)
                            {
                                if (sIdx >= 0 && sIdx < _parent._customStrokes.Count)
                                {
                                    var stroke = _parent._customStrokes[sIdx];
                                    if (stroke != null)
                                    {
                                        _startCustomStrokePoints[sIdx] = (PointF[])stroke.PathPoints.Clone();
                                    }
                                }
                            }

                            _parent.PushUndo("Biến đổi vùng chọn (Selection Transform)");
                            Capture = true;
                            Invalidate();
                            return;
                        }
                        else if ((ModifierKeys & Keys.Shift) != Keys.Shift)
                        {
                            // Bấm chuột ra vùng ngoài transformer mà không giữ Shift -> Tự động Hủy / Bỏ chọn (Giống Photoshop, Illustrator)
                            _selectedAnchorIndices.Clear();
                            _selectedCustomStrokeIndices.Clear();
                            _activeAnchorIndex = -1;
                            _parent._selectedAnchorIndex = -1;
                            _parent._selectedNodeId = null;
                            _selectionTransformRotation = 0f;
                            _hudMessage = "Đã bỏ chọn vùng transformer";
                            Invalidate();
                        }
                    }

                    // Spacebar held -> Panning shortcut
                    if ((ModifierKeys & Keys.Space) == Keys.Space || _parent._toolMode == EditorToolMode.Hand)
                    {
                        _isPanning = true;
                        _panStartMouse = e.Location;
                        _panStartOriginX = _parent._panX;
                        _panStartOriginY = _parent._panY;
                        Cursor = Cursors.SizeAll;
                        Capture = true;
                        return;
                    }

                    // Zoom tool
                    if (_parent._toolMode == EditorToolMode.Zoom)
                    {
                        if ((ModifierKeys & Keys.Alt) == Keys.Alt) _parent.ZoomOut();
                        else _parent.ZoomIn();
                        return;
                    }

                    // Pen tool
                    if (_parent._toolMode == EditorToolMode.Pen)
                    {
                        PointF ptStage = ScreenToStage(e.Location, stageRect);
                        _parent._activePenPoints.Add(ptStage);
                        Invalidate();
                        return;
                    }

                    // Lasso tool
                    if (_parent._toolMode == EditorToolMode.Lasso)
                    {
                        if ((ModifierKeys & Keys.Shift) != Keys.Shift)
                        {
                            _selectedAnchorIndices.Clear();
                            _selectedCustomStrokeIndices.Clear();
                        }
                        _isLassoing = true;
                        _lassoPoints.Clear();
                        _lassoPoints.Add(ScreenToStage(e.Location, stageRect));
                        Capture = true;
                        Invalidate();
                        return;
                    }

                    // Lasso Cut / Knife tool
                    if (_parent._toolMode == EditorToolMode.LassoCut)
                    {
                        _isCutting = true;
                        _cutPoints.Clear();
                        _cutPoints.Add(ScreenToStage(e.Location, stageRect));
                        Capture = true;
                        Invalidate();
                        return;
                    }

                    // Direct Selection tool (Anchor Points & Marquee Selection Box)
                    if (_parent._toolMode == EditorToolMode.DirectSelect)
                    {
                        var selItem = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                        int anchorIdx = (selItem != null) ? HitTestAnchorPoint(e.Location, selItem, stageRect) : -1;
                        if (anchorIdx >= 0)
                        {
                            _selectedCustomStrokeIndices.Clear();
                            if (!_selectedAnchorIndices.Contains(anchorIdx))
                            {
                                if ((ModifierKeys & Keys.Shift) != Keys.Shift)
                                {
                                    _selectedAnchorIndices.Clear();
                                }
                                _selectedAnchorIndices.Add(anchorIdx);
                            }
                            _isDraggingAnchor = true;
                            _activeAnchorIndex = anchorIdx;
                            _parent._selectedAnchorIndex = anchorIdx;
                            _anchorDragStartStage = ScreenToStage(e.Location, stageRect);
                            _parent.PushUndo("Di chuyển điểm neo");
                            Capture = true;
                            Invalidate();
                            return;
                        }

                        // Check click on custom strokes in DirectSelect
                        int dsStrokeHit = HitTestCustomStrokes(e.Location, stageRect);
                        if (dsStrokeHit >= 0)
                        {
                            _parent._selectedNodeId = null;
                            if (_parent._treeAst != null) _parent._treeAst.SelectedNode = null;
                            if (_parent._lvLayers != null) _parent._lvLayers.SelectedIndices.Clear();
                            _selectedAnchorIndices.Clear();
                            _activeAnchorIndex = -1;
                            _parent._selectedAnchorIndex = -1;

                            _selectedCustomStrokeIndices.Clear();
                            _selectedCustomStrokeIndices.Add(dsStrokeHit);

                            _isTransformingSelection = true;
                            _activeSelectionHandle = BoundingHandle.Body;
                            _selectionDragStartStage = ScreenToStage(e.Location, stageRect);
                            _selectionDragStartMouse = e.Location;
                            _selectionStartBounds = GetSelectedRegionStageBounds();
                            _selectionStartCenter = new PointF(_selectionStartBounds.X + _selectionStartBounds.Width / 2f, _selectionStartBounds.Y + _selectionStartBounds.Height / 2f);

                            _startAnchorPoints.Clear();
                            _startCustomStrokePoints.Clear();
                            _startCustomStrokePoints[dsStrokeHit] = (PointF[])_parent._customStrokes[dsStrokeHit].PathPoints.Clone();

                            Capture = true;
                            Invalidate();
                            return;
                        }

                        // Kéo selection box để chọn nhanh theo vùng (Marquee Box)
                        _isBoxSelecting = true;
                        _boxSelectStart = e.Location;
                        _boxSelectCurrent = e.Location;
                        if ((ModifierKeys & Keys.Shift) != Keys.Shift)
                        {
                            _selectedAnchorIndices.Clear();
                            _selectedCustomStrokeIndices.Clear();
                        }
                        Capture = true;
                        Invalidate();
                        return;
                    }

                    // Selection tool (V / Q)
                    if (_parent._toolMode == EditorToolMode.Select)
                    {
                        // Check handles on current selection
                        if (_parent._selectedNodeIds.Count > 1)
                        {
                            RectangleF grpBounds = GetSelectedNodesStageBounds();
                            BoundingHandle grpHandle = HitTestGroupBoundingHandles(e.Location, grpBounds, stageRect);
                            if (grpHandle != BoundingHandle.None)
                            {
                                bool isCtrl = (ModifierKeys & Keys.Control) == Keys.Control;
                                if (grpHandle != BoundingHandle.Body || !isCtrl)
                                {
                                    StartGroupManipulation(e.Location, stageRect, grpBounds, grpHandle);
                                    return;
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(_parent._selectedNodeId))
                        {
                            var selItem = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                            if (selItem != null && (selItem.Transform == null || !selItem.Transform.IsLocked))
                            {
                                BoundingHandle handle = HitTestBoundingHandles(e.Location, selItem, stageRect);
                                if (handle != BoundingHandle.None)
                                {
                                    _parent.PushUndo("Biến dạng linh kiện");
                                    _isManipulating = true;
                                    _activeHandle = handle;
                                    _dragStartMouseStage = ScreenToStage(e.Location, stageRect);
                                    _startTransform = selItem.Transform != null ? selItem.Transform.Clone() : new NodeCustomTransform();
                                    _startStageBounds = selItem.StageBounds;
                                    _startCenter = new PointF(_startStageBounds.X + _startStageBounds.Width / 2f, _startStageBounds.Y + _startStageBounds.Height / 2f);
                                    _startAngleDeg = (float)(Math.Atan2(_dragStartMouseStage.Y - _startCenter.Y, _dragStartMouseStage.X - _startCenter.X) * 180.0 / Math.PI);

                                    if (_dragStartCustomVectorPath != null) _dragStartCustomVectorPath.Dispose();
                                    _dragStartCustomVectorPath = null;
                                    if (_parent._componentCustomPaths.ContainsKey(selItem.NodeId))
                                    {
                                        _dragStartCustomVectorPath = (GraphicsPath)_parent._componentCustomPaths[selItem.NodeId].Clone();
                                    }
                                    else if (selItem.VectorPath != null)
                                    {
                                        _dragStartCustomVectorPath = (GraphicsPath)selItem.VectorPath.Clone();
                                    }

                                    Capture = true;
                                    return;
                                }
                            }
                        }

                        // 1. Check if user clicked on any Custom Stroke (cut piece or freehand stroke)
                        int hitStroke = HitTestCustomStrokes(e.Location, stageRect);
                        if (hitStroke >= 0)
                        {
                            _parent._selectedNodeIds.Clear();
                            _parent._selectedNodeId = null;
                            if (_parent._treeAst != null) _parent._treeAst.SelectedNode = null;
                            if (_parent._lvLayers != null) _parent._lvLayers.SelectedIndices.Clear();
                            _selectedAnchorIndices.Clear();
                            _activeAnchorIndex = -1;
                            _parent._selectedAnchorIndex = -1;

                            _selectedCustomStrokeIndices.Clear();
                            _selectedCustomStrokeIndices.Add(hitStroke);

                            _isTransformingSelection = true;
                            _activeSelectionHandle = BoundingHandle.Body;
                            _selectionDragStartStage = ScreenToStage(e.Location, stageRect);
                            _selectionDragStartMouse = e.Location;
                            _selectionStartBounds = GetSelectedRegionStageBounds();
                            _selectionStartCenter = new PointF(_selectionStartBounds.X + _selectionStartBounds.Width / 2f, _selectionStartBounds.Y + _selectionStartBounds.Height / 2f);
                            _selectionStartAngleDeg = (float)(Math.Atan2(_selectionDragStartStage.Y - _selectionStartCenter.Y, _selectionDragStartStage.X - _selectionStartCenter.X) * 180.0 / Math.PI);

                            _startAnchorPoints.Clear();
                            _startCustomStrokePoints.Clear();
                            _startCustomStrokePoints[hitStroke] = (PointF[])_parent._customStrokes[hitStroke].PathPoints.Clone();

                            _parent.PushUndo("Di chuyển nét vẽ");
                            Capture = true;
                            Invalidate();
                            return;
                        }

                        // 2. Test click on another component
                        string hit = IdsGlyphRenderer.HitTestNodeId(e.Location, ClientRectangle, _parent._renderItems, _parent._zoom, _parent._panX, _parent._panY);
                        if (!string.IsNullOrEmpty(hit))
                        {
                            _selectedCustomStrokeIndices.Clear();
                            _selectedAnchorIndices.Clear();
                            _activeAnchorIndex = -1;
                            _parent._selectedAnchorIndex = -1;

                            bool isCtrl = (ModifierKeys & Keys.Control) == Keys.Control;
                            if (isCtrl)
                            {
                                // Multi-select toggle!
                                if (_parent._selectedNodeIds.Contains(hit))
                                {
                                    _parent._selectedNodeIds.Remove(hit);
                                    if (_parent._selectedNodeId == hit)
                                    {
                                        _parent._selectedNodeId = null;
                                        foreach (string id in _parent._selectedNodeIds) { _parent._selectedNodeId = id; break; }
                                    }
                                }
                                else
                                {
                                    if (!string.IsNullOrEmpty(_parent._selectedNodeId) && !_parent._selectedNodeIds.Contains(_parent._selectedNodeId))
                                    {
                                        _parent._selectedNodeIds.Add(_parent._selectedNodeId);
                                    }
                                    _parent._selectedNodeIds.Add(hit);
                                    _parent._selectedNodeId = hit;
                                }

                                _parent.RebuildInspector();
                                _parent.SelectTreeNodeById(_parent._treeAst.Nodes, _parent._selectedNodeId);
                                _parent.RebuildLayersList();
                                Invalidate();
                                return;
                            }
                            else
                            {
                                // Click without Ctrl
                                if (_parent._selectedNodeIds.Count > 1 && _parent._selectedNodeIds.Contains(hit))
                                {
                                    RectangleF grpBounds = GetSelectedNodesStageBounds();
                                    StartGroupManipulation(e.Location, stageRect, grpBounds, BoundingHandle.Body);
                                    return;
                                }

                                _parent._selectedNodeIds.Clear();
                                _parent._selectedNodeIds.Add(hit);
                                _parent._selectedNodeId = hit;
                                _parent.RebuildInspector();
                                _parent.SelectTreeNodeById(_parent._treeAst.Nodes, hit);
                                _parent.RebuildLayersList();

                                var item = _parent._renderItems.Find(x => x.NodeId == hit);
                                if (item != null && (item.Transform == null || !item.Transform.IsLocked))
                                {
                                    _isManipulating = true;
                                    _activeHandle = BoundingHandle.Body;
                                    _dragStartMouseStage = ScreenToStage(e.Location, stageRect);
                                    _startTransform = item.Transform != null ? item.Transform.Clone() : new NodeCustomTransform();
                                    _startStageBounds = item.StageBounds;
                                    _startCenter = new PointF(_startStageBounds.X + _startStageBounds.Width / 2f, _startStageBounds.Y + _startStageBounds.Height / 2f);

                                    if (_dragStartCustomVectorPath != null) _dragStartCustomVectorPath.Dispose();
                                    _dragStartCustomVectorPath = null;
                                    if (_parent._componentCustomPaths.ContainsKey(item.NodeId))
                                    {
                                        _dragStartCustomVectorPath = (GraphicsPath)_parent._componentCustomPaths[item.NodeId].Clone();
                                    }
                                    else if (item.VectorPath != null)
                                    {
                                        _dragStartCustomVectorPath = (GraphicsPath)item.VectorPath.Clone();
                                    }

                                    Capture = true;
                                }
                                Invalidate();
                                return;
                            }
                        }
                        else
                        {
                            if ((ModifierKeys & Keys.Control) != Keys.Control)
                            {
                                _parent._selectedNodeIds.Clear();
                                _parent._selectedNodeId = null;
                                if (_parent._treeAst != null) _parent._treeAst.SelectedNode = null;
                                if (_parent._lvLayers != null) _parent._lvLayers.SelectedIndices.Clear();
                                _parent.RebuildInspector();
                                _parent.RebuildLayersList();
                            }
                            _selectedCustomStrokeIndices.Clear();
                            _selectedAnchorIndices.Clear();
                            _activeAnchorIndex = -1;
                            _parent._selectedAnchorIndex = -1;

                            // Empty canvas -> drag pan
                            _isPanning = true;
                            _panStartMouse = e.Location;
                            _panStartOriginX = _parent._panX;
                            _panStartOriginY = _parent._panY;
                            Cursor = Cursors.SizeAll;
                            Capture = true;
                            Invalidate();
                        }
                    }
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                var stageRect = GetStageScreenRect();

                // 0. Selection Transformer Dragging (Move, Scale, Rotate)
                if (_isTransformingSelection)
                {
                    PointF curStage = ScreenToStage(e.Location, stageRect);
                    float dx = curStage.X - _selectionDragStartStage.X;
                    float dy = curStage.Y - _selectionDragStartStage.Y;

                    if (_activeSelectionHandle == BoundingHandle.Body)
                    {
                        ApplySelectionTranslation(dx, dy);
                        _hudMessage = string.Format("↔ Dịch chuyển vùng chọn: X: {0:+0;-0;0} px   Y: {1:+0;-0;0} px", dx, dy);
                    }
                    else if (_activeSelectionHandle == BoundingHandle.Rotate)
                    {
                        float curAngle = (float)(Math.Atan2(curStage.Y - _selectionStartCenter.Y, curStage.X - _selectionStartCenter.X) * 180.0 / Math.PI);
                        float deltaAngle = curAngle - _selectionStartAngleDeg;
                        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                        {
                            deltaAngle = (float)(Math.Round(deltaAngle / 15.0) * 15.0);
                        }
                        ApplySelectionRotation(deltaAngle, _selectionStartCenter);
                        _selectionTransformRotation = deltaAngle;
                        _hudMessage = string.Format("⟳ Xoay vùng chọn: {0:0}°", deltaAngle);
                    }
                    else
                    {
                        float origW = Math.Max(10f, _selectionStartBounds.Width);
                        float origH = Math.Max(10f, _selectionStartBounds.Height);
                        float scaleFactorX = 1f;
                        float scaleFactorY = 1f;

                        switch (_activeSelectionHandle)
                        {
                            case BoundingHandle.MiddleRight: scaleFactorX = (origW + dx) / origW; break;
                            case BoundingHandle.MiddleLeft: scaleFactorX = (origW - dx) / origW; break;
                            case BoundingHandle.BottomCenter: scaleFactorY = (origH + dy) / origH; break;
                            case BoundingHandle.TopCenter: scaleFactorY = (origH - dy) / origH; break;
                            case BoundingHandle.BottomRight:
                                scaleFactorX = (origW + dx) / origW;
                                scaleFactorY = (origH + dy) / origH;
                                break;
                            case BoundingHandle.BottomLeft:
                                scaleFactorX = (origW - dx) / origW;
                                scaleFactorY = (origH + dy) / origH;
                                break;
                            case BoundingHandle.TopRight:
                                scaleFactorX = (origW + dx) / origW;
                                scaleFactorY = (origH - dy) / origH;
                                break;
                            case BoundingHandle.TopLeft:
                                scaleFactorX = (origW - dx) / origW;
                                scaleFactorY = (origH - dy) / origH;
                                break;
                        }

                        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                        {
                            float uniformScale = Math.Max(scaleFactorX, scaleFactorY);
                            if (_activeSelectionHandle == BoundingHandle.MiddleRight || _activeSelectionHandle == BoundingHandle.MiddleLeft)
                                scaleFactorY = scaleFactorX;
                            else if (_activeSelectionHandle == BoundingHandle.TopCenter || _activeSelectionHandle == BoundingHandle.BottomCenter)
                                scaleFactorX = scaleFactorY;
                            else
                            {
                                scaleFactorX = uniformScale;
                                scaleFactorY = uniformScale;
                            }
                        }

                        scaleFactorX = Math.Max(0.05f, Math.Min(10f, scaleFactorX));
                        scaleFactorY = Math.Max(0.05f, Math.Min(10f, scaleFactorY));

                        PointF scaleAnchor = _selectionStartCenter;
                        bool isAlt = (ModifierKeys & Keys.Alt) == Keys.Alt;
                        if (isAlt)
                        {
                            switch (_activeSelectionHandle)
                            {
                                case BoundingHandle.MiddleRight:
                                    scaleAnchor = new PointF(_selectionStartBounds.Left, _selectionStartCenter.Y);
                                    break;
                                case BoundingHandle.MiddleLeft:
                                    scaleAnchor = new PointF(_selectionStartBounds.Right, _selectionStartCenter.Y);
                                    break;
                                case BoundingHandle.BottomCenter:
                                    scaleAnchor = new PointF(_selectionStartCenter.X, _selectionStartBounds.Top);
                                    break;
                                case BoundingHandle.TopCenter:
                                    scaleAnchor = new PointF(_selectionStartCenter.X, _selectionStartBounds.Bottom);
                                    break;
                                case BoundingHandle.BottomRight:
                                    scaleAnchor = new PointF(_selectionStartBounds.Left, _selectionStartBounds.Top);
                                    break;
                                case BoundingHandle.BottomLeft:
                                    scaleAnchor = new PointF(_selectionStartBounds.Right, _selectionStartBounds.Top);
                                    break;
                                case BoundingHandle.TopRight:
                                    scaleAnchor = new PointF(_selectionStartBounds.Left, _selectionStartBounds.Bottom);
                                    break;
                                case BoundingHandle.TopLeft:
                                    scaleAnchor = new PointF(_selectionStartBounds.Right, _selectionStartBounds.Bottom);
                                    break;
                            }
                        }

                        ApplySelectionScale(scaleFactorX, scaleFactorY, scaleAnchor);
                        _hudMessage = isAlt
                            ? string.Format("\u2922 Co d\u00e3n 1 h\u01b0\u1edbng (Alt): W: {0:F0}%  H: {1:F0}%", scaleFactorX * 100f, scaleFactorY * 100f)
                            : string.Format("\u2922 Co d\u00e3n v\u00f9ng ch\u1ecdn: W: {0:F0}%  H: {1:F0}% [\u0110\u00e8 Alt \u0111\u1ec3 k\u00e9o 1 h\u01b0\u1edbng]", scaleFactorX * 100f, scaleFactorY * 100f);
                    }

                    Invalidate();
                    return;
                }

                // 1. Pan Dragging
                if (_isPanning)
                {
                    int dx = e.X - _panStartMouse.X;
                    int dy = e.Y - _panStartMouse.Y;
                    _parent._panX = _panStartOriginX + dx;
                    _parent._panY = _panStartOriginY + dy;
                    Invalidate();
                    return;
                }

                // 2. Direct Selection & Lasso Anchor Dragging (Multi-Anchor Support)
                if (_isDraggingAnchor && _parent._selectedNodeId != null && _activeAnchorIndex >= 0)
                {
                    var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                    var path = (item != null) ? _parent.EnsureVectorPath(item) : null;
                    if (path != null && _activeAnchorIndex < path.PointCount)
                    {
                        PointF curStage = ScreenToStage(e.Location, stageRect);
                        float dStageX = curStage.X - _anchorDragStartStage.X;
                        float dStageY = curStage.Y - _anchorDragStartStage.Y;

                        var pts = path.PathPoints;
                        var types = path.PathTypes;

                        if (_selectedAnchorIndices.Count > 0)
                        {
                            foreach (int idx in _selectedAnchorIndices)
                            {
                                if (idx >= 0 && idx < pts.Length)
                                {
                                    pts[idx] = new PointF(pts[idx].X + dStageX, pts[idx].Y + dStageY);
                                }
                            }
                        }
                        else
                        {
                            pts[_activeAnchorIndex] = new PointF(pts[_activeAnchorIndex].X + dStageX, pts[_activeAnchorIndex].Y + dStageY);
                        }

                        item.VectorPath = new GraphicsPath(pts, types);
                        _parent.SetComponentCustomPath(item.NodeId, item.VectorPath, item.TargetRect, item.Transform);
                        _anchorDragStartStage = curStage;

                        _hudMessage = string.Format("Di chuyển {0} điểm neo: ({1:+0;-0;0}, {2:+0;-0;0})",
                            _selectedAnchorIndices.Count > 0 ? _selectedAnchorIndices.Count : 1, pts[_activeAnchorIndex].X, pts[_activeAnchorIndex].Y);
                        Invalidate();
                    }
                    return;
                }

                // Lassoing drag
                if (_isLassoing)
                {
                    PointF pStage = ScreenToStage(e.Location, stageRect);
                    if (_lassoPoints.Count == 0 || DistSq(_lassoPoints[_lassoPoints.Count - 1], pStage) > 4f)
                    {
                        _lassoPoints.Add(pStage);
                        Invalidate();
                    }
                    return;
                }

                // Cutting drag
                if (_isCutting)
                {
                    PointF pStage = ScreenToStage(e.Location, stageRect);
                    _cutPoints.Add(pStage);
                    Invalidate();
                    return;
                }

                // Selection Box dragging (Direct Selection Tool A)
                if (_isBoxSelecting)
                {
                    _boxSelectCurrent = e.Location;
                    Invalidate();
                    return;
                }

                // 3. Selection Tool Manipulation (Move, Scale, Rotate)
                if (_isManipulating && _parent._selectedNodeIds.Count > 1)
                {
                    PointF curStage = ScreenToStage(e.Location, stageRect);
                    float dx = curStage.X - _dragStartMouseStage.X;
                    float dy = curStage.Y - _dragStartMouseStage.Y;

                    if (_activeHandle == BoundingHandle.Body)
                    {
                        if (_parent._snapToGridEnabled)
                        {
                            dx = (float)Math.Round(dx / 20.0) * 20.0f;
                            dy = (float)Math.Round(dy / 20.0) * 20.0f;
                        }

                        foreach (string id in _parent._selectedNodeIds)
                        {
                            var n = _parent.FindNodeById(_parent._ast, id);
                            if (n != null && (n.Transform == null || !n.Transform.IsLocked))
                            {
                                NodeCustomTransform sT;
                                if (!_startMultiTransforms.TryGetValue(id, out sT))
                                    sT = n.Transform != null ? n.Transform.Clone() : new NodeCustomTransform();

                                n.Transform = sT.Clone();
                                n.Transform.OffsetX = (int)Math.Max(-800, Math.Min(800, Math.Round(sT.OffsetX + dx)));
                                n.Transform.OffsetY = (int)Math.Max(-800, Math.Min(800, Math.Round(sT.OffsetY + dy)));
                                _parent._transformsMap[id] = n.Transform;
                            }
                        }

                        _hudMessage = string.Format("Nhóm: X: {0:+0;-0;0} px   Y: {1:+0;-0;0} px", dx, dy);
                        _parent._renderItems = IdsLayoutEngine.ComputeLayout(_parent._ast);
                        _parent.PreserveComponentCustomPaths();
                        Invalidate();
                        return;
                    }
                    else if (_activeHandle == BoundingHandle.Rotate)
                    {
                        float curAngle = (float)(Math.Atan2(curStage.Y - _startGroupCenter.Y, curStage.X - _startGroupCenter.X) * 180.0 / Math.PI);
                        float deltaAngle = curAngle - _startAngleDeg;
                        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                        {
                            deltaAngle = (float)(Math.Round(deltaAngle / 15.0) * 15.0);
                        }

                        float rad = deltaAngle * (float)Math.PI / 180f;
                        float cosA = (float)Math.Cos(rad);
                        float sinA = (float)Math.Sin(rad);

                        foreach (string id in _parent._selectedNodeIds)
                        {
                            var n = _parent.FindNodeById(_parent._ast, id);
                            if (n != null && (n.Transform == null || !n.Transform.IsLocked))
                            {
                                NodeCustomTransform sT;
                                if (!_startMultiTransforms.TryGetValue(id, out sT))
                                    sT = n.Transform != null ? n.Transform.Clone() : new NodeCustomTransform();

                                PointF sCenter;
                                if (!_startMultiCenters.TryGetValue(id, out sCenter))
                                    sCenter = _startGroupCenter;

                                float relX = sCenter.X - _startGroupCenter.X;
                                float relY = sCenter.Y - _startGroupCenter.Y;
                                float orbX = relX * cosA - relY * sinA;
                                float orbY = relX * sinA + relY * cosA;
                                float dX = orbX - relX;
                                float dY = orbY - relY;

                                n.Transform = sT.Clone();
                                n.Transform.OffsetX = (int)Math.Max(-400, Math.Min(400, Math.Round(sT.OffsetX + dX)));
                                n.Transform.OffsetY = (int)Math.Max(-400, Math.Min(400, Math.Round(sT.OffsetY + dY)));

                                int newRot = (int)Math.Round(sT.Rotation + deltaAngle);
                                while (newRot > 180) newRot -= 360;
                                while (newRot < -180) newRot += 360;
                                n.Transform.Rotation = newRot;

                                _parent._transformsMap[id] = n.Transform;
                            }
                        }

                        _hudMessage = string.Format("Góc xoay nhóm: {0:+0;-0;0}°", deltaAngle);
                        _parent._renderItems = IdsLayoutEngine.ComputeLayout(_parent._ast);
                        _parent.PreserveComponentCustomPaths();
                        Invalidate();
                        return;
                    }
                    else
                    {
                        // Scale Handles: calibrate sensitivity to sync 1:1 with user mouse cursor
                        float origW = Math.Max(120f, _startGroupBounds.Width);
                        float origH = Math.Max(120f, _startGroupBounds.Height);

                        float scaleFactorX = 1f;
                        float scaleFactorY = 1f;

                        bool isAlt = (ModifierKeys & Keys.Alt) == Keys.Alt;
                        float mult = isAlt ? 1f : 2f;

                        switch (_activeHandle)
                        {
                            case BoundingHandle.MiddleRight: scaleFactorX = (origW + mult * dx) / origW; break;
                            case BoundingHandle.MiddleLeft: scaleFactorX = (origW - mult * dx) / origW; break;
                            case BoundingHandle.BottomCenter: scaleFactorY = (origH + mult * dy) / origH; break;
                            case BoundingHandle.TopCenter: scaleFactorY = (origH - mult * dy) / origH; break;
                            case BoundingHandle.BottomRight:
                                scaleFactorX = (origW + mult * dx) / origW;
                                scaleFactorY = (origH + mult * dy) / origH;
                                break;
                            case BoundingHandle.BottomLeft:
                                scaleFactorX = (origW - mult * dx) / origW;
                                scaleFactorY = (origH + mult * dy) / origH;
                                break;
                            case BoundingHandle.TopRight:
                                scaleFactorX = (origW + mult * dx) / origW;
                                scaleFactorY = (origH - mult * dy) / origH;
                                break;
                            case BoundingHandle.TopLeft:
                                scaleFactorX = (origW - mult * dx) / origW;
                                scaleFactorY = (origH - mult * dy) / origH;
                                break;
                        }

                        // Proportional scaling when Shift is held
                        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                        {
                            float uniformScale = Math.Max(scaleFactorX, scaleFactorY);
                            if (_activeHandle == BoundingHandle.MiddleRight || _activeHandle == BoundingHandle.MiddleLeft)
                                scaleFactorY = scaleFactorX;
                            else if (_activeHandle == BoundingHandle.TopCenter || _activeHandle == BoundingHandle.BottomCenter)
                                scaleFactorX = scaleFactorY;
                            else
                            {
                                scaleFactorX = uniformScale;
                                scaleFactorY = uniformScale;
                            }
                        }

                        scaleFactorX = Math.Max(0.05f, Math.Min(10.0f, scaleFactorX));
                        scaleFactorY = Math.Max(0.05f, Math.Min(10.0f, scaleFactorY));

                        isAlt = (ModifierKeys & Keys.Alt) == Keys.Alt;
                        PointF anchor = _startGroupCenter;
                        if (isAlt)
                        {
                            switch (_activeHandle)
                            {
                                case BoundingHandle.BottomRight:
                                    anchor = new PointF(_startGroupBounds.Left, _startGroupBounds.Top);
                                    break;
                                case BoundingHandle.BottomLeft:
                                    anchor = new PointF(_startGroupBounds.Right, _startGroupBounds.Top);
                                    break;
                                case BoundingHandle.TopRight:
                                    anchor = new PointF(_startGroupBounds.Left, _startGroupBounds.Bottom);
                                    break;
                                case BoundingHandle.TopLeft:
                                    anchor = new PointF(_startGroupBounds.Right, _startGroupBounds.Bottom);
                                    break;
                                case BoundingHandle.MiddleRight:
                                    anchor = new PointF(_startGroupBounds.Left, _startGroupCenter.Y);
                                    break;
                                case BoundingHandle.MiddleLeft:
                                    anchor = new PointF(_startGroupBounds.Right, _startGroupCenter.Y);
                                    break;
                                case BoundingHandle.BottomCenter:
                                    anchor = new PointF(_startGroupCenter.X, _startGroupBounds.Top);
                                    break;
                                case BoundingHandle.TopCenter:
                                    anchor = new PointF(_startGroupCenter.X, _startGroupBounds.Bottom);
                                    break;
                            }
                        }

                        foreach (string id in _parent._selectedNodeIds)
                        {
                            var n = _parent.FindNodeById(_parent._ast, id);
                            if (n != null && (n.Transform == null || !n.Transform.IsLocked))
                            {
                                NodeCustomTransform sT;
                                if (!_startMultiTransforms.TryGetValue(id, out sT))
                                    sT = n.Transform != null ? n.Transform.Clone() : new NodeCustomTransform();

                                PointF sCenter;
                                if (!_startMultiCenters.TryGetValue(id, out sCenter))
                                    sCenter = _startGroupCenter;

                                float newCenterX = anchor.X + (sCenter.X - anchor.X) * scaleFactorX;
                                float newCenterY = anchor.Y + (sCenter.Y - anchor.Y) * scaleFactorY;
                                float dX = newCenterX - sCenter.X;
                                float dY = newCenterY - sCenter.Y;

                                n.Transform = sT.Clone();
                                n.Transform.ScaleX = (float)Math.Round(Math.Max(0.1f, Math.Min(5.0f, sT.ScaleX * scaleFactorX)), 2);
                                n.Transform.ScaleY = (float)Math.Round(Math.Max(0.1f, Math.Min(5.0f, sT.ScaleY * scaleFactorY)), 2);
                                n.Transform.OffsetX = (int)Math.Max(-400, Math.Min(400, Math.Round(sT.OffsetX + dX)));
                                n.Transform.OffsetY = (int)Math.Max(-400, Math.Min(400, Math.Round(sT.OffsetY + dY)));
                                _parent._transformsMap[id] = n.Transform;
                            }
                        }

                        _hudMessage = string.Format("Nhóm ({0}): W: {1:F0}%  H: {2:F0}% {3}",
                            _parent._selectedNodeIds.Count,
                            scaleFactorX * 100f,
                            scaleFactorY * 100f,
                            isAlt ? "[Alt: 1 hướng]" : "[Alt để kéo 1 hướng]");

                        _parent._renderItems = IdsLayoutEngine.ComputeLayout(_parent._ast);
                        _parent.PreserveComponentCustomPaths();
                        Invalidate();
                        return;
                    }
                }
                else if (_isManipulating && _parent._selectedNodeId != null)
                {
                    var node = _parent.FindNodeById(_parent._ast, _parent._selectedNodeId);
                    if (node != null && (node.Transform == null || !node.Transform.IsLocked))
                    {
                        PointF curStage = ScreenToStage(e.Location, stageRect);
                        float dx = curStage.X - _dragStartMouseStage.X;
                        float dy = curStage.Y - _dragStartMouseStage.Y;

                        if (_activeHandle == BoundingHandle.Body)
                        {
                            float targetOffsetX = _startTransform.OffsetX + dx;
                            float targetOffsetY = _startTransform.OffsetY + dy;

                            // Snap to Grid
                            if (_parent._snapToGridEnabled)
                            {
                                targetOffsetX = (float)Math.Round(targetOffsetX / 20.0) * 20.0f;
                                targetOffsetY = (float)Math.Round(targetOffsetY / 20.0) * 20.0f;
                            }

                            // Smart Guides
                            _parent._activeSmartGuides.Clear();
                            if (_parent._smartGuidesEnabled)
                            {
                                float testCenterX = _startCenter.X + (targetOffsetX - _startTransform.OffsetX);
                                float testCenterY = _startCenter.Y + (targetOffsetY - _startTransform.OffsetY);
                                float testLeft = _startStageBounds.X + (targetOffsetX - _startTransform.OffsetX);
                                float testRight = testLeft + _startStageBounds.Width;
                                float testTop = _startStageBounds.Y + (targetOffsetY - _startTransform.OffsetY);
                                float testBottom = testTop + _startStageBounds.Height;
                                const float threshold = 12f;

                                // Canvas Center
                                if (Math.Abs(testCenterX - 500f) < threshold)
                                {
                                    targetOffsetX += (500f - testCenterX);
                                    _parent._activeSmartGuides.Add(new SmartGuideLine(500, 0, 500, 1000, "Tâm dọc 500", Color.FromArgb(236, 72, 153)));
                                }
                                if (Math.Abs(testCenterY - 500f) < threshold)
                                {
                                    targetOffsetY += (500f - testCenterY);
                                    _parent._activeSmartGuides.Add(new SmartGuideLine(0, 500, 1000, 500, "Tâm ngang 500", Color.FromArgb(236, 72, 153)));
                                }

                                // Em-Square Margins (90, 910)
                                if (Math.Abs(testLeft - 90f) < threshold)
                                {
                                    targetOffsetX += (90f - testLeft);
                                    _parent._activeSmartGuides.Add(new SmartGuideLine(90, 0, 90, 1000, "Lề trái 90", Color.FromArgb(236, 72, 153)));
                                }
                                if (Math.Abs(testRight - 910f) < threshold)
                                {
                                    targetOffsetX += (910f - testRight);
                                    _parent._activeSmartGuides.Add(new SmartGuideLine(910, 0, 910, 1000, "Lề phải 910", Color.FromArgb(236, 72, 153)));
                                }
                                if (Math.Abs(testTop - 90f) < threshold)
                                {
                                    targetOffsetY += (90f - testTop);
                                    _parent._activeSmartGuides.Add(new SmartGuideLine(0, 90, 1000, 90, "Lề trên 90", Color.FromArgb(236, 72, 153)));
                                }
                                if (Math.Abs(testBottom - 910f) < threshold)
                                {
                                    targetOffsetY += (910f - testBottom);
                                    _parent._activeSmartGuides.Add(new SmartGuideLine(0, 910, 1000, 910, "Lề dưới 910", Color.FromArgb(236, 72, 153)));
                                }

                                // Sibling Components
                                foreach (var sib in _parent._renderItems)
                                {
                                    if (sib.NodeId == _parent._selectedNodeId) continue;
                                    float sibCenterX = sib.StageBounds.X + sib.StageBounds.Width / 2f;
                                    float sibCenterY = sib.StageBounds.Y + sib.StageBounds.Height / 2f;

                                    if (Math.Abs(testCenterX - sibCenterX) < threshold)
                                    {
                                        targetOffsetX += (sibCenterX - testCenterX);
                                        _parent._activeSmartGuides.Add(new SmartGuideLine(sibCenterX, 0, sibCenterX, 1000, "Gióng tâm X [" + sib.Char + "]", Color.FromArgb(59, 130, 246)));
                                        break;
                                    }
                                    if (Math.Abs(testCenterY - sibCenterY) < threshold)
                                    {
                                        targetOffsetY += (sibCenterY - testCenterY);
                                        _parent._activeSmartGuides.Add(new SmartGuideLine(0, sibCenterY, 1000, sibCenterY, "Gióng tâm Y [" + sib.Char + "]", Color.FromArgb(59, 130, 246)));
                                        break;
                                    }
                                }
                            }

                            node.Transform = _startTransform.Clone();
                            node.Transform.OffsetX = (int)Math.Max(-800, Math.Min(800, targetOffsetX));
                            node.Transform.OffsetY = (int)Math.Max(-800, Math.Min(800, targetOffsetY));
                            _parent._transformsMap[node.Id] = node.Transform;
                            _hudMessage = string.Format("X: {0:+0;-0;0} px   Y: {1:+0;-0;0} px", node.Transform.OffsetX, node.Transform.OffsetY);

                            _parent._renderItems = IdsLayoutEngine.ComputeLayout(_parent._ast);
                            _parent.PreserveComponentCustomPaths();
                            Invalidate();
                            return;
                        }
                        else if (_activeHandle == BoundingHandle.Rotate)
                        {
                            float curAngle = (float)(Math.Atan2(curStage.Y - _startCenter.Y, curStage.X - _startCenter.X) * 180.0 / Math.PI);
                            float deltaAngle = curAngle - _startAngleDeg;
                            float rawRot = _startTransform.Rotation + deltaAngle;

                            // Shift snaps to 15-degree steps
                            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                            {
                                rawRot = (float)(Math.Round(rawRot / 15.0) * 15.0);
                            }

                            int newRot = (int)Math.Round(rawRot);
                            while (newRot > 180) newRot -= 360;
                            while (newRot < -180) newRot += 360;

                            node.Transform = _startTransform.Clone();
                            node.Transform.Rotation = newRot;
                            _parent._transformsMap[node.Id] = node.Transform;
                            _hudMessage = string.Format("Góc xoay: {0}°", newRot);

                            _parent._renderItems = IdsLayoutEngine.ComputeLayout(_parent._ast);
                            _parent.PreserveComponentCustomPaths();
                            Invalidate();
                            return;
                        }
                        else
                        {
                            // Scale Handles: calibrate sensitivity to sync 1:1 with user mouse cursor
                            PointF curUnrot = RotatePoint(curStage, _startCenter, -_startTransform.Rotation);
                            PointF startUnrot = RotatePoint(_dragStartMouseStage, _startCenter, -_startTransform.Rotation);
                            float localDx = curUnrot.X - startUnrot.X;
                            float localDy = curUnrot.Y - startUnrot.Y;

                            float origW = Math.Max(120f, _startStageBounds.Width);
                            float origH = Math.Max(120f, _startStageBounds.Height);

                            float scaleFactorX = 1f;
                            float scaleFactorY = 1f;

                            bool isAltScale = (ModifierKeys & Keys.Alt) == Keys.Alt;
                            float mult = isAltScale ? 1f : 2f;

                            switch (_activeHandle)
                            {
                                case BoundingHandle.MiddleRight: scaleFactorX = (origW + mult * localDx) / origW; break;
                                case BoundingHandle.MiddleLeft: scaleFactorX = (origW - mult * localDx) / origW; break;
                                case BoundingHandle.BottomCenter: scaleFactorY = (origH + mult * localDy) / origH; break;
                                case BoundingHandle.TopCenter: scaleFactorY = (origH - mult * localDy) / origH; break;
                                case BoundingHandle.BottomRight:
                                    scaleFactorX = (origW + mult * localDx) / origW;
                                    scaleFactorY = (origH + mult * localDy) / origH;
                                    break;
                                case BoundingHandle.BottomLeft:
                                    scaleFactorX = (origW - mult * localDx) / origW;
                                    scaleFactorY = (origH + mult * localDy) / origH;
                                    break;
                                case BoundingHandle.TopRight:
                                    scaleFactorX = (origW + mult * localDx) / origW;
                                    scaleFactorY = (origH - mult * localDy) / origH;
                                    break;
                                case BoundingHandle.TopLeft:
                                    scaleFactorX = (origW - mult * localDx) / origW;
                                    scaleFactorY = (origH - mult * localDy) / origH;
                                    break;
                            }

                            // Proportional scaling when Shift is held
                            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                            {
                                float uniformScale = Math.Max(scaleFactorX, scaleFactorY);
                                if (_activeHandle == BoundingHandle.MiddleRight || _activeHandle == BoundingHandle.MiddleLeft)
                                    scaleFactorY = scaleFactorX;
                                else if (_activeHandle == BoundingHandle.TopCenter || _activeHandle == BoundingHandle.BottomCenter)
                                    scaleFactorX = scaleFactorY;
                                else
                                {
                                    scaleFactorX = uniformScale;
                                    scaleFactorY = uniformScale;
                                }
                            }

                            float newScaleX = Math.Max(0.1f, Math.Min(5.0f, _startTransform.ScaleX * scaleFactorX));
                            float newScaleY = Math.Max(0.1f, Math.Min(5.0f, _startTransform.ScaleY * scaleFactorY));

                            node.Transform = _startTransform.Clone();
                            node.Transform.ScaleX = (float)Math.Round(newScaleX, 2);
                            node.Transform.ScaleY = (float)Math.Round(newScaleY, 2);

                            float shiftLocalX = 0f;
                            float shiftLocalY = 0f;

                            // 1-directional scaling when Alt is held: pin opposite edge/corner
                            if ((ModifierKeys & Keys.Alt) == Keys.Alt)
                            {
                                float baseScaleX = (_startTransform.ScaleX != 0) ? _startTransform.ScaleX : 1.0f;
                                float baseScaleY = (_startTransform.ScaleY != 0) ? _startTransform.ScaleY : 1.0f;
                                float deltaW = origW * (node.Transform.ScaleX - _startTransform.ScaleX) / baseScaleX;
                                float deltaH = origH * (node.Transform.ScaleY - _startTransform.ScaleY) / baseScaleY;

                                switch (_activeHandle)
                                {
                                    case BoundingHandle.MiddleRight:
                                        shiftLocalX = +deltaW / 2f;
                                        break;
                                    case BoundingHandle.MiddleLeft:
                                        shiftLocalX = -deltaW / 2f;
                                        break;
                                    case BoundingHandle.BottomCenter:
                                        shiftLocalY = +deltaH / 2f;
                                        break;
                                    case BoundingHandle.TopCenter:
                                        shiftLocalY = -deltaH / 2f;
                                        break;
                                    case BoundingHandle.BottomRight:
                                        shiftLocalX = +deltaW / 2f;
                                        shiftLocalY = +deltaH / 2f;
                                        break;
                                    case BoundingHandle.BottomLeft:
                                        shiftLocalX = -deltaW / 2f;
                                        shiftLocalY = +deltaH / 2f;
                                        break;
                                    case BoundingHandle.TopRight:
                                        shiftLocalX = +deltaW / 2f;
                                        shiftLocalY = -deltaH / 2f;
                                        break;
                                    case BoundingHandle.TopLeft:
                                        shiftLocalX = -deltaW / 2f;
                                        shiftLocalY = -deltaH / 2f;
                                        break;
                                }

                                float rotRad = _startTransform.Rotation * (float)Math.PI / 180f;
                                float cosRot = (float)Math.Cos(rotRad);
                                float sinRot = (float)Math.Sin(rotRad);
                                float stageShiftX = shiftLocalX * cosRot - shiftLocalY * sinRot;
                                float stageShiftY = shiftLocalX * sinRot + shiftLocalY * cosRot;

                                node.Transform.OffsetX = (int)Math.Round(_startTransform.OffsetX + stageShiftX);
                                node.Transform.OffsetY = (int)Math.Round(_startTransform.OffsetY + stageShiftY);
                            }

                            _parent._transformsMap[node.Id] = node.Transform;

                            isAltScale = (ModifierKeys & Keys.Alt) == Keys.Alt;
                            _hudMessage = isAltScale
                                ? string.Format("T\u1ec9 l\u1ec7 (1 h\u01b0\u1edbng - Alt): W: {0:F0}%  H: {1:F0}%", node.Transform.ScaleX * 100, node.Transform.ScaleY * 100)
                                : string.Format("T\u1ec9 l\u1ec7 (t\u00e2m): W: {0:F0}%  H: {1:F0}% [\u0110\u00e8 Alt \u0111\u1ec3 k\u00e9o 1 h\u01b0\u1edbng]", node.Transform.ScaleX * 100, node.Transform.ScaleY * 100);

                            _parent._renderItems = IdsLayoutEngine.ComputeLayout(_parent._ast);
                            _parent.PreserveComponentCustomPaths();
                            Invalidate();
                            return;
                        }
                    }
                }

                // 3.5. Selection Transformer Cursor Feedback
                if (HasActiveSelectionTransformer && !_isTransformingSelection && !_isManipulating && !_isLassoing && !_isBoxSelecting)
                {
                    BoundingHandle selHandle = HitTestSelectionTransformerHandles(e.Location, stageRect);
                    switch (selHandle)
                    {
                        case BoundingHandle.Rotate:
                            Cursor = Cursors.Hand;
                            return;
                        case BoundingHandle.TopLeft:
                        case BoundingHandle.BottomRight:
                            Cursor = Cursors.SizeNWSE;
                            return;
                        case BoundingHandle.TopRight:
                        case BoundingHandle.BottomLeft:
                            Cursor = Cursors.SizeNESW;
                            return;
                        case BoundingHandle.TopCenter:
                        case BoundingHandle.BottomCenter:
                            Cursor = Cursors.SizeNS;
                            return;
                        case BoundingHandle.MiddleLeft:
                        case BoundingHandle.MiddleRight:
                            Cursor = Cursors.SizeWE;
                            return;
                        case BoundingHandle.Body:
                            Cursor = Cursors.SizeAll;
                            return;
                    }
                }

                // 4. Hover Cursor Feedback
                if (_parent._toolMode == EditorToolMode.Hand || (ModifierKeys & Keys.Space) == Keys.Space)
                {
                    Cursor = _isPanning ? Cursors.SizeAll : Cursors.Hand;
                }
                else if (_parent._toolMode == EditorToolMode.Zoom)
                {
                    Cursor = Cursors.Help;
                }
                else if (_parent._toolMode == EditorToolMode.Lasso)
                {
                    var selItem = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                    int anchorHit = (selItem != null) ? HitTestAnchorPoint(e.Location, selItem, stageRect) : -1;
                    Cursor = (anchorHit >= 0) ? Cursors.Hand : Cursors.Cross;
                }
                else if (_parent._toolMode == EditorToolMode.LassoCut)
                {
                    Cursor = Cursors.Cross;
                }
                else if (_parent._toolMode == EditorToolMode.Pen)
                {
                    _parent._penCursorPt = e.Location;
                    Cursor = Cursors.Cross;
                    Invalidate();
                }
                else if (_parent._toolMode == EditorToolMode.DirectSelect)
                {
                    var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                    int anchorHit = (item != null) ? HitTestAnchorPoint(e.Location, item, stageRect) : -1;
                    Cursor = (anchorHit >= 0) ? Cursors.Hand : Cursors.Default;
                }
                else if (_parent._toolMode == EditorToolMode.Select)
                {
                    if (_parent._selectedNodeIds.Count > 1)
                    {
                        RectangleF grpBounds = GetSelectedNodesStageBounds();
                        BoundingHandle handle = HitTestGroupBoundingHandles(e.Location, grpBounds, stageRect);
                        switch (handle)
                        {
                            case BoundingHandle.Rotate:
                                Cursor = Cursors.Hand;
                                return;
                            case BoundingHandle.TopLeft:
                            case BoundingHandle.BottomRight:
                                Cursor = Cursors.SizeNWSE;
                                return;
                            case BoundingHandle.TopRight:
                            case BoundingHandle.BottomLeft:
                                Cursor = Cursors.SizeNESW;
                                return;
                            case BoundingHandle.TopCenter:
                            case BoundingHandle.BottomCenter:
                                Cursor = Cursors.SizeNS;
                                return;
                            case BoundingHandle.MiddleLeft:
                            case BoundingHandle.MiddleRight:
                                Cursor = Cursors.SizeWE;
                                return;
                            case BoundingHandle.Body:
                                Cursor = Cursors.SizeAll;
                                return;
                        }
                    }
                    else if (!string.IsNullOrEmpty(_parent._selectedNodeId))
                    {
                        var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                        if (item != null && (item.Transform == null || item.Transform.IsVisible))
                        {
                            BoundingHandle handle = HitTestBoundingHandles(e.Location, item, stageRect);
                            switch (handle)
                            {
                                case BoundingHandle.Rotate:
                                    Cursor = Cursors.Hand;
                                    return;
                                case BoundingHandle.TopLeft:
                                case BoundingHandle.BottomRight:
                                    Cursor = Cursors.SizeNWSE;
                                    return;
                                case BoundingHandle.TopRight:
                                case BoundingHandle.BottomLeft:
                                    Cursor = Cursors.SizeNESW;
                                    return;
                                case BoundingHandle.TopCenter:
                                case BoundingHandle.BottomCenter:
                                    Cursor = Cursors.SizeNS;
                                    return;
                                case BoundingHandle.MiddleLeft:
                                case BoundingHandle.MiddleRight:
                                    Cursor = Cursors.SizeWE;
                                    return;
                                case BoundingHandle.Body:
                                    Cursor = Cursors.SizeAll;
                                    return;
                            }
                        }
                    }

                    string hit = IdsGlyphRenderer.HitTestNodeId(e.Location, ClientRectangle, _parent._renderItems, _parent._zoom, _parent._panX, _parent._panY);
                    int hStroke = HitTestCustomStrokes(e.Location, stageRect);
                    Cursor = (!string.IsNullOrEmpty(hit) || hStroke >= 0) ? Cursors.Hand : Cursors.Default;
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                var stageRect = GetStageScreenRect();
                if (_isLassoing)
                {
                    _isLassoing = false;
                    Capture = false;
                    bool isShift = (ModifierKeys & Keys.Shift) == Keys.Shift;

                    if (_lassoPoints.Count >= 3)
                    {
                        _parent.EnsureAllComponentVectorPaths();

                        GlyphRenderData bestItem = null;
                        List<int> bestIndices = null;
                        int maxInsideCount = 0;

                        // Ưu tiên linh kiện đang kích hoạt nếu vùng khoanh chứa điểm neo của nó
                        if (!string.IsNullOrEmpty(_parent._selectedNodeId))
                        {
                            var curItem = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                            if (curItem != null && (curItem.Transform == null || curItem.Transform.IsVisible))
                            {
                                var path = _parent.EnsureVectorPath(curItem);
                                if (path != null && path.PointCount > 0)
                                {
                                    List<int> insideIndices;
                                    if (IdsVectorGeometryService.IsPathInPolygon(path, _lassoPoints, out insideIndices) && insideIndices.Count > 0)
                                    {
                                        bestItem = curItem;
                                        bestIndices = insideIndices;
                                        maxInsideCount = insideIndices.Count;
                                    }
                                }
                            }
                        }

                        // Nếu linh kiện hiện tại không chứa điểm neo nào lọt vào vòng khoanh, quét các linh kiện khác
                        if (bestItem == null)
                        {
                            foreach (var item in _parent._renderItems)
                            {
                                if (item.Transform != null && !item.Transform.IsVisible) continue;
                                var path = _parent.EnsureVectorPath(item);
                                if (path == null || path.PointCount == 0) continue;

                                List<int> insideIndices;
                                if (IdsVectorGeometryService.IsPathInPolygon(path, _lassoPoints, out insideIndices))
                                {
                                    if (insideIndices.Count > maxInsideCount)
                                    {
                                        maxInsideCount = insideIndices.Count;
                                        bestItem = item;
                                        bestIndices = insideIndices;
                                    }
                                }
                            }
                        }

                        if (!isShift)
                        {
                            _selectedCustomStrokeIndices.Clear();
                        }

                        // 2. Kiểm tra các nét vẽ tự do (Custom Strokes)
                        int customStrokesMatched = 0;
                        for (int k = 0; k < _parent._customStrokes.Count; k++)
                        {
                            var stroke = _parent._customStrokes[k];
                            List<int> strokeIndices;
                            if (IdsVectorGeometryService.IsPathInPolygon(stroke, _lassoPoints, out strokeIndices))
                            {
                                _selectedCustomStrokeIndices.Add(k);
                                customStrokesMatched++;
                            }
                        }

                        // 3. Áp dụng kết quả lựa chọn
                        if (bestItem != null)
                        {
                            if (!isShift || _parent._selectedNodeId != bestItem.NodeId)
                            {
                                _parent._selectedNodeId = bestItem.NodeId;
                                if (!isShift) _selectedAnchorIndices.Clear();
                            }

                            if (bestIndices != null && bestIndices.Count > 0)
                            {
                                foreach (int idx in bestIndices) _selectedAnchorIndices.Add(idx);
                                _activeAnchorIndex = bestIndices[0];
                                _parent._selectedAnchorIndex = _activeAnchorIndex;
                            }
                            else if (!isShift)
                            {
                                _activeAnchorIndex = -1;
                                _parent._selectedAnchorIndex = -1;
                            _parent._selectedNodeId = null;
                            }

                            _parent.RebuildInspector();
                            _parent.SelectTreeNodeById(_parent._treeAst.Nodes, bestItem.NodeId);
                            _parent.RebuildLayersList();

                            int ptCount = _selectedAnchorIndices.Count;
                            _hudMessage = string.Format("🪢 Lasso: Đã chọn nét [{0}] ({1} điểm neo)", bestItem.Char, ptCount);
                        }
                        else if (customStrokesMatched > 0 || (isShift && _selectedCustomStrokeIndices.Count > 0))
                        {
                            if (!isShift)
                            {
                                _selectedAnchorIndices.Clear();
                                _activeAnchorIndex = -1;
                                _parent._selectedAnchorIndex = -1;
                            _parent._selectedNodeId = null;
                            }
                            _hudMessage = string.Format("🪢 Lasso: Đã chọn {0} nét vẽ tự do", _selectedCustomStrokeIndices.Count);
                        }
                        else
                        {
                            if (!isShift)
                            {
                                _selectedAnchorIndices.Clear();
                                _selectedCustomStrokeIndices.Clear();
                                _activeAnchorIndex = -1;
                                _parent._selectedAnchorIndex = -1;
                            _parent._selectedNodeId = null;
                                _selectionTransformRotation = 0f;
                                _parent._selectedNodeId = null;
                                _hudMessage = "🪢 Lasso: Không có nét hoặc điểm neo nào trong vùng khoanh";
                            }
                        }
                    }
                    else
                    {
                        // Thao tác Click chuột đơn lẻ bằng công cụ Lasso (Single Click)
                        int hitStrokeSingle = HitTestCustomStrokes(e.Location, stageRect);
                        if (hitStrokeSingle >= 0)
                        {
                            _parent._selectedNodeId = null;
                            if (_parent._treeAst != null) _parent._treeAst.SelectedNode = null;
                            if (_parent._lvLayers != null) _parent._lvLayers.SelectedIndices.Clear();
                            _selectedAnchorIndices.Clear();
                            _activeAnchorIndex = -1;
                            _parent._selectedAnchorIndex = -1;

                            if (!isShift) _selectedCustomStrokeIndices.Clear();
                            _selectedCustomStrokeIndices.Add(hitStrokeSingle);
                            _hudMessage = string.Format("🪢 Lasso: Đã chọn nét #{0}", hitStrokeSingle + 1);
                        }
                        else
                        {
                            string hitNode = IdsGlyphRenderer.HitTestNodeId(e.Location, ClientRectangle, _parent._renderItems, _parent._zoom, _parent._panX, _parent._panY);
                        if (!string.IsNullOrEmpty(hitNode))
                        {
                            _parent._selectedNodeId = hitNode;
                            var hitItem = _parent._renderItems.Find(x => x.NodeId == hitNode);
                            int aHit = (hitItem != null) ? HitTestAnchorPoint(e.Location, hitItem, stageRect) : -1;
                            if (!isShift)
                            {
                                _selectedAnchorIndices.Clear();
                                _selectedCustomStrokeIndices.Clear();
                            }
                            if (aHit >= 0)
                            {
                                _selectedAnchorIndices.Add(aHit);
                                _activeAnchorIndex = aHit;
                                _parent._selectedAnchorIndex = aHit;
                            }
                            else
                            {
                                _activeAnchorIndex = -1;
                                _parent._selectedAnchorIndex = -1;
                            _parent._selectedNodeId = null;
                            }
                            _parent.RebuildInspector();
                            _parent.SelectTreeNodeById(_parent._treeAst.Nodes, hitNode);
                            _parent.RebuildLayersList();
                            _hudMessage = string.Format("Đã chọn linh kiện [{0}]", hitItem != null ? hitItem.Char : hitNode);
                        }
                        else
                        {
                            if (!isShift)
                            {
                                _selectedAnchorIndices.Clear();
                                _selectedCustomStrokeIndices.Clear();
                                _activeAnchorIndex = -1;
                                _parent._selectedAnchorIndex = -1;
                                _parent._selectedNodeId = null;
                                _selectionTransformRotation = 0f;
                                _parent._selectedNodeId = null;
                                _hudMessage = "Đã bỏ chọn";
                            }
                        }
                        }
                    }

                    _lassoPoints.Clear();
                    Invalidate();
                    return;
                }
                if (_isCutting)
                {
                    _isCutting = false;
                    Capture = false;
                    if (_cutPoints.Count >= 2)
                    {
                        bool cutSuccess = false;
                        PointF cutJunction = PointF.Empty;

                        // 1. Cắt nét custom strokes (hỗ trợ toàn bộ quỹ đạo vẽ tự do)
                        for (int i = _parent._customStrokes.Count - 1; i >= 0; i--)
                        {
                            GraphicsPath p1, p2;
                            PointF junc;
                            if (IdsVectorGeometryService.TryCutPolyline(_parent._customStrokes[i], _cutPoints, out p1, out p2, out junc))
                            {
                                _parent.PushUndo("Cắt nét bút (Lasso Cut)");
                                _parent._customStrokes[i].Dispose();
                                _parent._customStrokes.RemoveAt(i);
                                _parent._customStrokes.Add(p1);
                                _parent._customStrokes.Add(p2);
                                cutSuccess = true;
                                cutJunction = junc;
                                break;
                            }
                        }

                        // 2. Cắt linh kiện: Cắt rời phần nét được khoanh hoặc rạch qua (bảo toàn toàn bộ thân chữ còn lại)
                        if (!cutSuccess)
                        {
                            // Ưu tiên linh kiện đang được chọn
                            GlyphRenderData targetItem = null;
                            if (!string.IsNullOrEmpty(_parent._selectedNodeId))
                            {
                                targetItem = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                                if (targetItem != null && targetItem.Transform != null && !targetItem.Transform.IsVisible) targetItem = null;
                            }

                            if (targetItem != null)
                            {
                                _parent.EnsureVectorPath(targetItem);
                                if (targetItem.VectorPath != null)
                                {
                                    GraphicsPath remainingComp, severed;
                                    PointF junc;
                                    if (IdsVectorGeometryService.TryCutCompoundPathByTrajectory(targetItem.VectorPath, _cutPoints, out remainingComp, out severed, out junc))
                                    {
                                        _parent.PushUndo("Cắt nét linh kiện");
                                        targetItem.VectorPath.Dispose();
                                        targetItem.VectorPath = remainingComp;
                                        if (remainingComp != null && remainingComp.PointCount > 0)
                                        {
                                            _parent.SetComponentCustomPath(targetItem.NodeId, remainingComp, targetItem.TargetRect, targetItem.Transform);
                                        }
                                        else
                                        {
                                            _parent.RemoveComponentCustomPath(targetItem.NodeId);
                                            targetItem.Transform.IsVisible = false;
                                            var node = _parent.FindNodeById(_parent._ast, targetItem.NodeId);
                                            if (node != null)
                                            {
                                                var t = node.Transform ?? new NodeCustomTransform();
                                                t.IsVisible = false;
                                                node.Transform = t;
                                                _parent._transformsMap[node.Id] = t;
                                            }
                                        }
                                        using (var m = new Matrix())
                                        {
                                            m.Translate(35f, -35f);
                                            severed.Transform(m);
                                        }
                                        _parent._customStrokes.Add(severed);
                                        _selectedCustomStrokeIndices.Clear();
                                        _selectedCustomStrokeIndices.Add(_parent._customStrokes.Count - 1);
                                        _selectedAnchorIndices.Clear();
                                        _activeAnchorIndex = -1;
                                        _parent._selectedAnchorIndex = -1;
                                        _parent._selectedNodeId = null;
                                        if (_parent._treeAst != null) _parent._treeAst.SelectedNode = null;
                                        if (_parent._lvLayers != null) _parent._lvLayers.SelectedIndices.Clear();
                                        cutSuccess = true;
                                        cutJunction = junc;
                                    }
                                }
                            }

                            // Nếu chưa cắt được, quét kiểm tra tất cả các linh kiện trên canvas
                            if (!cutSuccess)
                            {
                                foreach (var item in _parent._renderItems)
                                {
                                    if (item == targetItem || (item.Transform != null && !item.Transform.IsVisible)) continue;
                                    _parent.EnsureVectorPath(item);
                                    if (item.VectorPath == null) continue;

                                    GraphicsPath remainingComp, severed;
                                    PointF junc;
                                    if (IdsVectorGeometryService.TryCutCompoundPathByTrajectory(item.VectorPath, _cutPoints, out remainingComp, out severed, out junc))
                                    {
                                        _parent.PushUndo("Cắt nét linh kiện");
                                        item.VectorPath.Dispose();
                                        item.VectorPath = remainingComp;
                                        if (remainingComp != null && remainingComp.PointCount > 0)
                                        {
                                            _parent.SetComponentCustomPath(item.NodeId, remainingComp, item.TargetRect, item.Transform);
                                        }
                                        else
                                        {
                                            _parent.RemoveComponentCustomPath(item.NodeId);
                                            item.Transform.IsVisible = false;
                                            var node = _parent.FindNodeById(_parent._ast, item.NodeId);
                                            if (node != null)
                                            {
                                                var t = node.Transform ?? new NodeCustomTransform();
                                                t.IsVisible = false;
                                                node.Transform = t;
                                                _parent._transformsMap[node.Id] = t;
                                            }
                                        }
                                        using (var m = new Matrix())
                                        {
                                            m.Translate(35f, -35f);
                                            severed.Transform(m);
                                        }
                                        _parent._customStrokes.Add(severed);
                                        _selectedCustomStrokeIndices.Clear();
                                        _selectedCustomStrokeIndices.Add(_parent._customStrokes.Count - 1);
                                        _selectedAnchorIndices.Clear();
                                        _activeAnchorIndex = -1;
                                        _parent._selectedAnchorIndex = -1;
                                        _parent._selectedNodeId = null;
                                        if (_parent._treeAst != null) _parent._treeAst.SelectedNode = null;
                                        if (_parent._lvLayers != null) _parent._lvLayers.SelectedIndices.Clear();
                                        cutSuccess = true;
                                        cutJunction = junc;
                                        break;
                                    }
                                }
                            }
                        }

                        if (cutSuccess)
                        {
                            _hudMessage = string.Format("✂ Đã cắt rời phần nét thành công tại ({0:0}, {1:0})! Bấm Ghép nối (Ctrl+J) để Auto-Join", cutJunction.X, cutJunction.Y);
                            _parent.SetToolMode(EditorToolMode.Select);
                        }
                        else
                        {
                            _hudMessage = "✂ Vết cắt không giao cắt với nét vẽ nào";
                        }
                    }
                    _cutPoints.Clear();
                    Invalidate();
                    return;
                }

                // Kết thúc kéo Selection Box (Direct Selection Tool A)
                if (_isBoxSelecting)
                {
                    _isBoxSelecting = false;
                    Capture = false;

                    float boxW = Math.Abs(_boxSelectStart.X - _boxSelectCurrent.X);
                    float boxH = Math.Abs(_boxSelectStart.Y - _boxSelectCurrent.Y);

                    if (boxW >= 4 || boxH >= 4)
                    {
                        PointF p1 = ScreenToStage(_boxSelectStart, stageRect);
                        PointF p2 = ScreenToStage(_boxSelectCurrent, stageRect);
                        RectangleF stageBox = RectangleF.FromLTRB(
                            Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y),
                            Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y));

                        _parent.EnsureAllComponentVectorPaths();

                        GlyphRenderData bestItem = null;
                        int maxFound = 0;
                        List<int> bestAnchors = new List<int>();

                        var activeItem = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                        var searchList = new List<GlyphRenderData>();
                        if (activeItem != null && (activeItem.Transform == null || activeItem.Transform.IsVisible)) searchList.Add(activeItem);
                        foreach (var it in _parent._renderItems)
                        {
                            if (it != activeItem && (it.Transform == null || it.Transform.IsVisible))
                                searchList.Add(it);
                        }

                        foreach (var it in searchList)
                        {
                            var path = _parent.EnsureVectorPath(it);
                            if (path == null || path.PointCount == 0) continue;
                            var pts = path.PathPoints;
                            var found = new List<int>();
                            for (int i = 0; i < pts.Length; i++)
                            {
                                if (stageBox.Contains(pts[i])) found.Add(i);
                            }
                            if (found.Count > maxFound)
                            {
                                maxFound = found.Count;
                                bestItem = it;
                                bestAnchors = found;
                            }
                        }

                        bool isBoxShift = (ModifierKeys & Keys.Shift) == Keys.Shift;
                        if (!isBoxShift)
                        {
                            _selectedCustomStrokeIndices.Clear();
                        }

                        for (int k = 0; k < _parent._customStrokes.Count; k++)
                        {
                            var stroke = _parent._customStrokes[k];
                            var pts = stroke.PathPoints;
                            bool anyIn = false;
                            foreach (var pt in pts)
                            {
                                if (stageBox.Contains(pt)) { anyIn = true; break; }
                            }
                            if (anyIn) _selectedCustomStrokeIndices.Add(k);
                        }

                        if (bestItem != null)
                        {
                            if (!isBoxShift || _parent._selectedNodeId != bestItem.NodeId)
                            {
                                _parent._selectedNodeId = bestItem.NodeId;
                                if (!isBoxShift) _selectedAnchorIndices.Clear();
                            }

                            foreach (int idx in bestAnchors) _selectedAnchorIndices.Add(idx);
                            if (_selectedAnchorIndices.Count > 0)
                            {
                                _activeAnchorIndex = bestAnchors.Count > 0 ? bestAnchors[0] : -1;
                                _parent._selectedAnchorIndex = _activeAnchorIndex;
                            }
                            _parent.RebuildInspector();
                            _parent.SelectTreeNodeById(_parent._treeAst.Nodes, bestItem.NodeId);
                            _parent.RebuildLayersList();
                            _hudMessage = string.Format("Vùng chọn điểm neo (A): Đã chọn {0} điểm neo [{1}]", _selectedAnchorIndices.Count, bestItem.Char);
                        }
                        else if (_selectedCustomStrokeIndices.Count > 0)
                        {
                            if (!isBoxShift)
                            {
                                _selectedAnchorIndices.Clear();
                                _activeAnchorIndex = -1;
                                _parent._selectedAnchorIndex = -1;
                            _parent._selectedNodeId = null;
                            }
                            _hudMessage = string.Format("Vùng chọn điểm neo (A): Đã chọn {0} nét vẽ tự do", _selectedCustomStrokeIndices.Count);
                        }
                        else
                        {
                            if (!isBoxShift)
                            {
                                _selectedAnchorIndices.Clear();
                                _selectedCustomStrokeIndices.Clear();
                                _activeAnchorIndex = -1;
                                _parent._selectedAnchorIndex = -1;
                            _parent._selectedNodeId = null;
                                _parent._selectedNodeId = null;
                                _hudMessage = "Vùng chọn điểm neo (A): Không có điểm neo nào trong khung quét";
                            }
                        }
                    }
                    else
                    {
                        string hitComp = IdsGlyphRenderer.HitTestNodeId(e.Location, ClientRectangle, _parent._renderItems, _parent._zoom, _parent._panX, _parent._panY);
                        if (!string.IsNullOrEmpty(hitComp))
                        {
                            _parent._selectedNodeId = hitComp;
                            _selectedAnchorIndices.Clear();
                            _activeAnchorIndex = -1;
                            _parent.RebuildInspector();
                            _parent.SelectTreeNodeById(_parent._treeAst.Nodes, hitComp);
                            _parent.RebuildLayersList();
                        }
                        else
                        {
                            _selectedAnchorIndices.Clear();
                            _selectedCustomStrokeIndices.Clear();
                            _activeAnchorIndex = -1;
                            _parent._selectedAnchorIndex = -1;
                            _parent._selectedNodeId = null;
                        }
                    }
                    Invalidate();
                    return;
                }
                if (_isPanning)
                {
                    _isPanning = false;
                    Capture = false;
                    Cursor = (_parent._toolMode == EditorToolMode.Hand || (ModifierKeys & Keys.Space) == Keys.Space)
                        ? Cursors.Hand
                        : Cursors.Default;
                    Invalidate();
                }
                if (_isManipulating)
                {
                    if (_parent._selectedNodeIds.Count > 1)
                    {
                        foreach (string id in _parent._selectedNodeIds)
                        {
                            if (_parent._componentCustomPaths.ContainsKey(id))
                            {
                                var curItem = (_parent._renderItems != null) ? _parent._renderItems.Find(x => x.NodeId == id) : null;
                                if (curItem != null && curItem.VectorPath != null)
                                {
                                    var n = _parent.FindNodeById(_parent._ast, id);
                                    _parent.SetComponentCustomPath(id, curItem.VectorPath, curItem.TargetRect, (n != null && n.Transform != null) ? n.Transform : curItem.Transform);
                                }
                            }
                        }
                        _startMultiTransforms.Clear();
                        _startMultiCenters.Clear();
                        _startMultiBounds.Clear();
                        foreach (var p in _startMultiCustomPaths.Values) { try { p.Dispose(); } catch { } }
                        _startMultiCustomPaths.Clear();
                    }
                    else if (!string.IsNullOrEmpty(_parent._selectedNodeId) && _parent._componentCustomPaths.ContainsKey(_parent._selectedNodeId))
                    {
                        var curItem = (_parent._renderItems != null) ? _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId) : null;
                        if (curItem != null && curItem.VectorPath != null)
                        {
                            var node = _parent.FindNodeById(_parent._ast, _parent._selectedNodeId);
                            _parent.SetComponentCustomPath(_parent._selectedNodeId, curItem.VectorPath, curItem.TargetRect, (node != null && node.Transform != null) ? node.Transform : curItem.Transform);
                        }
                        if (_dragStartCustomVectorPath != null)
                        {
                            _dragStartCustomVectorPath.Dispose();
                            _dragStartCustomVectorPath = null;
                        }
                    }
                    _isManipulating = false;
                    _activeHandle = BoundingHandle.None;
                    _hudMessage = null;
                    _parent._activeSmartGuides.Clear();
                    Capture = false;
                    Cursor = Cursors.Default;
                    _parent.RebuildLayersList();
                    _parent.RebuildInspector();
                    _parent.UpdateLegibilityStrip();
                    Invalidate();
                }
                if (_isDraggingAnchor)
                {
                    _isDraggingAnchor = false;
                    _hudMessage = null;
                    Capture = false;
                    _parent.UpdateLegibilityStrip();
                    Invalidate();
                }
                if (_isTransformingSelection)
                {
                    _isTransformingSelection = false;
                    _activeSelectionHandle = BoundingHandle.None;
                    _hudMessage = null;
                    Capture = false;
                    _parent.RebuildLayersList();
                    _parent.RebuildInspector();
                    _parent.UpdateLegibilityStrip();
                    Invalidate();
                }
                Capture = false;
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                if (_parent._toolMode == EditorToolMode.Pen && _parent._activePenPoints.Count >= 2)
                {
                    CommitPenStroke();
                    return;
                }
                _parent.ZoomReset();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // 1. Base Stage & Glyphs
                IdsGlyphRenderer.RenderStage(
                    g,
                    ClientRectangle,
                    _parent._renderItems,
                    _parent._selectedNodeId,
                    _parent._gridType,
                    _parent._showBBoxes,
                    _parent._colorTheme,
                    _parent._zoom,
                    _parent._activeFontFamily,
                    _parent._panX,
                    _parent._panY
                );

                var stageRect = GetStageScreenRect();

                // 2. Custom Drawn Vector Strokes
                DrawCustomStrokes(g, stageRect);

                // 3. Active Pen Tool Rubber-Band & Nodes
                if (_parent._toolMode == EditorToolMode.Pen)
                {
                    DrawPenPreview(g, stageRect);
                }

                // 4. Smart Guides
                if (_parent._activeSmartGuides.Count > 0)
                {
                    DrawSmartGuides(g, stageRect);
                }

                // 5. Direct Selection & Lasso Vector Anchor Points
                if ((_parent._toolMode == EditorToolMode.DirectSelect || _parent._toolMode == EditorToolMode.Lasso) &&
                    (!string.IsNullOrEmpty(_parent._selectedNodeId) || _selectedCustomStrokeIndices.Count > 0))
                {
                    DrawDirectSelectionAnchors(g, stageRect);
                }

                // 6. Illustrator Bounding Box & 9 Handles
                if (_parent._toolMode == EditorToolMode.Select && (!string.IsNullOrEmpty(_parent._selectedNodeId) || _parent._selectedNodeIds.Count > 0))
                {
                    DrawBoundingBox(g, stageRect);
                }

                // Lasso Selection Loop
                if (_isLassoing && _lassoPoints.Count >= 2)
                {
                    DrawLassoLoop(g, stageRect);
                }

                // Lasso Cutting Line
                if (_isCutting && _cutPoints.Count >= 2)
                {
                    DrawCuttingLine(g, stageRect);
                }

                // Selection Marquee Box (Direct Selection Tool A)
                if (_isBoxSelecting)
                {
                    DrawSelectionMarqueeBox(g);
                }

                // Selection Transformer (Photoshop / Illustrator Style)
                if (HasActiveSelectionTransformer)
                {
                    DrawSelectionTransformer(g, stageRect);
                }

                // Auto-Join Pulse Effect
                if (_pulseRadius > 0f && !_pulseCenter.IsEmpty)
                {
                    DrawAutoJoinPulse(g, stageRect);
                }

                // 7. Floating HUD Badge (Coordinates, Dimensions, Angle)
                if (!string.IsNullOrEmpty(_hudMessage))
                {
                    DrawHudBadge(g);
                }
            }

            private void DrawBoundingBox(Graphics g, RectangleF stageRect)
            {
                if (_parent._selectedNodeIds.Count > 1)
                {
                    DrawMultiSelectionBoundingBox(g, stageRect);
                    return;
                }

                var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                if (item == null || item.Transform == null || !item.Transform.IsVisible) return;

                float scale = stageRect.Width / 1000f;
                PointF centerStage = new PointF(item.StageBounds.X + item.StageBounds.Width / 2f, item.StageBounds.Y + item.StageBounds.Height / 2f);
                PointF centerScreen = StageToScreen(centerStage, stageRect);

                float boxW = item.StageBounds.Width * scale;
                float boxH = item.StageBounds.Height * scale;
                float halfW = boxW / 2f;
                float halfH = boxH / 2f;
                float stemLen = 22f;

                var state = g.Save();
                g.TranslateTransform(centerScreen.X, centerScreen.Y);
                if (item.Transform != null && item.Transform.Rotation != 0)
                {
                    g.RotateTransform(item.Transform.Rotation);
                }

                // Bounding Box outline: Cyan (#0ea5e9)
                using (var boxPen = new Pen(Color.FromArgb(14, 165, 233), 1.5f))
                {
                    g.DrawRectangle(boxPen, -halfW, -halfH, boxW, boxH);
                }

                // Stem line to rotation handle
                using (var stemPen = new Pen(Color.FromArgb(14, 165, 233), 1.2f))
                {
                    g.DrawLine(stemPen, 0, -halfH, 0, -halfH - stemLen);
                }

                // Rotation handle (White circle with cyan border)
                float rotR = 4.5f;
                using (var brush = new SolidBrush(Color.White))
                using (var pen = new Pen(Color.FromArgb(14, 165, 233), 1.8f))
                {
                    g.FillEllipse(brush, -rotR, -halfH - stemLen - rotR, rotR * 2, rotR * 2);
                    g.DrawEllipse(pen, -rotR, -halfH - stemLen - rotR, rotR * 2, rotR * 2);
                }

                // 8 Resize handles (White squares with cyan border)
                PointF[] handlePositions = new PointF[]
                {
                    new PointF(-halfW, -halfH),
                    new PointF(0, -halfH),
                    new PointF(halfW, -halfH),
                    new PointF(halfW, 0),
                    new PointF(halfW, halfH),
                    new PointF(0, halfH),
                    new PointF(-halfW, halfH),
                    new PointF(-halfW, 0)
                };

                float hSize = 7f;
                float hHalf = hSize / 2f;
                using (var fillBrush = new SolidBrush(Color.White))
                using (var borderPen = new Pen(Color.FromArgb(14, 165, 233), 1.5f))
                {
                    foreach (var hp in handlePositions)
                    {
                        g.FillRectangle(fillBrush, hp.X - hHalf, hp.Y - hHalf, hSize, hSize);
                        g.DrawRectangle(borderPen, hp.X - hHalf, hp.Y - hHalf, hSize, hSize);
                    }
                }

                g.Restore(state);
            }

            private void DrawSmartGuides(Graphics g, RectangleF stageRect)
            {
                using (var guidePen = new Pen(Color.FromArgb(236, 72, 153), 1.2f))
                using (var textBrush = new SolidBrush(Color.White))
                using (var badgeBrush = new SolidBrush(Color.FromArgb(219, 39, 119)))
                using (var font = new Font("Segoe UI", 7.5F, FontStyle.Bold))
                {
                    guidePen.DashStyle = DashStyle.Dash;
                    guidePen.DashPattern = new float[] { 4, 3 };

                    foreach (var line in _parent._activeSmartGuides)
                    {
                        PointF p1 = StageToScreen(new PointF(line.X1, line.Y1), stageRect);
                        PointF p2 = StageToScreen(new PointF(line.X2, line.Y2), stageRect);
                        g.DrawLine(guidePen, p1, p2);

                        if (!string.IsNullOrEmpty(line.Label))
                        {
                            PointF mid = new PointF((p1.X + p2.X) / 2f, (p1.Y + p2.Y) / 2f);
                            var sz = g.MeasureString(line.Label, font);
                            RectangleF badgeRect = new RectangleF(mid.X - sz.Width / 2f - 4, mid.Y - sz.Height / 2f - 2, sz.Width + 8, sz.Height + 4);
                            g.FillRectangle(badgeBrush, badgeRect);
                            g.DrawString(line.Label, font, textBrush, badgeRect.X + 4, badgeRect.Y + 2);
                        }
                    }
                }
            }

            private void DrawCustomStrokes(Graphics g, RectangleF stageRect)
            {
                if (_parent._customStrokes.Count == 0) return;
                float scale = stageRect.Width / 1000f;
                var state = g.Save();
                g.TranslateTransform(stageRect.X, stageRect.Y);
                g.ScaleTransform(scale, scale);

                using (var strokePen = new Pen(Color.FromArgb(30, 41, 59), 3.5f / scale))
                using (var selGlowPen = new Pen(Color.FromArgb(120, 37, 99, 235), 7.0f / scale))
                {
                    strokePen.StartCap = LineCap.Round;
                    strokePen.EndCap = LineCap.Round;
                    strokePen.LineJoin = LineJoin.Round;
                    selGlowPen.StartCap = LineCap.Round;
                    selGlowPen.EndCap = LineCap.Round;
                    selGlowPen.LineJoin = LineJoin.Round;

                    for (int k = 0; k < _parent._customStrokes.Count; k++)
                    {
                        var path = _parent._customStrokes[k];
                        if (path == null || path.PointCount < 2) continue;

                        using (var iter = new GraphicsPathIterator(path))
                        {
                            bool isClosed;
                            using (var temp = new GraphicsPath())
                            {
                                iter.NextSubpath(temp, out isClosed);
                                if (isClosed)
                                {
                                    using (var fillBr = new SolidBrush(Color.FromArgb(30, 41, 59)))
                                    {
                                        g.FillPath(fillBr, path);
                                    }
                                }
                            }
                        }

                        if (_selectedCustomStrokeIndices.Contains(k))
                        {
                            g.DrawPath(selGlowPen, path);
                        }
                        g.DrawPath(strokePen, path);
                    }
                }
                g.Restore(state);
            }

            private void DrawPenPreview(Graphics g, RectangleF stageRect)
            {
                if (_parent._activePenPoints.Count == 0) return;

                using (var penLine = new Pen(Color.FromArgb(37, 99, 235), 2f))
                using (var previewPen = new Pen(Color.FromArgb(147, 197, 253), 1.5f))
                using (var nodeBrush = new SolidBrush(Color.White))
                using (var nodeBorder = new Pen(Color.FromArgb(37, 99, 235), 1.5f))
                {
                    previewPen.DashStyle = DashStyle.Dash;

                    for (int i = 0; i < _parent._activePenPoints.Count - 1; i++)
                    {
                        PointF p1 = StageToScreen(_parent._activePenPoints[i], stageRect);
                        PointF p2 = StageToScreen(_parent._activePenPoints[i + 1], stageRect);
                        g.DrawLine(penLine, p1, p2);
                    }

                    Point penCursor = _parent._penCursorPt;
                    if (!penCursor.IsEmpty && _parent._activePenPoints.Count > 0)
                    {
                        PointF last = StageToScreen(_parent._activePenPoints[_parent._activePenPoints.Count - 1], stageRect);
                        g.DrawLine(previewPen, last, (PointF)penCursor);
                    }

                    float r = 3.5f;
                    foreach (var p in _parent._activePenPoints)
                    {
                        PointF sp = StageToScreen(p, stageRect);
                        g.FillEllipse(nodeBrush, sp.X - r, sp.Y - r, r * 2, r * 2);
                        g.DrawEllipse(nodeBorder, sp.X - r, sp.Y - r, r * 2, r * 2);
                    }
                }
            }

            private void DrawDirectSelectionAnchors(Graphics g, RectangleF stageRect)
            {
                var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                if (item != null && (item.Transform == null || item.Transform.IsVisible))
                {
                    var path = _parent.EnsureVectorPath(item);
                    if (path != null && path.PointCount > 0)
                    {
                        var pts = path.PathPoints;
                        float boxSize = 6.5f;
                        float half = boxSize / 2f;

                        using (var unselBrush = new SolidBrush(Color.White))
                        using (var selBrush = new SolidBrush(Color.FromArgb(37, 99, 235)))
                        using (var selBorderPen = new Pen(Color.FromArgb(29, 78, 216), 1.8f))
                        using (var unselBorderPen = new Pen(Color.FromArgb(100, 116, 139), 1.2f))
                        using (var glowPen = new Pen(Color.FromArgb(70, 59, 130, 246), 4f))
                        {
                            for (int i = 0; i < pts.Length; i++)
                            {
                                PointF sp = StageToScreen(pts[i], stageRect);
                                bool isSel = (_selectedAnchorIndices.Contains(i) || i == _activeAnchorIndex);
                                if (isSel)
                                {
                                    g.DrawRectangle(glowPen, sp.X - half - 1, sp.Y - half - 1, boxSize + 2, boxSize + 2);
                                    g.FillRectangle(selBrush, sp.X - half, sp.Y - half, boxSize, boxSize);
                                    g.DrawRectangle(selBorderPen, sp.X - half, sp.Y - half, boxSize, boxSize);
                                }
                                else
                                {
                                    g.FillRectangle(unselBrush, sp.X - half, sp.Y - half, boxSize, boxSize);
                                    g.DrawRectangle(unselBorderPen, sp.X - half, sp.Y - half, boxSize, boxSize);
                                }
                            }
                        }
                    }
                }

                if (_selectedCustomStrokeIndices.Count > 0)
                {
                    float boxSize = 6.5f;
                    float half = boxSize / 2f;
                    using (var strokeBrush = new SolidBrush(Color.FromArgb(234, 88, 12)))
                    using (var strokeBorder = new Pen(Color.FromArgb(194, 65, 12), 1.5f))
                    {
                        foreach (int sIdx in _selectedCustomStrokeIndices)
                        {
                            if (sIdx >= 0 && sIdx < _parent._customStrokes.Count)
                            {
                                var strokePath = _parent._customStrokes[sIdx];
                                var pts = strokePath.PathPoints;
                                for (int i = 0; i < pts.Length; i++)
                                {
                                    PointF sp = StageToScreen(pts[i], stageRect);
                                    g.FillRectangle(strokeBrush, sp.X - half, sp.Y - half, boxSize, boxSize);
                                    g.DrawRectangle(strokeBorder, sp.X - half, sp.Y - half, boxSize, boxSize);
                                }
                            }
                        }
                    }
                }
            }

            private void DrawSelectionTransformer(Graphics g, RectangleF stageRect)
            {
                if (!HasActiveSelectionTransformer) return;

                RectangleF stageBox = GetSelectedRegionStageBounds();
                if (stageBox.IsEmpty) return;

                float scale = stageRect.Width / 1000f;
                PointF centerStage = new PointF(stageBox.X + stageBox.Width / 2f, stageBox.Y + stageBox.Height / 2f);
                PointF centerScreen = StageToScreen(centerStage, stageRect);

                float boxW = stageBox.Width * scale;
                float boxH = stageBox.Height * scale;
                float halfW = boxW / 2f;
                float halfH = boxH / 2f;
                float stemLen = 24f;

                var state = g.Save();
                g.TranslateTransform(centerScreen.X, centerScreen.Y);
                if (_selectionTransformRotation != 0f)
                {
                    g.RotateTransform(_selectionTransformRotation);
                }

                // 1. Bounding Box fill & dashed outline
                using (var fillBrush = new SolidBrush(Color.FromArgb(20, 37, 99, 235)))
                using (var boxPen = new Pen(Color.FromArgb(37, 99, 235), 1.5f) { DashStyle = DashStyle.Dash })
                {
                    g.FillRectangle(fillBrush, -halfW, -halfH, boxW, boxH);
                    g.DrawRectangle(boxPen, -halfW, -halfH, boxW, boxH);
                }

                // 2. Stem line to rotation handle
                using (var stemPen = new Pen(Color.FromArgb(37, 99, 235), 1.3f))
                {
                    g.DrawLine(stemPen, 0, -halfH, 0, -halfH - stemLen);
                }

                // 3. Rotation handle (White circle with blue border)
                float rotR = 5f;
                using (var brush = new SolidBrush(Color.White))
                using (var pen = new Pen(Color.FromArgb(37, 99, 235), 1.8f))
                {
                    g.FillEllipse(brush, -rotR, -halfH - stemLen - rotR, rotR * 2, rotR * 2);
                    g.DrawEllipse(pen, -rotR, -halfH - stemLen - rotR, rotR * 2, rotR * 2);
                }

                // 4. Center crosshair (+)
                using (var crossPen = new Pen(Color.FromArgb(37, 99, 235), 1.2f))
                {
                    g.DrawLine(crossPen, -5f, 0, 5f, 0);
                    g.DrawLine(crossPen, 0, -5f, 0, 5f);
                }

                // 5. 8 Resize handles (White squares with crisp blue border)
                PointF[] handlePositions = new PointF[]
                {
                    new PointF(-halfW, -halfH),
                    new PointF(0, -halfH),
                    new PointF(halfW, -halfH),
                    new PointF(halfW, 0),
                    new PointF(halfW, halfH),
                    new PointF(0, halfH),
                    new PointF(-halfW, halfH),
                    new PointF(-halfW, 0)
                };

                float hSize = 7.5f;
                float hHalf = hSize / 2f;
                using (var fillBrush = new SolidBrush(Color.White))
                using (var borderPen = new Pen(Color.FromArgb(37, 99, 235), 1.6f))
                {
                    foreach (var hp in handlePositions)
                    {
                        g.FillRectangle(fillBrush, hp.X - hHalf, hp.Y - hHalf, hSize, hSize);
                        g.DrawRectangle(borderPen, hp.X - hHalf, hp.Y - hHalf, hSize, hSize);
                    }
                }

                g.Restore(state);
            }

            private void DrawSelectionMarqueeBox(Graphics g)
            {
                float x = Math.Min(_boxSelectStart.X, _boxSelectCurrent.X);
                float y = Math.Min(_boxSelectStart.Y, _boxSelectCurrent.Y);
                float w = Math.Abs(_boxSelectStart.X - _boxSelectCurrent.X);
                float h = Math.Abs(_boxSelectStart.Y - _boxSelectCurrent.Y);
                if (w < 1 || h < 1) return;

                using (var fillBrush = new SolidBrush(Color.FromArgb(35, 37, 99, 235)))
                using (var borderPen = new Pen(Color.FromArgb(37, 99, 235), 1.2f))
                {
                    borderPen.DashStyle = DashStyle.Dash;
                    g.FillRectangle(fillBrush, x, y, w, h);
                    g.DrawRectangle(borderPen, x, y, w, h);
                }
            }

            private void DrawLassoLoop(Graphics g, RectangleF stageRect)
            {
                if (_lassoPoints.Count < 2) return;
                PointF[] screenPts = new PointF[_lassoPoints.Count];
                for (int i = 0; i < _lassoPoints.Count; i++)
                {
                    screenPts[i] = StageToScreen(_lassoPoints[i], stageRect);
                }

                if (_lassoPoints.Count >= 3)
                {
                    using (var fillBrush = new SolidBrush(Color.FromArgb(40, 99, 102, 241)))
                    {
                        g.FillPolygon(fillBrush, screenPts);
                    }
                }

                using (var borderPen = new Pen(Color.FromArgb(99, 102, 241), 1.8f))
                {
                    borderPen.DashStyle = DashStyle.Dash;
                    if (_lassoPoints.Count >= 3)
                    {
                        g.DrawPolygon(borderPen, screenPts);
                    }
                    else
                    {
                        g.DrawLines(borderPen, screenPts);
                    }
                }
            }

            private void DrawCuttingLine(Graphics g, RectangleF stageRect)
            {
                if (_cutPoints.Count < 2) return;
                PointF[] screenPts = new PointF[_cutPoints.Count];
                for (int i = 0; i < _cutPoints.Count; i++)
                {
                    screenPts[i] = StageToScreen(_cutPoints[i], stageRect);
                }

                using (var cutPen = new Pen(Color.FromArgb(249, 115, 22), 2.2f))
                using (var dotBrush = new SolidBrush(Color.FromArgb(234, 88, 12)))
                {
                    cutPen.DashStyle = DashStyle.DashDot;
                    g.DrawLines(cutPen, screenPts);

                    float r = 4f;
                    g.FillEllipse(dotBrush, screenPts[0].X - r, screenPts[0].Y - r, r * 2, r * 2);
                    g.FillEllipse(dotBrush, screenPts[screenPts.Length - 1].X - r, screenPts[screenPts.Length - 1].Y - r, r * 2, r * 2);
                }
            }

            private void DrawAutoJoinPulse(Graphics g, RectangleF stageRect)
            {
                PointF screenPt = StageToScreen(_pulseCenter, stageRect);
                float scale = stageRect.Width / 1000f;
                float r = _pulseRadius * scale;
                int alpha = Math.Max(0, (int)(255 * (1f - _pulseRadius / 35f)));

                using (var pulseBrush = new SolidBrush(Color.FromArgb(alpha / 3, 34, 197, 94)))
                using (var pulsePen = new Pen(Color.FromArgb(alpha, 34, 197, 94), 2.5f))
                {
                    g.FillEllipse(pulseBrush, screenPt.X - r, screenPt.Y - r, r * 2, r * 2);
                    g.DrawEllipse(pulsePen, screenPt.X - r, screenPt.Y - r, r * 2, r * 2);
                }
            }

            private static float DistSq(PointF a, PointF b)
            {
                float dx = a.X - b.X;
                float dy = a.Y - b.Y;
                return dx * dx + dy * dy;
            }

            private void DrawHudBadge(Graphics g)
            {
                using (var bgBrush = new SolidBrush(Color.FromArgb(230, 15, 23, 42)))
                using (var textBrush = new SolidBrush(Color.White))
                using (var font = new Font("Segoe UI", 9F, FontStyle.Bold))
                using (var borderPen = new Pen(Color.FromArgb(56, 189, 248), 1f))
                {
                    var sz = g.MeasureString(_hudMessage, font);
                    float badgeW = sz.Width + 16;
                    float badgeH = sz.Height + 10;
                    float bx = 16f;
                    float by = 46f;
                    RectangleF rect = new RectangleF(bx, by, badgeW, badgeH);

                    g.FillRectangle(bgBrush, rect);
                    g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);
                    g.DrawString(_hudMessage, font, textBrush, bx + 8, by + 5);
                }
            }
        }
    }

    public class SmoothTrackBar : Control
    {
        private int _min = 0;
        private int _max = 100;
        private int _val = 0;
        private bool _isDragging = false;
        private bool _isHovered = false;

        public event EventHandler ValueChanged;

        public SmoothTrackBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Height = 28;
            Cursor = Cursors.Hand;
        }

        public int Minimum
        {
            get { return _min; }
            set
            {
                _min = value;
                if (_val < _min) Value = _min;
                Invalidate();
            }
        }

        public int Maximum
        {
            get { return _max; }
            set
            {
                _max = value;
                if (_val > _max) Value = _max;
                Invalidate();
            }
        }

        public int Value
        {
            get { return _val; }
            set
            {
                int clamped = Math.Max(_min, Math.Min(_max, value));
                if (_val != clamped)
                {
                    _val = clamped;
                    Invalidate();
                    var handler = ValueChanged;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
            }
        }

        public int TickFrequency { get; set; }

        private void UpdateFromMouse(int mouseX)
        {
            int thumbRadius = 7;
            int trackLeft = thumbRadius + 6;
            int trackRight = Width - thumbRadius - 6;
            int trackW = trackRight - trackLeft;
            if (trackW <= 0) return;

            float ratio = (float)(mouseX - trackLeft) / trackW;
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            int newVal = _min + (int)Math.Round(ratio * (_max - _min));
            Value = newVal;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                Capture = true;
                Focus();
                UpdateFromMouse(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging && e.Button == MouseButtons.Left)
            {
                UpdateFromMouse(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                Capture = false;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = Math.Max(1, (_max - _min) / 50);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down)
            {
                Value = _val - step;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up)
            {
                Value = _val + step;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Home)
            {
                Value = _min;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.End)
            {
                Value = _max;
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int thumbRadius = 7;
            int trackLeft = thumbRadius + 6;
            int trackRight = Width - thumbRadius - 6;
            int trackW = trackRight - trackLeft;
            int centerY = Height / 2;

            if (trackW <= 0) return;

            float ratio = (_max > _min) ? (float)(_val - _min) / (_max - _min) : 0f;
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            int thumbX = trackLeft + (int)Math.Round(ratio * trackW);

            // Draw track background (unfilled)
            using (var penTrack = new Pen(Color.FromArgb(226, 232, 240), 4f))
            {
                penTrack.StartCap = LineCap.Round;
                penTrack.EndCap = LineCap.Round;
                g.DrawLine(penTrack, trackLeft, centerY, trackRight, centerY);
            }

            // Draw active track (filled)
            int fillStartX = trackLeft;
            if (_min < 0 && _max > 0)
            {
                float zeroRatio = (float)(0 - _min) / (_max - _min);
                fillStartX = trackLeft + (int)Math.Round(zeroRatio * trackW);
            }

            using (var penActive = new Pen(Color.FromArgb(37, 99, 235), 4f)) // Windows Accent Blue #2563EB
            {
                penActive.StartCap = LineCap.Round;
                penActive.EndCap = LineCap.Round;
                if (Math.Abs(thumbX - fillStartX) > 1)
                {
                    g.DrawLine(penActive, Math.Min(fillStartX, thumbX), centerY, Math.Max(fillStartX, thumbX), centerY);
                }
            }

            // Draw center tick if range spans negative to positive
            if (_min < 0 && _max > 0)
            {
                float zeroRatio = (float)(0 - _min) / (_max - _min);
                int zeroX = trackLeft + (int)Math.Round(zeroRatio * trackW);
                using (var penCenterTick = new Pen(Color.FromArgb(148, 163, 184), 2f))
                {
                    g.DrawLine(penCenterTick, zeroX, centerY - 5, zeroX, centerY + 5);
                }
            }

            // Draw Thumb
            int d = thumbRadius * 2;
            var thumbRect = new Rectangle(thumbX - thumbRadius, centerY - thumbRadius, d, d);

            // Subtle drop shadow
            using (var brushShadow = new SolidBrush(Color.FromArgb(35, 0, 0, 0)))
            {
                g.FillEllipse(brushShadow, thumbRect.X + 1, thumbRect.Y + 1, d, d);
            }

            // Thumb body (White with blue border)
            Color thumbBorder = (_isDragging || _isHovered) ? Color.FromArgb(29, 78, 216) : Color.FromArgb(37, 99, 235);
            using (var brushThumb = new SolidBrush(Color.White))
            {
                g.FillEllipse(brushThumb, thumbRect);
            }
            using (var penThumb = new Pen(thumbBorder, 2f))
            {
                g.DrawEllipse(penThumb, thumbRect);
            }

            // Inner dot when hovered or dragged
            if (_isDragging || _isHovered)
            {
                int innerR = 3;
                using (var brushInner = new SolidBrush(Color.FromArgb(37, 99, 235)))
                {
                    g.FillEllipse(brushInner, thumbX - innerR, centerY - innerR, innerR * 2, innerR * 2);
                }
            }
        }
    }
}
