using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Forms
{
    public class FontTestControl : UserControl
    {
        // Core Font Services & State
        private readonly FontLoaderService _mainFontService;
        private PrivateFontCollection _privateFonts;
        private IntPtr _lastFontBuffer = IntPtr.Zero;
        private OpenTypeBinaryReader _testReader;
        private FontFamily _testFontFamily;
        private string _testFontPath;
        private FontFamily _refFontFamily;

        // Visual Configuration
        private float _fontSize = 36f;
        private float _lineSpacing = 1.35f;
        private float _zoom = 1.0f;
        private bool _showMetrics = true;
        private bool _compareMode = false;
        private int _themeIndex = 0; // 0: White/Black, 1: Dark Slate, 2: Antique Parchment

        // Spacing & Margin Configuration
        private float _charSpacing = 0f;
        private float _puaMarginLeft = 0f;
        private float _puaMarginRight = 0f;
        private float _puaBaselineShift = 0f;

        private NumericUpDown _numCharSpacing;
        private NumericUpDown _numPuaMarginLeft;
        private NumericUpDown _numPuaMarginRight;
        private NumericUpDown _numPuaBaselineShift;
        private Button _btnResetPuaSpacing;

        // UI Controls
        private Panel _topBar;
        private Panel _paletteBar;
        private SplitContainer _mainSplit;
        private TextBox _txtEditor;
        private FontTestCanvas _canvas;
        private Panel _canvasContainer;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusCharCount;
        private ToolStripStatusLabel _statusPuaCount;
        private ToolStripStatusLabel _statusFontInfo;

        // Toolbar Components
        private Label _lblFontName;
        private Button _btnBrowseFont;
        private Button _btnReloadModified;
        private ComboBox _cbFontSize;
        private ComboBox _cbLineSpacing;
        private ComboBox _cbTheme;
        private CheckBox _chkShowMetrics;
        private CheckBox _chkCompareMode;

        // Palette Components
        private FlowLayoutPanel _puaFlow;
        private TextBox _txtHexInput;
        private Button _btnInsertHex;
        private ComboBox _cbPresets;
        private Button _btnCopyText;
        private Button _btnExportPng;
        private Button _btnClearText;

        public FontTestControl(FontLoaderService fontService)
        {
            _mainFontService = fontService;
            InitializeComponent();
            InitReferenceFont();
            TryAutoLoadInitialFont();
            PopulatePuaPalette();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(241, 245, 249); // Slate 100

            // 1. Status Strip
            _statusStrip = new StatusStrip { SizingGrip = false, BackColor = Color.FromArgb(248, 250, 252) };
            _statusCharCount = new ToolStripStatusLabel { Text = "Ký tự: 0", Margin = new Padding(8, 2, 8, 2) };
            _statusPuaCount = new ToolStripStatusLabel { Text = "PUA / Ext-B: 0", Margin = new Padding(8, 2, 8, 2) };
            _statusFontInfo = new ToolStripStatusLabel { Text = "Chưa nạp font tùy chỉnh", Spring = true, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(8, 2, 8, 2) };
            _statusStrip.Items.AddRange(new ToolStripItem[] { _statusCharCount, _statusPuaCount, _statusFontInfo });

            // 2. Top Bar (Font & Formatting Tools)
            _topBar = CreateTopBar();

            // 3. Palette Bar (Quick Insert PUA, Hex, Presets)
            _paletteBar = CreatePaletteBar();

            // 4. Main Split Container (Top Editor, Bottom High-DPI Canvas)
            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(203, 213, 225)
            };

            // Top Panel: Editor
            var editorPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 4) };
            var lblEditorTitle = new Label
            {
                Text = "KHUNG SOẠN THẢO VĂN BẢN (Nhập chữ tự do, dán văn bản, hoặc bấm ký tự PUA bên trên):",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105)
            };
            _txtEditor = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 16F),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(15, 23, 42)
            };
            _txtEditor.TextChanged += (s, e) => OnEditorTextChanged();
            editorPanel.Controls.Add(_txtEditor);
            editorPanel.Controls.Add(lblEditorTitle);
            _mainSplit.Panel1.Controls.Add(editorPanel);

            // Bottom Panel: Canvas Viewport
            var canvasWrapper = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252) };
            var canvasHeader = CreateCanvasHeader();

            _canvasContainer = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(12)
            };
            _canvas = new FontTestCanvas(this)
            {
                Location = new Point(12, 12),
                Size = new Size(1200, 800)
            };
            _canvasContainer.Controls.Add(_canvas);

            canvasWrapper.Controls.Add(_canvasContainer);
            canvasWrapper.Controls.Add(canvasHeader);
            _mainSplit.Panel2.Controls.Add(canvasWrapper);

            // Container Panel for Header + Palette
            var headerContainer = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            headerContainer.Controls.Add(_paletteBar);
            headerContainer.Controls.Add(_topBar);

            Controls.Add(_mainSplit);
            Controls.Add(headerContainer);
            Controls.Add(_statusStrip);

            ResumeLayout(false);
            PerformLayout();

            // Set default initial splitter distance
            Load += (s, e) =>
            {
                try
                {
                    _mainSplit.SplitterDistance = Math.Max(120, (int)(Height * 0.28));
                }
                catch { }
                ApplySamplePreset(1); // Mẫu khởi đầu
            };
        }

        private Panel CreateTopBar()
        {
            var pnl = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8, 7, 8, 5),
                BackColor = Color.FromArgb(255, 255, 255),
                WrapContents = true
            };

            var lblFontTag = new Label
            {
                Text = "Font Kiểm Tra:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                AutoSize = true,
                Margin = new Padding(0, 5, 4, 0)
            };

            _lblFontName = new Label
            {
                Text = "Mặc định (Chưa nạp font file)",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(2, 132, 199),
                AutoSize = true,
                Margin = new Padding(0, 5, 8, 0)
            };

            _btnBrowseFont = CreateToolButton("Chọn Font (.ttf)...", OnBrowseFontClick, Color.FromArgb(241, 245, 249), Color.FromArgb(15, 23, 42));
            _btnReloadModified = CreateToolButton("⚡ Nạp font vừa sửa", OnReloadModifiedClick, Color.FromArgb(243, 232, 255), Color.FromArgb(107, 33, 168));

            var sep1 = CreateSeparator();

            var lblSize = new Label { Text = "Cỡ chữ:", AutoSize = true, Margin = new Padding(4, 5, 2, 0), ForeColor = Color.FromArgb(51, 65, 85) };
            _cbFontSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 85, Margin = new Padding(0, 2, 8, 0) };
            _cbFontSize.Items.AddRange(new object[] { "14 pt", "18 pt", "24 pt", "32 pt", "36 pt", "48 pt", "64 pt", "72 pt", "96 pt", "120 pt", "144 pt", "160 pt", "180 pt", "190 pt", "200 pt", "240 pt" });
            _cbFontSize.SelectedIndex = 4; // 36 pt
            Action applyFontSize = () =>
            {
                string raw = _cbFontSize.Text ?? "";
                string txt = raw.ToLowerInvariant().Replace("pt", "").Trim();
                float val;
                if (float.TryParse(txt, out val) && val >= 6 && val <= 360)
                {
                    _fontSize = val;
                    UpdateFontAndRedraw();
                }
            };
            _cbFontSize.SelectedIndexChanged += (s, e) => applyFontSize();
            _cbFontSize.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    applyFontSize();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            _cbFontSize.Leave += (s, e) => applyFontSize();

            var lblLine = new Label { Text = "Dòng:", AutoSize = true, Margin = new Padding(2, 5, 2, 0), ForeColor = Color.FromArgb(51, 65, 85) };
            _cbLineSpacing = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Margin = new Padding(0, 2, 8, 0) };
            _cbLineSpacing.Items.AddRange(new object[] { "1.0x", "1.2x", "1.35x", "1.5x", "1.8x", "2.0x" });
            _cbLineSpacing.SelectedIndex = 2; // 1.35x
            _cbLineSpacing.SelectedIndexChanged += (s, e) =>
            {
                string txt = _cbLineSpacing.SelectedItem.ToString().Replace("x", "").Trim();
                float val;
                if (float.TryParse(txt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val))
                {
                    _lineSpacing = val;
                    _canvas.Invalidate();
                }
            };

            var lblTh = new Label { Text = "Giao diện:", AutoSize = true, Margin = new Padding(2, 5, 2, 0), ForeColor = Color.FromArgb(51, 65, 85) };
            _cbTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(0, 2, 8, 0) };
            _cbTheme.Items.AddRange(new object[] { "Giấy Trắng", "Chế Độ Tối", "Thư Pháp Cổ" });
            _cbTheme.SelectedIndex = 0;
            _cbTheme.SelectedIndexChanged += (s, e) =>
            {
                _themeIndex = _cbTheme.SelectedIndex;
                ApplyTheme();
            };

            _chkShowMetrics = new CheckBox
            {
                Text = "Đường gióng (Baseline / Khung)",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(4, 5, 8, 0),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            _chkShowMetrics.CheckedChanged += (s, e) =>
            {
                _showMetrics = _chkShowMetrics.Checked;
                _canvas.Invalidate();
            };

            _chkCompareMode = new CheckBox
            {
                Text = "So sánh font gốc (Reference)",
                Checked = false,
                AutoSize = true,
                Margin = new Padding(4, 5, 8, 0),
                ForeColor = Color.FromArgb(180, 50, 50),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _chkCompareMode.CheckedChanged += (s, e) =>
            {
                _compareMode = _chkCompareMode.Checked;
                _canvas.Invalidate();
            };

            pnl.Controls.AddRange(new Control[]
            {
                lblFontTag, _lblFontName, _btnBrowseFont, _btnReloadModified,
                sep1,
                lblSize, _cbFontSize,
                lblLine, _cbLineSpacing,
                lblTh, _cbTheme,
                _chkShowMetrics, _chkCompareMode
            });

            return pnl;
        }

        private Panel CreatePaletteBar()
        {
            var pnl = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8, 4, 8, 6),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var topRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0)
            };

            var lblPua = new Label
            {
                Text = "Kho chữ PUA đã tạo:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Margin = new Padding(0, 5, 6, 0)
            };

            _puaFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0, 0, 8, 0)
            };

            var sep2 = CreateSeparator();

            var lblHex = new Label
            {
                Text = "Chèn mã Hex: U+",
                AutoSize = true,
                Margin = new Padding(2, 5, 2, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105)
            };

            _txtHexInput = new TextBox
            {
                Width = 70,
                Text = "E050",
                Margin = new Padding(0, 2, 4, 0),
                Font = new Font("Consolas", 9.5F)
            };
            _txtHexInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    OnInsertHexClick(null, EventArgs.Empty);
                    e.SuppressKeyPress = true;
                }
            };

            _btnInsertHex = CreateToolButton("+ Chèn", OnInsertHexClick, Color.FromArgb(224, 231, 255), Color.FromArgb(67, 56, 202));

            var sep3 = CreateSeparator();

            var lblPreset = new Label
            {
                Text = "Văn bản mẫu:",
                AutoSize = true,
                Margin = new Padding(4, 5, 2, 0),
                ForeColor = Color.FromArgb(51, 65, 85)
            };

            _cbPresets = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 190,
                Margin = new Padding(0, 2, 8, 0)
            };
            _cbPresets.Items.AddRange(new object[]
            {
                "1. Hỗn hợp Hán - Nôm - PUA",
                "2. Thang kích thước (Size Ladder)",
                "3. Cân hàng Baseline & Ô vuông",
                "4. Toàn bộ kho chữ PUA",
                "5. Đoạn văn Truyện Kiều cổ"
            });
            _cbPresets.SelectedIndex = 0;
            _cbPresets.SelectedIndexChanged += (s, e) =>
            {
                ApplySamplePreset(_cbPresets.SelectedIndex + 1);
            };

            _btnCopyText = CreateToolButton("Sao chép", OnCopyTextClick, Color.FromArgb(241, 245, 249), Color.FromArgb(15, 23, 42));
            _btnExportPng = CreateToolButton("Xuất PNG", OnExportPngClick, Color.FromArgb(220, 252, 231), Color.FromArgb(22, 101, 52));
            _btnClearText = CreateToolButton("Xóa sạch", (s, e) => _txtEditor.Clear(), Color.FromArgb(254, 242, 242), Color.FromArgb(153, 27, 27));

            topRow.Controls.AddRange(new Control[]
            {
                lblPua, _puaFlow,
                sep2,
                lblHex, _txtHexInput, _btnInsertHex,
                sep3,
                lblPreset, _cbPresets,
                _btnCopyText, _btnExportPng, _btnClearText
            });

            // Row 2: Spacing & Margin controls (Khoảng cách ký tự & Căn lề PUA E000+)
            var spacingRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 4, 0, 0),
                Padding = new Padding(0, 3, 0, 0)
            };

            var lblSpacing = new Label
            {
                Text = "Khoảng cách chữ (Tracking):",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Margin = new Padding(0, 5, 4, 0)
            };

            _numCharSpacing = new NumericUpDown
            {
                Minimum = -50,
                Maximum = 100,
                Value = 0,
                Width = 60,
                Margin = new Padding(0, 2, 8, 0),
                Font = new Font("Segoe UI", 9F)
            };
            _numCharSpacing.ValueChanged += (s, e) =>
            {
                _charSpacing = (float)_numCharSpacing.Value;
                _canvas.Invalidate();
            };

            var sepSpacing = CreateSeparator();

            var lblPuaSection = new Label
            {
                Text = "Căn lề PUA (≥ E000):",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(107, 33, 168),
                AutoSize = true,
                Margin = new Padding(2, 5, 4, 0)
            };

            var lblPuaLsb = new Label
            {
                Text = "Lề trái (LSB):",
                AutoSize = true,
                Margin = new Padding(0, 5, 2, 0),
                ForeColor = Color.FromArgb(51, 65, 85)
            };

            _numPuaMarginLeft = new NumericUpDown
            {
                Minimum = -100,
                Maximum = 100,
                Value = 0,
                Width = 58,
                Margin = new Padding(0, 2, 8, 0),
                Font = new Font("Segoe UI", 9F)
            };
            _numPuaMarginLeft.ValueChanged += (s, e) =>
            {
                _puaMarginLeft = (float)_numPuaMarginLeft.Value;
                _canvas.Invalidate();
            };

            var lblPuaRsb = new Label
            {
                Text = "Lề phải (RSB):",
                AutoSize = true,
                Margin = new Padding(0, 5, 2, 0),
                ForeColor = Color.FromArgb(51, 65, 85)
            };

            _numPuaMarginRight = new NumericUpDown
            {
                Minimum = -100,
                Maximum = 100,
                Value = 0,
                Width = 58,
                Margin = new Padding(0, 2, 8, 0),
                Font = new Font("Segoe UI", 9F)
            };
            _numPuaMarginRight.ValueChanged += (s, e) =>
            {
                _puaMarginRight = (float)_numPuaMarginRight.Value;
                _canvas.Invalidate();
            };

            var lblPuaBase = new Label
            {
                Text = "Căn lề dọc (Baseline):",
                AutoSize = true,
                Margin = new Padding(0, 5, 2, 0),
                ForeColor = Color.FromArgb(51, 65, 85)
            };

            _numPuaBaselineShift = new NumericUpDown
            {
                Minimum = -50,
                Maximum = 50,
                Value = 0,
                Width = 58,
                Margin = new Padding(0, 2, 8, 0),
                Font = new Font("Segoe UI", 9F)
            };
            _numPuaBaselineShift.ValueChanged += (s, e) =>
            {
                _puaBaselineShift = (float)_numPuaBaselineShift.Value;
                _canvas.Invalidate();
            };

            _btnResetPuaSpacing = CreateToolButton("Đặt lại PUA", (s, e) =>
            {
                _numPuaMarginLeft.Value = 0;
                _numPuaMarginRight.Value = 0;
                _numPuaBaselineShift.Value = 0;
            }, Color.FromArgb(243, 232, 255), Color.FromArgb(107, 33, 168));

            spacingRow.Controls.AddRange(new Control[]
            {
                lblSpacing, _numCharSpacing,
                sepSpacing,
                lblPuaSection,
                lblPuaLsb, _numPuaMarginLeft,
                lblPuaRsb, _numPuaMarginRight,
                lblPuaBase, _numPuaBaselineShift,
                _btnResetPuaSpacing
            });

            pnl.Controls.Add(spacingRow);
            pnl.Controls.Add(topRow);
            return pnl;
        }

        private Panel CreateCanvasHeader()
        {
            var pnl = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(8, 4, 8, 4)
            };

            var lblTitle = new Label
            {
                Text = "KẾT XUẤT ĐỒ HỌA CHI TIẾT (High-DPI Font Testing Viewport):",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Dock = DockStyle.Left,
                AutoSize = true
            };

            var zoomFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight
            };

            var lblZoom = new Label { Text = "Thu phóng:", AutoSize = true, Margin = new Padding(0, 3, 4, 0), ForeColor = Color.FromArgb(71, 85, 105) };
            var btnZoomOut = new Button { Text = "−", Width = 26, Height = 22, Margin = new Padding(0, 0, 2, 0) };
            var lblZoomVal = new Label { Text = "100%", AutoSize = true, Margin = new Padding(2, 3, 2, 0), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
            var btnZoomIn = new Button { Text = "+", Width = 26, Height = 22, Margin = new Padding(2, 0, 0, 0) };

            btnZoomOut.Click += (s, e) =>
            {
                if (_zoom > 0.5f)
                {
                    _zoom = Math.Max(0.5f, _zoom - 0.25f);
                    lblZoomVal.Text = string.Format("{0:0}%", _zoom * 100);
                    _canvas.Invalidate();
                }
            };
            btnZoomIn.Click += (s, e) =>
            {
                if (_zoom < 3.0f)
                {
                    _zoom = Math.Min(3.0f, _zoom + 0.25f);
                    lblZoomVal.Text = string.Format("{0:0}%", _zoom * 100);
                    _canvas.Invalidate();
                }
            };

            zoomFlow.Controls.AddRange(new Control[] { lblZoom, btnZoomOut, lblZoomVal, btnZoomIn });
            pnl.Controls.Add(lblTitle);
            pnl.Controls.Add(zoomFlow);
            return pnl;
        }

        private Button CreateToolButton(string text, EventHandler onClick, Color bg, Color fg)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = fg,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(2, 1, 4, 1),
                Padding = new Padding(4, 2, 4, 2)
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.Click += onClick;
            return btn;
        }

        private Control CreateSeparator()
        {
            return new Label
            {
                Text = "|",
                ForeColor = Color.FromArgb(203, 213, 225),
                AutoSize = true,
                Margin = new Padding(4, 5, 4, 0)
            };
        }

        private void InitReferenceFont()
        {
            try
            {
                // Reference font priority: SimSun, Nom Na Tong, MS Mincho, generic serif
                var installed = new InstalledFontCollection();
                foreach (var f in installed.Families)
                {
                    if (string.Equals(f.Name, "SimSun", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.Name, "Nom Na Tong", StringComparison.OrdinalIgnoreCase))
                    {
                        _refFontFamily = f;
                        break;
                    }
                }
            }
            catch { }

            if (_refFontFamily == null)
            {
                _refFontFamily = FontFamily.GenericSerif;
            }
        }

        private void TryAutoLoadInitialFont()
        {
            // 1. Kiểm tra FontEditorDialog.LastModifiedFontPath trước
            if (!string.IsNullOrEmpty(FontEditorDialog.LastModifiedFontPath) && File.Exists(FontEditorDialog.LastModifiedFontPath))
            {
                LoadFontFile(FontEditorDialog.LastModifiedFontPath);
                return;
            }

            // 2. Kiểm tra font đang mở của chương trình chính
            if (_mainFontService != null && _mainFontService.CurrentFont != null)
            {
                if (!string.IsNullOrEmpty(_mainFontService.CurrentFont.FontPath) && File.Exists(_mainFontService.CurrentFont.FontPath))
                {
                    LoadFontFile(_mainFontService.CurrentFont.FontPath);
                    return;
                }
            }

            // 3. Fallback: SimSun hoặc SimSun-ExtB trong thư mục Windows Fonts
            string fontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string simsunb = Path.Combine(fontsDir, "simsunb.ttf");
            if (File.Exists(simsunb))
            {
                LoadFontFile(simsunb);
            }
        }

        public void LoadFontFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            try
            {
                _testFontPath = filePath;

                if (_privateFonts != null)
                {
                    _privateFonts.Dispose();
                    _privateFonts = null;
                }
                if (_lastFontBuffer != IntPtr.Zero)
                {
                    System.Runtime.InteropServices.Marshal.FreeCoTaskMem(_lastFontBuffer);
                    _lastFontBuffer = IntPtr.Zero;
                }

                // Dùng AddMemoryFont thay vì AddFontFile để Windows GDI không khóa file trên đĩa
                byte[] fontBytes = File.ReadAllBytes(filePath);
                _lastFontBuffer = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(fontBytes.Length);
                System.Runtime.InteropServices.Marshal.Copy(fontBytes, 0, _lastFontBuffer, fontBytes.Length);
                _privateFonts = new PrivateFontCollection();
                _privateFonts.AddMemoryFont(_lastFontBuffer, fontBytes.Length);

                if (_testReader != null)
                {
                    _testReader.Dispose();
                    _testReader = null;
                }
                _testReader = new OpenTypeBinaryReader(filePath);

                if (_privateFonts.Families.Length > 0)
                {
                    _testFontFamily = _privateFonts.Families[0];
                }
                else
                {
                    _testFontFamily = FontFamily.GenericSerif;
                }

                string fileName = Path.GetFileName(filePath);
                string familyName = !string.IsNullOrEmpty(_testReader.Header.FamilyName) ? _testReader.Header.FamilyName : _testFontFamily.Name;
                _lblFontName.Text = string.Format("{0} ({1})", familyName, fileName);

                _statusFontInfo.Text = string.Format("Font: {0} | UnitsPerEm: {1} | Glyphs: {2:N0} | Khối: U+{3:X4}..U+{4:X4}",
                    fileName, _testReader.Header.UnitsPerEm, _testReader.Header.NumGlyphs,
                    _testReader.Header.XMin, _testReader.Header.XMax);

                UpdateFontAndRedraw();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải tệp font: " + ex.Message, "Lỗi Nạp Font", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulatePuaPalette()
        {
            _puaFlow.Controls.Clear();
            var list = IdsDictionaryStorage.Load();
            if (list == null || list.Count == 0) return;

            int count = 0;
            foreach (var item in list)
            {
                if (count >= 16) break; // Hiển thị 16 ký tự PUA nổi bật
                int cp = ParseCodePoint(item.PuaCode);
                if (cp <= 0) continue;

                string ch = char.ConvertFromUtf32(cp);
                var btn = new Button
                {
                    Text = string.Format("{0} (U+{1:X4})", ch, cp),
                    AutoSize = true,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(238, 242, 255),
                    ForeColor = Color.FromArgb(67, 56, 202),
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(2, 1, 2, 1),
                    Padding = new Padding(3, 1, 3, 1)
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(199, 210, 254);
                btn.Click += (s, e) => InsertTextAtCaret(ch);

                _puaFlow.Controls.Add(btn);
                count++;
            }
        }

        private static int ParseCodePoint(string puaCode)
        {
            if (string.IsNullOrEmpty(puaCode)) return 0;
            string clean = puaCode.Trim().Replace("U+", "").Replace("u+", "").Replace("0x", "").Replace("0X", "");
            int cp;
            if (int.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out cp))
            {
                return cp;
            }
            return 0;
        }

        private void InsertTextAtCaret(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int sel = _txtEditor.SelectionStart;
            _txtEditor.Text = _txtEditor.Text.Insert(sel, text);
            _txtEditor.SelectionStart = sel + text.Length;
            _txtEditor.Focus();
        }

        private void OnInsertHexClick(object sender, EventArgs e)
        {
            int cp = ParseCodePoint(_txtHexInput.Text);
            if (cp > 0 && cp <= 0x10FFFF)
            {
                string ch = char.ConvertFromUtf32(cp);
                InsertTextAtCaret(ch);
            }
            else
            {
                MessageBox.Show("Mã Unicode không hợp lệ! Vui lòng nhập số Hex từ 0020 đến 10FFFF.", "Mã không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnBrowseFontClick(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Tệp phông chữ TrueType (*.ttf;*.otf)|*.ttf;*.otf|Tất cả tệp (*.*)|*.*";
                ofd.Title = "Chọn tệp Font TrueType đã sửa hoặc cần kiểm tra";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    LoadFontFile(ofd.FileName);
                }
            }
        }

        private void OnReloadModifiedClick(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(FontEditorDialog.LastModifiedFontPath) && File.Exists(FontEditorDialog.LastModifiedFontPath))
            {
                LoadFontFile(FontEditorDialog.LastModifiedFontPath);
                return;
            }

            // Tìm kiếm tệp *_modified.ttf trong Desktop hoặc thư mục ứng dụng
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var candidates = Directory.GetFiles(desktop, "*_modified.ttf");
            if (candidates != null && candidates.Length > 0)
            {
                LoadFontFile(candidates[candidates.Length - 1]);
                return;
            }

            MessageBox.Show("Chưa tìm thấy tệp font modified nào gần đây. Vui lòng bấm 'Chọn Font...' để chỉ định tệp phông chữ.", "Chưa có font modified", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnCopyTextClick(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_txtEditor.Text))
            {
                Clipboard.SetText(_txtEditor.Text);
                MessageBox.Show("Đã sao chép toàn bộ văn bản (bao gồm cả ký tự PUA) vào Clipboard!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnExportPngClick(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "Ảnh PNG (*.png)|*.png";
                sfd.FileName = "Font_Test_Output.png";
                sfd.Title = "Xuất kết quả kiểm tra font ra ảnh PNG";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        using (var bmp = new Bitmap(_canvas.Width, _canvas.Height))
                        {
                            _canvas.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                            bmp.Save(sfd.FileName, ImageFormat.Png);
                        }
                        MessageBox.Show("Đã xuất ảnh thành công:\n" + sfd.FileName, "Xuất ảnh hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi xuất ảnh: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void OnEditorTextChanged()
        {
            string txt = _txtEditor.Text;
            int count = 0;
            int puaCount = 0;

            int i = 0;
            while (i < txt.Length)
            {
                int cp = char.ConvertToUtf32(txt, i);
                count++;
                if ((cp >= 0xE000 && cp <= 0xF8FF) || (cp >= 0xF0000 && cp <= 0x10FFFF) || (cp >= 0x20000 && cp <= 0x2EBEF))
                {
                    puaCount++;
                }
                i += char.IsSurrogatePair(txt, i) ? 2 : 1;
            }

            _statusCharCount.Text = string.Format("Ký tự: {0:N0}", count);
            _statusPuaCount.Text = string.Format("PUA / Ext-B: {0:N0}", puaCount);

            _canvas.Invalidate();
        }

        private void ApplySamplePreset(int presetIndex)
        {
            // Lấy danh sách PUA thực tế nếu có
            var puaList = IdsDictionaryStorage.Load();
            string puaSample1 = (puaList != null && puaList.Count > 0) ? char.ConvertFromUtf32(ParseCodePoint(puaList[0].PuaCode)) : char.ConvertFromUtf32(0xE050);
            string puaSample2 = (puaList != null && puaList.Count > 1) ? char.ConvertFromUtf32(ParseCodePoint(puaList[1].PuaCode)) : char.ConvertFromUtf32(0xE051);

            switch (presetIndex)
            {
                case 1:
                    _txtEditor.Text = string.Format(
                        "天地玄黃 宇宙洪荒 日月盈昃 辰宿列張\n" +
                        "Trời cao đất dày, lòng son tấc cỏ {0} {1}\n" +
                        "Kiểm tra nét chữ modified: {0} và {1}\n" +
                        "Kim cổ tương truyền, văn hiến thiên thu.", puaSample1, puaSample2);
                    break;
                case 2:
                    _txtEditor.Text = string.Format(
                        "Thang kích thước nét (Size Ladder):\n" +
                        "天地 {0} 委 (Cỡ chữ tiêu chuẩn so sánh)\n" +
                        "天地玄黃 委妥 {0} {1} 國王", puaSample1, puaSample2);
                    break;
                case 3:
                    _txtEditor.Text = string.Format(
                        "一 {0} 十 {1} 口 {0} 日 {1} 大 {0} 天 {1} 國 {0} 委 {1}\n" +
                        "Cân chỉnh đường Baseline và lề trái phải (LSB/RSB):", puaSample1, puaSample2);
                    break;
                case 4:
                    var sb = new StringBuilder();
                    sb.AppendLine("Kho toàn bộ ký tự PUA đã lưu trong từ điển:");
                    if (puaList != null && puaList.Count > 0)
                    {
                        foreach (var item in puaList)
                        {
                            int cp = ParseCodePoint(item.PuaCode);
                            if (cp > 0)
                            {
                                sb.AppendFormat("{0}  (U+{1:X4} - {2}: {3})\n", char.ConvertFromUtf32(cp), cp, item.Name, item.IdsString);
                            }
                        }
                    }
                    else
                    {
                        sb.AppendLine(puaSample1 + " (U+E050)");
                    }
                    _txtEditor.Text = sb.ToString();
                    break;
                case 5:
                    _txtEditor.Text = string.Format(
                        "Trăm năm trong cõi người ta,\n" +
                        "Chữ tài chữ mệnh khéo là ghét nhau.\n" +
                        "Trải qua một cuộc bể dâu {0},\n" +
                        "Những điều trông thấy mà đau đớn lòng {1}.", puaSample1, puaSample2);
                    break;
            }
        }

        private void ApplyTheme()
        {
            switch (_themeIndex)
            {
                case 0: // Giấy Trắng
                    _canvasContainer.BackColor = Color.FromArgb(248, 250, 252);
                    _canvas.BackColor = Color.White;
                    break;
                case 1: // Chế độ Tối
                    _canvasContainer.BackColor = Color.FromArgb(15, 23, 42);
                    _canvas.BackColor = Color.FromArgb(30, 41, 59);
                    break;
                case 2: // Thư Pháp Cổ
                    _canvasContainer.BackColor = Color.FromArgb(235, 226, 206);
                    _canvas.BackColor = Color.FromArgb(247, 242, 230);
                    break;
            }
            _canvas.Invalidate();
        }

        private void UpdateFontAndRedraw()
        {
            if (_testFontFamily != null)
            {
                try
                {
                    _txtEditor.Font = new Font(_testFontFamily, Math.Max(12f, Math.Min(24f, _fontSize * 0.6f)));
                }
                catch { }
            }
            _canvas.Invalidate();
        }

        #region Custom High-DPI Visual Canvas
        private class FontTestCanvas : Control
        {
            private readonly FontTestControl _parent;

            public FontTestCanvas(FontTestControl parent)
            {
                _parent = parent;
                DoubleBuffered = true;
                BackColor = Color.White;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                string fullText = _parent._txtEditor.Text;
                if (string.IsNullOrEmpty(fullText))
                {
                    using (var hintBrush = new SolidBrush(Color.FromArgb(148, 163, 184)))
                    using (var hintFont = new Font("Segoe UI", 12F, FontStyle.Italic))
                    {
                        g.DrawString("Chưa có văn bản. Nhập chữ vào khung soạn thảo phía trên để hiển thị kiểm tra...",
                            hintFont, hintBrush, 30, 40);
                    }
                    return;
                }

                // Color Themes
                Color inkColor, refInkColor, baselineColor, metricsColor, cellBorderColor;
                switch (_parent._themeIndex)
                {
                    case 1: // Chế Độ Tối
                        inkColor = Color.FromArgb(241, 245, 249);
                        refInkColor = Color.FromArgb(251, 146, 60); // Cam nhạt
                        baselineColor = Color.FromArgb(56, 189, 248); // Xanh cyan
                        metricsColor = Color.FromArgb(100, 116, 139);
                        cellBorderColor = Color.FromArgb(51, 65, 85);
                        break;
                    case 2: // Cổ Phong
                        inkColor = Color.FromArgb(28, 25, 23);
                        refInkColor = Color.FromArgb(180, 83, 9);
                        baselineColor = Color.FromArgb(194, 65, 12);
                        metricsColor = Color.FromArgb(214, 196, 165);
                        cellBorderColor = Color.FromArgb(214, 196, 165);
                        break;
                    default: // Giấy Trắng
                        inkColor = Color.FromArgb(15, 23, 42); // Slate 900
                        refInkColor = Color.FromArgb(234, 88, 12); // Cam đậm
                        baselineColor = Color.FromArgb(37, 99, 235); // Xanh dương
                        metricsColor = Color.FromArgb(148, 163, 184);
                        cellBorderColor = Color.FromArgb(226, 232, 240);
                        break;
                }

                float effectiveFontSize = _parent._fontSize * _parent._zoom;
                if (effectiveFontSize < 6f) effectiveFontSize = 6f;

                Font testFont = null;
                Font refFont = null;
                try
                {
                    testFont = new Font(_parent._testFontFamily ?? FontFamily.GenericSerif, effectiveFontSize, FontStyle.Regular, GraphicsUnit.Pixel);
                }
                catch
                {
                    testFont = new Font(FontFamily.GenericSerif, effectiveFontSize, FontStyle.Regular, GraphicsUnit.Pixel);
                }

                try
                {
                    refFont = new Font(_parent._refFontFamily ?? FontFamily.GenericSerif, effectiveFontSize, FontStyle.Regular, GraphicsUnit.Pixel);
                }
                catch
                {
                    refFont = new Font(FontFamily.GenericSerif, effectiveFontSize, FontStyle.Regular, GraphicsUnit.Pixel);
                }

                string[] lines = fullText.Replace("\r\n", "\n").Split('\n');
                float startX = 35f;
                float currentY = 35f;
                float lineHeight = effectiveFontSize * _parent._lineSpacing;
                if (_parent._compareMode)
                {
                    lineHeight = (effectiveFontSize * _parent._lineSpacing * 2f) + 16f;
                }

                float maxX = Width;

                using (var inkBrush = new SolidBrush(inkColor))
                using (var refBrush = new SolidBrush(refInkColor))
                using (var baselinePen = new Pen(baselineColor, 1f) { DashStyle = DashStyle.Dash })
                using (var ascPen = new Pen(Color.FromArgb(34, 197, 94), 1f) { DashStyle = DashStyle.Dot })
                using (var descPen = new Pen(Color.FromArgb(239, 68, 68), 1f) { DashStyle = DashStyle.Dot })
                using (var cellPen = new Pen(cellBorderColor, 1f) { DashStyle = DashStyle.Solid })
                using (var tagFont = new Font("Segoe UI", 7.5F, FontStyle.Bold))
                using (var tagBrush = new SolidBrush(metricsColor))
                {
                    for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
                    {
                        string line = lines[lineIdx];
                        float rowBaselineY = currentY + (effectiveFontSize * 0.85f);

                        // 1. Vẽ dòng chữ Font Modified
                        float curX = startX;

                        // Nếu bật Compare Mode, vẽ nhãn phân biệt
                        if (_parent._compareMode)
                        {
                            g.DrawString("MODIFIED:", tagFont, tagBrush, 6, rowBaselineY - effectiveFontSize * 0.5f);
                        }

                        int charIdx = 0;
                        while (charIdx < line.Length)
                        {
                            int cp = char.ConvertToUtf32(line, charIdx);
                            string chStr = char.ConvertFromUtf32(cp);
                            int step = char.IsSurrogatePair(line, charIdx) ? 2 : 1;

                            // Đo bề rộng ký tự
                            float charWidth = effectiveFontSize;
                            ushort gid = 0;
                            ushort adv = 0;
                            short lsb = 0;
                            if (_parent._testReader != null)
                            {
                                gid = _parent._testReader.GetGlyphIndex(cp);
                                if (gid > 0)
                                {
                                    _parent._testReader.GetHorizontalMetrics(gid, out adv, out lsb);
                                    if (adv > 0)
                                    {
                                        charWidth = (adv / (float)_parent._testReader.Header.UnitsPerEm) * effectiveFontSize;
                                    }
                                }
                            }

                            // Nếu là chữ Latin / ngoài font (gid == 0) hoặc khoảng trắng, đo theo kích thước font thực tế
                            if (gid == 0 || (cp <= 0x024F && cp != 0x20))
                            {
                                SizeF sz = g.MeasureString(chStr, testFont, PointF.Empty, StringFormat.GenericTypographic);
                                charWidth = Math.Max(effectiveFontSize * 0.22f, sz.Width);
                            }
                            else if (cp == 0x20)
                            {
                                charWidth = effectiveFontSize * 0.32f;
                            }

                            if (charWidth <= 0f) charWidth = effectiveFontSize;

                            bool isPua = (cp >= 0xE000 && cp <= 0xF8FF) || (cp >= 0xF0000 && cp <= 0x10FFFF) || (cp >= 0x20000 && cp <= 0x2EBEF);
                            float puaScale = effectiveFontSize / 36f;
                            float puaLeft = isPua ? (_parent._puaMarginLeft * puaScale) : 0f;
                            float puaRight = isPua ? (_parent._puaMarginRight * puaScale) : 0f;
                            float puaShiftY = isPua ? (_parent._puaBaselineShift * puaScale) : 0f;

                            // Áp dụng lề trái cho ký tự PUA (LSB adjustment)
                            curX += puaLeft;

                            // Vẽ đường gióng khung ô vuông quanh ký tự nếu được bật (bỏ qua dấu cách)
                            if (_parent._showMetrics && cp > 0x20)
                            {
                                float cellTop = rowBaselineY - (effectiveFontSize * 0.88f) + puaShiftY;
                                float cellHeight = effectiveFontSize;
                                g.DrawRectangle(cellPen, curX, cellTop, charWidth, cellHeight);
                            }

                            // Thử vẽ trực tiếp bằng Vector Glyph Outline nếu có sẵn trong file TrueType
                            bool drawnByVector = false;
                            if (_parent._testReader != null)
                            {
                                var rawOutline = _parent._testReader.GetGlyphOutline(cp);
                                if (rawOutline != null && rawOutline.Contours.Count > 0)
                                {
                                    float scale = effectiveFontSize / _parent._testReader.Header.UnitsPerEm;
                                    using (var smoothPath = VectorPathRenderer.CreateSmoothGraphicsPath(rawOutline, curX, rowBaselineY + puaShiftY, scale))
                                    {
                                        g.FillPath(inkBrush, smoothPath);
                                        drawnByVector = true;
                                    }
                                }
                            }

                            // Fallback bằng GDI+ DrawString nếu không có vector outline
                            if (!drawnByVector)
                            {
                                g.DrawString(chStr, testFont, inkBrush, curX, rowBaselineY + puaShiftY - (effectiveFontSize * 0.85f));
                            }

                            curX += charWidth + 2f + (_parent._charSpacing * puaScale) + puaRight;
                            charIdx += step;
                        }

                        // Vẽ đường Baseline (Y=0)
                        if (_parent._showMetrics)
                        {
                            g.DrawLine(baselinePen, startX - 10, rowBaselineY, Math.Max(curX + 20, 600), rowBaselineY);
                        }

                        // 2. Nếu ở Chế độ Đối Chiếu (Compare Mode): Vẽ thêm dòng chữ bằng Font Gốc ngay bên dưới
                        if (_parent._compareMode)
                        {
                            float refRowBaselineY = rowBaselineY + (effectiveFontSize * 1.1f);
                            g.DrawString("REFERENCE:", tagFont, new SolidBrush(refInkColor), 6, refRowBaselineY - effectiveFontSize * 0.5f);

                            float refCurX = startX;
                            int rCharIdx = 0;
                            while (rCharIdx < line.Length)
                            {
                                int cp = char.ConvertToUtf32(line, rCharIdx);
                                string chStr = char.ConvertFromUtf32(cp);
                                int step = char.IsSurrogatePair(line, rCharIdx) ? 2 : 1;

                                bool isRefPua = (cp >= 0xE000 && cp <= 0xF8FF) || (cp >= 0xF0000 && cp <= 0x10FFFF) || (cp >= 0x20000 && cp <= 0x2EBEF);
                                float refPuaScale = effectiveFontSize / 36f;
                                float refPuaLeft = isRefPua ? (_parent._puaMarginLeft * refPuaScale) : 0f;
                                float refPuaRight = isRefPua ? (_parent._puaMarginRight * refPuaScale) : 0f;
                                float refPuaShiftY = isRefPua ? (_parent._puaBaselineShift * refPuaScale) : 0f;

                                refCurX += refPuaLeft;
                                SizeF sz = g.MeasureString(chStr, refFont);
                                float rWidth = Math.Max(sz.Width * 0.8f, effectiveFontSize);

                                g.DrawString(chStr, refFont, refBrush, refCurX, refRowBaselineY + refPuaShiftY - (effectiveFontSize * 0.85f));
                                refCurX += rWidth + 2f + (_parent._charSpacing * refPuaScale) + refPuaRight;
                                rCharIdx += step;
                            }

                            if (_parent._showMetrics)
                            {
                                g.DrawLine(baselinePen, startX - 10, refRowBaselineY, Math.Max(refCurX + 20, 600), refRowBaselineY);
                            }
                        }

                        if (curX > maxX) maxX = curX;
                        currentY += lineHeight;
                    }
                }

                if (testFont != null) testFont.Dispose();
                if (refFont != null) refFont.Dispose();

                // Tự động mở rộng kích thước Canvas nếu văn bản dài
                int neededWidth = Math.Max(1200, (int)maxX + 80);
                int neededHeight = Math.Max(800, (int)currentY + 100);
                if (Width != neededWidth || Height != neededHeight)
                {
                    Size = new Size(neededWidth, neededHeight);
                }
            }
        }
        #endregion
    }
}
