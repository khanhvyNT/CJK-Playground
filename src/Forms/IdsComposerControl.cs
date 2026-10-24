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
        private Dictionary<string, NodeCustomTransform> _transformsMap = new Dictionary<string, NodeCustomTransform>();
        private Dictionary<string, NodeCustomTransform> _backupTransformsMap = new Dictionary<string, NodeCustomTransform>();
        private List<string> _lastBeautifyReport = new List<string>();
        private IdsNode _ast;
        private List<GlyphRenderData> _renderItems = new List<GlyphRenderData>();

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
        private Label _lblPresetName;
        private Button _btnBeautify;
        private Button _btnUndoBeautify;
        private Button _btnOpenPresets;
        private Button _btnSaveDict;
        private Button _btnOpenDict;
        private Button _btnExport;
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

        private void ApplyTransformAndRedraw()
        {
            _renderItems = IdsLayoutEngine.ComputeLayout(_ast);
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
            }
        }

        public void UpdateToolButtons()
        {
            Button[] toolBtns = { _btnToolSelect, _btnToolDirectSelect, _btnToolPen, _btnToolHand, _btnToolZoom };
            EditorToolMode[] modes = { EditorToolMode.Select, EditorToolMode.DirectSelect, EditorToolMode.Pen, EditorToolMode.Hand, EditorToolMode.Zoom };

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
                    if (item.NodeId == _selectedNodeId)
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
            if (string.IsNullOrEmpty(_selectedNodeId) || _ast == null) return;
            var node = FindNodeById(_ast, _selectedNodeId);
            if (node != null && !node.IsOperator)
            {
                var t = node.Transform ?? new NodeCustomTransform();
                t.IsVisible = false;
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
                var t = sel.Transform ?? new NodeCustomTransform();
                int dx = 0;
                int dy = 0;

                if (keyCode == Keys.Left) dx = -step;
                else if (keyCode == Keys.Right) dx = step;
                else if (keyCode == Keys.Up) dy = -step;
                else if (keyCode == Keys.Down) dy = step;
                else return false;

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

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Control focused = GetFocusedControl();
            if (!(focused is TextBoxBase) && !(focused is TreeView))
            {
                Keys code = keyData & Keys.KeyCode;
                if ((keyData & Keys.Control) == Keys.Control)
                {
                    if (code == Keys.C) { ContextCopy(); return true; }
                    if (code == Keys.V) { ContextPaste(); return true; }
                    if (code == Keys.D) { ContextDuplicate(); return true; }
                }
                else
                {
                    if (code == Keys.Q) { SetToolMode(EditorToolMode.Select); return true; }
                    if (code == Keys.V || code == Keys.H) { SetToolMode(EditorToolMode.Hand); return true; }
                    if (code == Keys.A) { SetToolMode(EditorToolMode.DirectSelect); return true; }
                    if (code == Keys.P) { SetToolMode(EditorToolMode.Pen); return true; }
                    if (code == Keys.Z) { SetToolMode(EditorToolMode.Zoom); return true; }
                    if (code == Keys.Delete) { ContextDelete(); return true; }
                    if (code == Keys.Escape)
                    {
                        if (_toolMode == EditorToolMode.Pen && _activePenPoints.Count > 0)
                        {
                            _activePenPoints.Clear();
                            if (_canvasSurface != null) _canvasSurface.Invalidate();
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

            // 1. Studio Header Toolbar (Native Windows Style)
            var topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(8, 6, 8, 6),
                BackColor = Color.FromArgb(245, 246, 248)
            };

            var topBarBorder = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(218, 222, 229)
            };
            topBar.Controls.Add(topBarBorder);

            var topFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false
            };

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

            _btnOpenPresets = CreateHeaderButton(isVi ? "📖 Mẫu chữ sẵn" : "📖 Presets", OnOpenPresets);
            _btnSaveDict = CreateHeaderButton(isVi ? "💾 Lưu từ điển" : "💾 Save Glyph", OnOpenSaveDialog);
            _btnOpenDict = CreateHeaderButton(isVi ? "📚 Sổ bộ PUA" : "📚 PUA Registry", OnOpenDictDialog);
            _btnExport = CreateHeaderButton(isVi ? "📤 Xuất SVG / PNG" : "📤 Export SVG/PNG", OnOpenExportDialog, Color.FromArgb(0, 102, 204));

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

            topFlow.Controls.AddRange(new Control[] {
                _lblPresetName, _btnBeautify, _btnUndoBeautify, _btnOpenPresets, _btnSaveDict, _btnOpenDict, _btnExport,
                sep1, _lblGrid, _cbGrid, _lblTheme, _cbTheme, sep2,
                _chkBBoxes, _chkSmartRadicals, _chkAspectGuard
            });
            topBar.Controls.Add(topFlow);

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
                            pfc.AddFontFile(ofd.FileName);
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
                    _canvasSurface.Invalidate();
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
                MultiSelect = false,
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
                if (_lvLayers.SelectedItems.Count > 0)
                {
                    string id = _lvLayers.SelectedItems[0].Tag as string;
                    if (!string.IsNullOrEmpty(id) && id != _selectedNodeId)
                    {
                        _selectedNodeId = id;
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

            toolsFlow.Controls.AddRange(new Control[] {
                _btnToolSelect, _btnToolDirectSelect, _btnToolPen, _btnToolHand, _btnToolZoom,
                sepTools, _btnSnapGrid, _btnSmartGuides
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

            _pnlCanvasContainer.Controls.Add(_canvasSurface);
            _pnlCanvasContainer.Controls.Add(pnlToolbar);
            pnlToolbar.BringToFront();
            _canvasSurface.SendToBack();

            centerPnl.Controls.Add(_pnlCanvasContainer);
            _pnlCanvasContainer.SendToBack();
            kbPnl.BringToFront();
            _pnlLegibility.BringToFront();

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
            Controls.Add(topBar);

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

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            SafeAdjustSplitters();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            SafeAdjustSplitters();
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
                Margin = new Padding(2, 4, 4, 0),
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
                    -200, 200, t.OffsetX,
                    val => { t.OffsetX = val; _transformsMap[sel.Id] = t; ApplyTransformAndRedraw(); },
                    out _trackOffsetX, out _lblOffsetX));

                rows.Add(CreateSliderGroup(
                    isVi ? "Dịch trục Y (Dọc, px):" : "Offset Y (px):",
                    -200, 200, t.OffsetY,
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

                var bmp = IdsGlyphRenderer.RenderHighResBitmap(_renderItems, _activeFontFamily, sz, themeCfg.InkColor, themeCfg.BgColor);
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

            // Direct selection state (Vector Anchor Points)
            private bool _isDraggingAnchor = false;
            private int _activeAnchorIndex = -1;
            private PointF _anchorDragStartStage;

            public CanvasSurface(IdsComposerControl parent)
            {
                _parent = parent;
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
                Cursor = Cursors.Default;
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
                if (item == null || item.VectorPath == null) return -1;
                var pts = item.VectorPath.PathPoints;
                float thresholdSq = 49f;

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
                if (e.Modifiers == Keys.None)
                {
                    if (e.KeyCode == Keys.Q) { _parent.SetToolMode(EditorToolMode.Select); e.Handled = true; return; }
                    if (e.KeyCode == Keys.V || e.KeyCode == Keys.H) { _parent.SetToolMode(EditorToolMode.Hand); e.Handled = true; return; }
                    if (e.KeyCode == Keys.A) { _parent.SetToolMode(EditorToolMode.DirectSelect); e.Handled = true; return; }
                    if (e.KeyCode == Keys.P) { _parent.SetToolMode(EditorToolMode.Pen); e.Handled = true; return; }
                    if (e.KeyCode == Keys.Z) { _parent.SetToolMode(EditorToolMode.Zoom); e.Handled = true; return; }
                }
                if (_parent._toolMode == EditorToolMode.Pen && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return))
                {
                    CommitPenStroke();
                    e.Handled = true;
                    return;
                }
                if (_parent._toolMode == EditorToolMode.Pen && e.KeyCode == Keys.Escape)
                {
                    _parent._activePenPoints.Clear();
                    Invalidate();
                    e.Handled = true;
                    return;
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
                    var path = new GraphicsPath();
                    path.AddLines(_parent._activePenPoints.ToArray());
                    _parent._customStrokes.Add(path);
                    _parent._activePenPoints.Clear();
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

                    // Direct Selection tool (Anchor Points)
                    if (_parent._toolMode == EditorToolMode.DirectSelect)
                    {
                        var selItem = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                        int anchorIdx = (selItem != null) ? HitTestAnchorPoint(e.Location, selItem, stageRect) : -1;
                        if (anchorIdx >= 0)
                        {
                            _isDraggingAnchor = true;
                            _activeAnchorIndex = anchorIdx;
                            _parent._selectedAnchorIndex = anchorIdx;
                            _anchorDragStartStage = ScreenToStage(e.Location, stageRect);
                            Capture = true;
                            Invalidate();
                            return;
                        }

                        // Otherwise test component hit
                        string hitComp = IdsGlyphRenderer.HitTestNodeId(e.Location, ClientRectangle, _parent._renderItems, _parent._zoom, _parent._panX, _parent._panY);
                        if (!string.IsNullOrEmpty(hitComp))
                        {
                            _parent._selectedNodeId = hitComp;
                            _activeAnchorIndex = -1;
                            _parent._selectedAnchorIndex = -1;
                            _parent.RebuildInspector();
                            _parent.SelectTreeNodeById(_parent._treeAst.Nodes, hitComp);
                            _parent.RebuildLayersList();
                            Invalidate();
                        }
                        return;
                    }

                    // Selection tool (V)
                    if (_parent._toolMode == EditorToolMode.Select)
                    {
                        // Check handles on current selection
                        if (!string.IsNullOrEmpty(_parent._selectedNodeId))
                        {
                            var selItem = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                            if (selItem != null && (selItem.Transform == null || !selItem.Transform.IsLocked))
                            {
                                BoundingHandle handle = HitTestBoundingHandles(e.Location, selItem, stageRect);
                                if (handle != BoundingHandle.None)
                                {
                                    _isManipulating = true;
                                    _activeHandle = handle;
                                    _dragStartMouseStage = ScreenToStage(e.Location, stageRect);
                                    _startTransform = selItem.Transform != null ? selItem.Transform.Clone() : new NodeCustomTransform();
                                    _startStageBounds = selItem.StageBounds;
                                    _startCenter = new PointF(_startStageBounds.X + _startStageBounds.Width / 2f, _startStageBounds.Y + _startStageBounds.Height / 2f);
                                    _startAngleDeg = (float)(Math.Atan2(_dragStartMouseStage.Y - _startCenter.Y, _dragStartMouseStage.X - _startCenter.X) * 180.0 / Math.PI);
                                    Capture = true;
                                    return;
                                }
                            }
                        }

                        // Test click on another component
                        string hit = IdsGlyphRenderer.HitTestNodeId(e.Location, ClientRectangle, _parent._renderItems, _parent._zoom, _parent._panX, _parent._panY);
                        if (!string.IsNullOrEmpty(hit))
                        {
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
                                Capture = true;
                            }
                            Invalidate();
                            return;
                        }
                        else
                        {
                            // Empty canvas -> drag pan
                            _isPanning = true;
                            _panStartMouse = e.Location;
                            _panStartOriginX = _parent._panX;
                            _panStartOriginY = _parent._panY;
                            Cursor = Cursors.SizeAll;
                            Capture = true;
                        }
                    }
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                var stageRect = GetStageScreenRect();

                // 1. Pan Dragging
                if (_isPanning)
                {
                    int dx = e.X - _panStartMouse.X;
                    int dy = e.Y - _panStartMouse.Y;
                    _parent._panX = _panStartOriginX + dx;
                    _parent._panY = _panStartOriginY + dy;
                    Invalidate();
                    Update();
                    return;
                }

                // 2. Direct Selection Anchor Dragging
                if (_isDraggingAnchor && _parent._selectedNodeId != null && _activeAnchorIndex >= 0)
                {
                    var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                    if (item != null && item.VectorPath != null && _activeAnchorIndex < item.VectorPath.PointCount)
                    {
                        PointF curStage = ScreenToStage(e.Location, stageRect);
                        float dStageX = curStage.X - _anchorDragStartStage.X;
                        float dStageY = curStage.Y - _anchorDragStartStage.Y;

                        var pts = item.VectorPath.PathPoints;
                        var types = item.VectorPath.PathTypes;
                        pts[_activeAnchorIndex] = new PointF(pts[_activeAnchorIndex].X + dStageX, pts[_activeAnchorIndex].Y + dStageY);
                        item.VectorPath = new GraphicsPath(pts, types);
                        _anchorDragStartStage = curStage;

                        _hudMessage = string.Format("Điểm neo #{0}: ({1:F0}, {2:F0})", _activeAnchorIndex, pts[_activeAnchorIndex].X, pts[_activeAnchorIndex].Y);
                        Invalidate();
                        Update();
                    }
                    return;
                }

                // 3. Selection Tool Manipulation (Move, Scale, Rotate)
                if (_isManipulating && _parent._selectedNodeId != null)
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
                            node.Transform.OffsetX = (int)Math.Max(-400, Math.Min(400, targetOffsetX));
                            node.Transform.OffsetY = (int)Math.Max(-400, Math.Min(400, targetOffsetY));
                            _parent._transformsMap[node.Id] = node.Transform;
                            _hudMessage = string.Format("X: {0:+0;-0;0} px   Y: {1:+0;-0;0} px", node.Transform.OffsetX, node.Transform.OffsetY);

                            _parent._renderItems = IdsLayoutEngine.ComputeLayout(_parent._ast);
                            Invalidate();
                            Update();
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
                            Invalidate();
                            Update();
                            return;
                        }
                        else
                        {
                            // Scale Handles
                            PointF curUnrot = RotatePoint(curStage, _startCenter, -_startTransform.Rotation);
                            PointF startUnrot = RotatePoint(_dragStartMouseStage, _startCenter, -_startTransform.Rotation);
                            float localDx = curUnrot.X - startUnrot.X;
                            float localDy = curUnrot.Y - startUnrot.Y;

                            float origW = Math.Max(10f, _startStageBounds.Width);
                            float origH = Math.Max(10f, _startStageBounds.Height);

                            float scaleFactorX = 1f;
                            float scaleFactorY = 1f;

                            switch (_activeHandle)
                            {
                                case BoundingHandle.MiddleRight: scaleFactorX = (origW + localDx) / origW; break;
                                case BoundingHandle.MiddleLeft: scaleFactorX = (origW - localDx) / origW; break;
                                case BoundingHandle.BottomCenter: scaleFactorY = (origH + localDy) / origH; break;
                                case BoundingHandle.TopCenter: scaleFactorY = (origH - localDy) / origH; break;
                                case BoundingHandle.BottomRight:
                                    scaleFactorX = (origW + localDx) / origW;
                                    scaleFactorY = (origH + localDy) / origH;
                                    break;
                                case BoundingHandle.BottomLeft:
                                    scaleFactorX = (origW - localDx) / origW;
                                    scaleFactorY = (origH + localDy) / origH;
                                    break;
                                case BoundingHandle.TopRight:
                                    scaleFactorX = (origW + localDx) / origW;
                                    scaleFactorY = (origH - localDy) / origH;
                                    break;
                                case BoundingHandle.TopLeft:
                                    scaleFactorX = (origW - localDx) / origW;
                                    scaleFactorY = (origH - localDy) / origH;
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

                            float newScaleX = Math.Max(0.2f, Math.Min(3.0f, _startTransform.ScaleX * scaleFactorX));
                            float newScaleY = Math.Max(0.2f, Math.Min(3.0f, _startTransform.ScaleY * scaleFactorY));

                            node.Transform = _startTransform.Clone();
                            node.Transform.ScaleX = (float)Math.Round(newScaleX, 2);
                            node.Transform.ScaleY = (float)Math.Round(newScaleY, 2);
                            _parent._transformsMap[node.Id] = node.Transform;

                            _hudMessage = string.Format("Tỉ lệ: W: {0:F0}%  H: {1:F0}%", node.Transform.ScaleX * 100, node.Transform.ScaleY * 100);

                            _parent._renderItems = IdsLayoutEngine.ComputeLayout(_parent._ast);
                            Invalidate();
                            Update();
                            return;
                        }
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
                    if (!string.IsNullOrEmpty(_parent._selectedNodeId))
                    {
                        var item = _parent._renderItems.Find(x => x.NodeId == _parent._selectedNodeId);
                        if (item != null)
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
                    Cursor = (!string.IsNullOrEmpty(hit)) ? Cursors.Hand : Cursors.Default;
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
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
                    _isManipulating = false;
                    _activeHandle = BoundingHandle.None;
                    _hudMessage = null;
                    _parent._activeSmartGuides.Clear();
                    Capture = false;
                    Cursor = Cursors.Default;
                    _parent.RebuildLayersList();
                    _parent.RebuildInspector();
                    Invalidate();
                }
                if (_isDraggingAnchor)
                {
                    _isDraggingAnchor = false;
                    _hudMessage = null;
                    Capture = false;
                    Invalidate();
                }
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

                // 5. Direct Selection Vector Anchor Points
                if (_parent._toolMode == EditorToolMode.DirectSelect && !string.IsNullOrEmpty(_parent._selectedNodeId))
                {
                    DrawDirectSelectionAnchors(g, stageRect);
                }

                // 6. Illustrator Bounding Box & 9 Handles
                if (_parent._toolMode == EditorToolMode.Select && !string.IsNullOrEmpty(_parent._selectedNodeId))
                {
                    DrawBoundingBox(g, stageRect);
                }

                // 7. Floating HUD Badge (Coordinates, Dimensions, Angle)
                if (!string.IsNullOrEmpty(_hudMessage))
                {
                    DrawHudBadge(g);
                }
            }

            private void DrawBoundingBox(Graphics g, RectangleF stageRect)
            {
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
                {
                    strokePen.StartCap = LineCap.Round;
                    strokePen.EndCap = LineCap.Round;
                    strokePen.LineJoin = LineJoin.Round;
                    foreach (var path in _parent._customStrokes)
                    {
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
                if (item == null || item.VectorPath == null) return;

                var pts = item.VectorPath.PathPoints;
                float boxSize = 6f;
                float half = boxSize / 2f;

                using (var unselBrush = new SolidBrush(Color.White))
                using (var selBrush = new SolidBrush(Color.FromArgb(37, 99, 235)))
                using (var borderPen = new Pen(Color.FromArgb(37, 99, 235), 1.5f))
                {
                    for (int i = 0; i < pts.Length; i++)
                    {
                        PointF sp = StageToScreen(pts[i], stageRect);
                        bool isSel = (i == _activeAnchorIndex);
                        if (isSel)
                        {
                            g.FillRectangle(selBrush, sp.X - half, sp.Y - half, boxSize, boxSize);
                        }
                        else
                        {
                            g.FillRectangle(unselBrush, sp.X - half, sp.Y - half, boxSize, boxSize);
                            g.DrawRectangle(borderPen, sp.X - half, sp.Y - half, boxSize, boxSize);
                        }
                    }
                }
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
