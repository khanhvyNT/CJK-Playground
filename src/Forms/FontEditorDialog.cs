using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Forms
{
    public class FontEditorDialog : Form
    {
        private readonly GraphicsPath _composedPath;
        private readonly RectangleF _canvasSourceRect;
        private readonly string _currentFormula;
        private readonly string _currentTargetChar;
        private readonly FontFamily _activeFontFamily;

        // UI Controls
        private TextBox _txtFontPath;
        private Button _btnBrowse;
        private Label _lblFontInfo;

        private TextBox _txtCodePoint;
        private Button _btnNextPua;
        private Button _btnOriginalChar;
        private Label _lblCollisionStatus;

        private CheckBox _chkAutoCenter;
        private NumericUpDown _numAdvanceWidth;
        private Label _lblMetricsInfo;

        private RadioButton _rbSaveNew;
        private RadioButton _rbOverwrite;

        private Button _btnBatchPua;
        private Button _btnSave;
        private Button _btnClose;

        private FontPreviewBox _previewBox;

        // State
        public static string LastModifiedFontPath { get; set; }
        private OpenTypeBinaryReader _fontReader;
        private RawGlyphOutline _currentConvertedOutline;
        private int _selectedCodePoint = 0;

        public FontEditorDialog(
            GraphicsPath composedPath,
            RectangleF canvasSourceRect,
            string currentFormula,
            string currentTargetChar,
            FontFamily activeFontFamily)
        {
            _composedPath = composedPath != null ? (GraphicsPath)composedPath.Clone() : new GraphicsPath();
            _canvasSourceRect = canvasSourceRect.Width > 0 ? canvasSourceRect : new RectangleF(0, 0, 1000, 1000);
            _currentFormula = currentFormula ?? "";
            _currentTargetChar = currentTargetChar ?? "";
            _activeFontFamily = activeFontFamily;

            InitializeComponent();
            InitializeDefaultFontAndCodePoint();
        }

        private void InitializeComponent()
        {
            var appIcon = AppIconHelper.GetAppIcon();
            if (appIcon != null) Icon = appIcon;

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            Text = isVi ? "Trình Nạp Ký Tự Vào Font TrueType (.TTF) - Font Glyph Studio" : "TrueType Font Glyph Studio & Injector";
            Size = new Size(860, 680);
            MinimumSize = new Size(780, 600);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(248, 250, 252);

            // Main Layout Container
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90f));  // Top Font Section
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Middle Split
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));  // Bottom Action Bar

            // 1. Top Font Selection GroupBox
            var grpFont = new GroupBox
            {
                Text = isVi ? "1. TỆP PHÔNG CHỮ ĐÍCH (.TTF)" : "1. TARGET TRUETYPE FONT (.TTF)",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Padding = new Padding(8)
            };

            var topFontLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            topFontLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topFontLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            topFontLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            topFontLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _txtFontPath = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ReadOnly = true,
                BackColor = Color.White
            };

            _btnBrowse = new Button
            {
                Text = isVi ? "📂 Duyệt..." : "📂 Browse...",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Height = 28,
                Cursor = Cursors.Hand
            };
            _btnBrowse.Click += OnBrowseFont;

            _lblFontInfo = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105),
                Text = isVi ? "Chưa chọn tệp phông chữ TrueType." : "No TrueType font selected.",
                TextAlign = ContentAlignment.MiddleLeft
            };

            topFontLayout.Controls.Add(_txtFontPath, 0, 0);
            topFontLayout.Controls.Add(_btnBrowse, 1, 0);
            topFontLayout.Controls.Add(_lblFontInfo, 0, 1);
            topFontLayout.SetColumnSpan(_lblFontInfo, 2);
            grpFont.Controls.Add(topFontLayout);

            // 2. Middle Content Split: Left (Settings) & Right (Visual Preview)
            var middleSplit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            middleSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            middleSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));

            // Left Panel (Unicode & Metrics)
            var leftPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 8, 0) };

            // GroupBox Unicode Address
            var grpUnicode = new GroupBox
            {
                Text = isVi ? "2. ĐỊA CHỈ MÃ ĐIỂM UNICODE" : "2. UNICODE CODE POINT",
                Dock = DockStyle.Top,
                Height = 220,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Padding = new Padding(10)
            };

            var lblCodePrompt = new Label
            {
                Text = isVi ? "Nhập mã điểm Hex (ví dụ U+2B820 hoặc U+E050):" : "Enter Hex code point (e.g. U+2B820 or U+E050):",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Height = 22
            };

            _txtCodePoint = new TextBox
            {
                Dock = DockStyle.Top,
                Font = new Font("Consolas", 12F, FontStyle.Bold),
                CharacterCasing = CharacterCasing.Upper,
                Height = 30
            };
            _txtCodePoint.TextChanged += OnCodePointTextChanged;

            var quickButtonsPnl = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 4, 0, 0)
            };

            _btnNextPua = new Button
            {
                Text = isVi ? "📍 Mã PUA tiếp theo" : "📍 Next Free PUA",
                AutoSize = true,
                Height = 28,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            _btnNextPua.Click += OnFindNextPuaClick;

            _btnOriginalChar = new Button
            {
                Text = isVi ? "📍 Ký tự gốc" : "📍 Original Char",
                AutoSize = true,
                Height = 28,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            _btnOriginalChar.Click += OnOriginalCharClick;

            quickButtonsPnl.Controls.Add(_btnNextPua);
            quickButtonsPnl.Controls.Add(_btnOriginalChar);

            _lblCollisionStatus = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4)
            };

            grpUnicode.Controls.Add(_lblCollisionStatus);
            grpUnicode.Controls.Add(quickButtonsPnl);
            grpUnicode.Controls.Add(_txtCodePoint);
            grpUnicode.Controls.Add(lblCodePrompt);

            // GroupBox Metrics & Alignment
            var grpMetrics = new GroupBox
            {
                Text = isVi ? "3. CĂN CHỈNH THÔNG SỐ VỊ TRÍ" : "3. METRICS & POSITIONING",
                Dock = DockStyle.Top,
                Height = 170,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Padding = new Padding(10),
                Margin = new Padding(0, 8, 0, 0)
            };

            _chkAutoCenter = new CheckBox
            {
                Text = isVi ? "Tự động canh giữa ô vuông (Auto Center Horizontal)" : "Auto Center Horizontally",
                Dock = DockStyle.Top,
                Checked = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Height = 26
            };
            _chkAutoCenter.CheckedChanged += (s, e) => RecalculateGlyphOutline();

            var advancePnl = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 4, 0, 0)
            };

            var lblAdv = new Label
            {
                Text = isVi ? "Độ rộng bước tiến (Advance Width):" : "Advance Width:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(0, 4, 8, 0)
            };

            _numAdvanceWidth = new NumericUpDown
            {
                Minimum = 100,
                Maximum = 4000,
                Value = 1000,
                Width = 90,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
            _numAdvanceWidth.ValueChanged += (s, e) => RecalculateGlyphOutline();

            advancePnl.Controls.Add(lblAdv);
            advancePnl.Controls.Add(_numAdvanceWidth);

            _lblMetricsInfo = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "LSB: 0 | Ink Width: 0"
            };

            grpMetrics.Controls.Add(_lblMetricsInfo);
            grpMetrics.Controls.Add(advancePnl);
            grpMetrics.Controls.Add(_chkAutoCenter);

            leftPnl.Controls.Add(grpMetrics);
            leftPnl.Controls.Add(grpUnicode);

            // Right Panel (Preview Box)
            var grpPreview = new GroupBox
            {
                Text = isVi ? "4. XEM TRƯỚC VỊ TRÍ TRONG Ô VUÔNG (EM-SQUARE)" : "4. DESIGN EM-SQUARE PREVIEW",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Padding = new Padding(8)
            };

            _previewBox = new FontPreviewBox
            {
                Dock = DockStyle.Fill
            };
            grpPreview.Controls.Add(_previewBox);

            middleSplit.Controls.Add(leftPnl, 0, 0);
            middleSplit.Controls.Add(grpPreview, 1, 0);

            // 3. Bottom Action Bar
            var bottomBar = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 8, 0, 0)
            };

            var bottomLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

            var saveOptionsPnl = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight
            };

            _rbSaveNew = new RadioButton
            {
                Text = isVi ? "Lưu tệp mới (*_modified.ttf)" : "Save as new file",
                Checked = true,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(0, 6, 12, 0)
            };

            _rbOverwrite = new RadioButton
            {
                Text = isVi ? "Ghi đè tệp gốc (.bak an toàn)" : "Overwrite (safe .bak backup)",
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(0, 6, 0, 0)
            };

            saveOptionsPnl.Controls.Add(_rbSaveNew);
            saveOptionsPnl.Controls.Add(_rbOverwrite);

            var actionButtonsPnl = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };

            _btnClose = new Button
            {
                Text = isVi ? "Đóng" : "Close",
                Width = 85,
                Height = 34,
                Cursor = Cursors.Hand
            };
            _btnClose.Click += (s, e) => Close();

            _btnSave = new Button
            {
                Text = isVi ? "💾 TIẾN HÀNH LƯU" : "💾 SAVE GLYPH",
                Width = 150,
                Height = 34,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnSave.Click += OnSaveClick;

            _btnBatchPua = new Button
            {
                Text = isVi ? "📚 Nạp Sổ bộ PUA..." : "📚 Batch PUA Dict...",
                Width = 145,
                Height = 34,
                Cursor = Cursors.Hand
            };
            _btnBatchPua.Click += OnBatchPuaClick;

            actionButtonsPnl.Controls.Add(_btnClose);
            actionButtonsPnl.Controls.Add(_btnSave);
            actionButtonsPnl.Controls.Add(_btnBatchPua);

            bottomLayout.Controls.Add(saveOptionsPnl, 0, 0);
            bottomLayout.Controls.Add(actionButtonsPnl, 1, 0);
            bottomBar.Controls.Add(bottomLayout);

            // Add all to Main Layout
            mainLayout.Controls.Add(grpFont, 0, 0);
            mainLayout.Controls.Add(middleSplit, 0, 1);
            mainLayout.Controls.Add(bottomBar, 0, 2);

            Controls.Add(mainLayout);
        }

        private void InitializeDefaultFontAndCodePoint()
        {
            // Tìm font mặc định phù hợp (Nom Na Tong hoặc SimSun-ExtB)
            string defaultPath = null;
            string fontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string simsunb = Path.Combine(fontsDir, "simsunb.ttf");
            string arial = Path.Combine(fontsDir, "arial.ttf");

            if (File.Exists(simsunb)) defaultPath = simsunb;
            else if (File.Exists(arial)) defaultPath = arial;

            if (!string.IsNullOrEmpty(defaultPath))
            {
                LoadFontFile(defaultPath);
            }

            // Gợi ý mã Unicode
            if (!string.IsNullOrEmpty(_currentTargetChar))
            {
                int cp = char.ConvertToUtf32(_currentTargetChar, 0);
                _txtCodePoint.Text = "U+" + cp.ToString("X4");
            }
            else
            {
                OnFindNextPuaClick(null, null);
            }

            RecalculateGlyphOutline();
        }

        #region Font Loading & Handling
        private void OnBrowseFont(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "TrueType Font (*.ttf)|*.ttf|Tất cả tệp (*.*)|*.*";
                ofd.Title = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese
                    ? "Chọn tệp phông chữ TrueType để nạp glyph"
                    : "Select TrueType Font file";

                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    LoadFontFile(ofd.FileName);
                }
            }
        }

        private void LoadFontFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                if (_fontReader != null)
                {
                    _fontReader.Dispose();
                    _fontReader = null;
                }

                _fontReader = new OpenTypeBinaryReader(filePath);
                _txtFontPath.Text = filePath;

                string family = !string.IsNullOrEmpty(_fontReader.Header.FamilyName) ? _fontReader.Header.FamilyName : Path.GetFileNameWithoutExtension(filePath);
                string ver = !string.IsNullOrEmpty(_fontReader.Header.Version) ? _fontReader.Header.Version : "v1.0";

                _lblFontInfo.Text = string.Format("{0} ({1}) | {2:N0} glyphs | Em: {3} | Asc: {4}, Desc: {5}",
                    family, ver, _fontReader.Header.NumGlyphs, _fontReader.Header.UnitsPerEm,
                    _fontReader.Header.Ascender, _fontReader.Header.Descender);

                if (_fontReader.Header.UnitsPerEm > 0)
                {
                    int emVal = _fontReader.Header.UnitsPerEm;
                    _numAdvanceWidth.Value = Math.Max(100, Math.Min(4000, emVal));
                }

                UpdateCollisionStatus();
                RecalculateGlyphOutline();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể đọc tệp phông chữ: " + ex.Message,
                    "Lỗi đọc font",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        #endregion

        #region Unicode Code Point Parsing & Status
        private void OnCodePointTextChanged(object sender, EventArgs e)
        {
            string raw = _txtCodePoint.Text.Trim();
            _selectedCodePoint = ParseUnicodeCodePoint(raw);
            UpdateCollisionStatus();
        }

        private int ParseUnicodeCodePoint(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;

            input = input.Trim();
            if (input.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                input = input.Substring(2);
            }

            int val;
            if (int.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val))
            {
                return val;
            }

            // Nếu người dùng dán trực tiếp 1 ký tự vào ô text
            if (input.Length >= 1)
            {
                return char.ConvertToUtf32(input, 0);
            }

            return 0;
        }

        private void UpdateCollisionStatus()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            if (_selectedCodePoint <= 0)
            {
                _lblCollisionStatus.Text = isVi ? "⚪ Vui lòng nhập mã điểm Unicode hợp lệ." : "⚪ Please enter a valid Unicode code point.";
                _lblCollisionStatus.ForeColor = Color.FromArgb(100, 116, 139);
                _previewBox.SetExistingOutline(null);
                return;
            }

            if (_fontReader == null)
            {
                _lblCollisionStatus.Text = isVi ? "⚪ Đang chờ chọn tệp phông chữ." : "⚪ Waiting for font selection.";
                _lblCollisionStatus.ForeColor = Color.FromArgb(100, 116, 139);
                _previewBox.SetExistingOutline(null);
                return;
            }

            bool isSupported = _fontReader.IsCharacterSupported(_selectedCodePoint);
            if (isSupported)
            {
                ushort gid = _fontReader.GetGlyphIndex(_selectedCodePoint);
                _lblCollisionStatus.Text = isVi
                    ? string.Format("🟡 ĐÃ CÓ TRONG FONT (Glyph ID: {0}) => Sẽ GHI ĐÈ thay thế!", gid)
                    : string.Format("🟡 EXISTING IN FONT (Glyph ID: {0}) => Will OVERWRITE!", gid);
                _lblCollisionStatus.ForeColor = Color.FromArgb(180, 83, 9); // Amber 700

                var existing = _fontReader.GetGlyphOutline(_selectedCodePoint);
                _previewBox.SetExistingOutline(existing);
            }
            else
            {
                _lblCollisionStatus.Text = isVi
                    ? "🟢 Ô MÃ ĐANG TRỐNG => Sẽ tạo thêm glyph mới vào font!"
                    : "🟢 UNUSED CODE POINT => Will append new glyph to font!";
                _lblCollisionStatus.ForeColor = Color.FromArgb(22, 101, 52); // Emerald 800
                _previewBox.SetExistingOutline(null);
            }
        }

        private void OnFindNextPuaClick(object sender, EventArgs e)
        {
            // Bắt đầu từ 0xE000 (Private Use Area - BMP)
            int startPua = 0xE000;
            if (_fontReader != null)
            {
                while (startPua <= 0xF8FF && _fontReader.IsCharacterSupported(startPua))
                {
                    startPua++;
                }
            }
            _txtCodePoint.Text = "U+" + startPua.ToString("X4");
        }

        private void OnOriginalCharClick(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentTargetChar))
            {
                int cp = char.ConvertToUtf32(_currentTargetChar, 0);
                _txtCodePoint.Text = "U+" + cp.ToString("X4");
            }
            else
            {
                MessageBox.Show(
                    LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese
                        ? "Không có thông tin ký tự gốc của công thức IDS này."
                        : "No original character associated with this formula.",
                    "Thông báo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        #endregion

        #region Glyph Outline Recalculation & Preview
        private void RecalculateGlyphOutline()
        {
            if (_composedPath == null || _composedPath.PointCount == 0) return;

            var options = new GlyphMetricsOptions();
            if (_fontReader != null && _fontReader.Header != null)
            {
                options.UnitsPerEm = _fontReader.Header.UnitsPerEm > 0 ? _fontReader.Header.UnitsPerEm : (ushort)1000;
                options.Ascender = _fontReader.Header.Ascender != 0 ? _fontReader.Header.Ascender : (short)880;
                options.Descender = _fontReader.Header.Descender != 0 ? _fontReader.Header.Descender : (short)-120;
            }
            else
            {
                options.UnitsPerEm = 1000;
                options.Ascender = 880;
                options.Descender = -120;
            }

            options.AdvanceWidth = (ushort)_numAdvanceWidth.Value;
            options.AutoCenterHorizontal = _chkAutoCenter.Checked;

            _currentConvertedOutline = VectorToGlyphConverter.ConvertPathToTrueTypeGlyph(
                _composedPath,
                _canvasSourceRect,
                options);

            if (_currentConvertedOutline != null)
            {
                int inkW = _currentConvertedOutline.XMax - _currentConvertedOutline.XMin;
                int inkH = _currentConvertedOutline.YMax - _currentConvertedOutline.YMin;
                _lblMetricsInfo.Text = string.Format("LSB: {0} | Ink Box: {1} x {2} | Contours: {3}",
                    _currentConvertedOutline.LeftSideBearing,
                    inkW, inkH, _currentConvertedOutline.NumberOfContours);

                _previewBox.SetMetrics(options.UnitsPerEm, options.Ascender, options.Descender, options.AdvanceWidth);
                _previewBox.SetNewOutline(_currentConvertedOutline);
            }
        }
        #endregion

        #region Saving and Batch Injection
        private void OnSaveClick(object sender, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            string srcPath = _txtFontPath.Text.Trim();
            if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath))
            {
                MessageBox.Show(
                    isVi ? "Vui lòng chọn tệp phông chữ .ttf đích trước khi lưu!" : "Please select a target .ttf font file!",
                    isVi ? "Chưa chọn font" : "No font selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (_selectedCodePoint <= 0)
            {
                MessageBox.Show(
                    isVi ? "Vui lòng nhập mã điểm Unicode hợp lệ!" : "Please enter a valid Unicode code point!",
                    isVi ? "Mã Unicode không hợp lệ" : "Invalid Unicode",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (_currentConvertedOutline == null || _currentConvertedOutline.NumberOfContours == 0)
            {
                MessageBox.Show(
                    isVi ? "Chữ hiện tại không có nét vẽ vector nào để lưu!" : "Current glyph has no vector contours to save!",
                    isVi ? "Glyph rỗng" : "Empty glyph",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Xác định đường dẫn file đích (không bao giờ lặp đuôi _modified)
            string destPath = srcPath;
            if (_rbSaveNew.Checked)
            {
                destPath = TrueTypeFontWriter.GetCleanModifiedFontPath(srcPath);
            }

            try
            {
                var entry = new GlyphInjectionEntry(
                    _selectedCodePoint,
                    _currentConvertedOutline,
                    (ushort)_numAdvanceWidth.Value,
                    _currentConvertedOutline.LeftSideBearing);

                // Giải phóng reader hiện tại trước khi ghi font để tránh xung đột file lock
                if (_fontReader != null)
                {
                    _fontReader.Dispose();
                    _fontReader = null;
                }

                TrueTypeFontWriter.InjectGlyphsToFile(srcPath, destPath, new[] { entry }, true);
                LastModifiedFontPath = destPath;

                // Tự động nạp file đích vừa lưu làm font đang làm việc trong dialog,
                // giúp lần lưu tiếp theo nạp tiếp vào font này (không mất glyph cũ và không lặp đuôi _modified)
                LoadFontFile(destPath);

                string successMsg = isVi
                    ? string.Format("🎉 ĐÃ LƯU GLYPH THÀNH CÔNG!\n\n• Mã Unicode: U+{0:X4}\n• Tệp font: {1}\n\nBạn có muốn chuyển sang tab 'Khu Vực Test Font' để gõ thử ngay không?", _selectedCodePoint, destPath)
                    : string.Format("🎉 GLYPH INJECTED SUCCESSFULLY!\n\n• Unicode: U+{0:X4}\n• Font file: {1}\n\nWould you like to switch to 'Font Testing Area' tab to test typing now?", _selectedCodePoint, destPath);

                var dr = MessageBox.Show(successMsg, isVi ? "Thành công" : "Success", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (dr == DialogResult.Yes)
                {
                    if (MainForm.Instance != null)
                    {
                        MainForm.Instance.SwitchToFontTestTab(destPath);
                    }
                    Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    (isVi ? "Lỗi khi lưu ký tự vào phông chữ:\n" : "Error saving glyph to font:\n") + ex.Message,
                    isVi ? "Lỗi ghi font" : "Font Write Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnBatchPuaClick(object sender, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            string srcPath = _txtFontPath.Text.Trim();
            if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath))
            {
                MessageBox.Show(
                    isVi ? "Vui lòng chọn tệp phông chữ .ttf đích trước khi nạp hàng loạt!" : "Please select target .ttf font file first!",
                    isVi ? "Chưa chọn font" : "No font selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var puaList = IdsDictionaryStorage.Load();
            if (puaList == null || puaList.Count == 0)
            {
                MessageBox.Show(
                    isVi ? "Sổ bộ PUA hiện đang trống!" : "PUA Dictionary is empty!",
                    isVi ? "Thông báo" : "Notice",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string confirmMsg = isVi
                ? string.Format("Tìm thấy {0} ký tự trong Sổ bộ PUA.\nBạn có muốn dựng vector và nạp toàn bộ danh sách này vào font '{1}'?",
                    puaList.Count, Path.GetFileName(srcPath))
                : string.Format("Found {0} glyphs in PUA Dictionary.\nInject all into font '{1}'?",
                    puaList.Count, Path.GetFileName(srcPath));

            if (MessageBox.Show(confirmMsg, isVi ? "Xác nhận nạp hàng loạt" : "Confirm Batch Injection",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            string destPath = srcPath;
            if (_rbSaveNew.Checked)
            {
                destPath = TrueTypeFontWriter.GetCleanModifiedFontPath(srcPath);
            }

            int successCount = 0;
            var entries = new List<GlyphInjectionEntry>();
            var options = new GlyphMetricsOptions(_fontReader != null ? _fontReader.Header : null);

            for (int i = 0; i < puaList.Count; i++)
            {
                var item = puaList[i];
                int cp = ParseUnicodeCodePoint(item.PuaCode);
                if (cp <= 0 || string.IsNullOrEmpty(item.IdsString)) continue;

                try
                {
                    var ast = IdsParserService.ParseIds(item.IdsString, true);
                    if (ast == null) continue;

                    var layoutItems = IdsLayoutEngine.ComputeLayout(ast);
                    if (layoutItems == null || layoutItems.Count == 0) continue;

                    // Convert layout items to GlyphRenderData
                    var renderDataList = new List<GlyphRenderData>();
                    for (int li = 0; li < layoutItems.Count; li++)
                    {
                        var it = layoutItems[li];
                        renderDataList.Add(new GlyphRenderData
                        {
                            Char = it.Char,
                            TargetRect = it.TargetRect,
                            NodeId = it.NodeId
                        });
                    }

                    using (var combinedPath = IdsGlyphRenderer.BuildCombinedVectorPath(renderDataList, _activeFontFamily))
                    {
                        if (combinedPath != null && combinedPath.PointCount > 0)
                        {
                            var outline = VectorToGlyphConverter.ConvertPathToTrueTypeGlyph(
                                combinedPath,
                                new RectangleF(0, 0, 1000, 1000),
                                options);

                            entries.Add(new GlyphInjectionEntry(cp, outline, options.AdvanceWidth, outline.LeftSideBearing));
                            successCount++;
                        }
                    }
                }
                catch { }
            }

            if (entries.Count == 0)
            {
                MessageBox.Show(
                    isVi ? "Không có ký tự PUA nào được dựng thành công để nạp." : "No PUA glyphs could be built.",
                    isVi ? "Thông báo" : "Notice",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Giải phóng reader trước khi nạp hàng loạt
                if (_fontReader != null)
                {
                    _fontReader.Dispose();
                    _fontReader = null;
                }

                TrueTypeFontWriter.InjectGlyphsToFile(srcPath, destPath, entries, true);
                LastModifiedFontPath = destPath;
                LoadFontFile(destPath);

                MessageBox.Show(
                    isVi
                        ? string.Format("🎉 ĐÃ NẠP HÀNG LOẠT THÀNH CÔNG {0}/{1} KÝ TỰ PUA!\n\nTệp font xuất ra:\n{2}", successCount, puaList.Count, destPath)
                        : string.Format("🎉 Successfully injected {0}/{1} PUA glyphs!\n\nOutput font:\n{2}", successCount, puaList.Count, destPath),
                    isVi ? "Hoàn tất nạp hàng loạt" : "Batch Injection Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    (isVi ? "Lỗi khi nạp hàng loạt:\n" : "Batch injection error:\n") + ex.Message,
                    isVi ? "Lỗi" : "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fontReader != null)
                {
                    _fontReader.Dispose();
                    _fontReader = null;
                }
                if (_composedPath != null)
                {
                    _composedPath.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        #region Custom Em-Square Preview Control
        private class FontPreviewBox : Control
        {
            private RawGlyphOutline _newOutline;
            private RawGlyphOutline _existingOutline;

            private ushort _unitsPerEm = 1000;
            private short _ascender = 880;
            private short _descender = -120;
            private ushort _advanceWidth = 1000;

            public FontPreviewBox()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = Color.FromArgb(248, 250, 252);
            }

            public void SetMetrics(ushort em, short asc, short desc, ushort advance)
            {
                _unitsPerEm = em > 0 ? em : (ushort)1000;
                _ascender = asc;
                _descender = desc;
                _advanceWidth = advance > 0 ? advance : _unitsPerEm;
                Invalidate();
            }

            public void SetNewOutline(RawGlyphOutline outline)
            {
                _newOutline = outline;
                Invalidate();
            }

            public void SetExistingOutline(RawGlyphOutline outline)
            {
                _existingOutline = outline;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                int w = ClientSize.Width;
                int h = ClientSize.Height;
                if (w < 10 || h < 10) return;

                // Tính toán vùng Em-square hiển thị căn giữa
                float margin = 40f;
                float availW = w - margin * 2f;
                float availH = h - margin * 2f;
                float scale = Math.Min(availW / _unitsPerEm, availH / (_ascender - _descender));
                if (scale <= 0.001f) scale = 0.2f;

                float originX = (w - (_unitsPerEm * scale)) / 2f;
                // Baseline Y trong tọa độ màn hình GDI+:
                float originBaselineY = (h - ((_ascender - _descender) * scale)) / 2f + (_ascender * scale);

                // 1. Vẽ nền ô vuông Em-square
                float emSquareTopY = originBaselineY - (_ascender * scale);
                float emSquareHeight = (_ascender - _descender) * scale;
                var emRect = new RectangleF(originX, emSquareTopY, _unitsPerEm * scale, emSquareHeight);

                using (var bgBrush = new SolidBrush(Color.White))
                {
                    g.FillRectangle(bgBrush, emRect);
                }
                using (var borderPen = new Pen(Color.FromArgb(203, 213, 225), 1.5f))
                {
                    g.DrawRectangle(borderPen, emRect.X, emRect.Y, emRect.Width, emRect.Height);
                }

                // 2. Vẽ các đường gióng thông số chữ (Typographic Lines)
                // Baseline Y=0 (Nét liền đen/xám đậm)
                using (var baselinePen = new Pen(Color.FromArgb(100, 116, 139), 1.5f))
                {
                    g.DrawLine(baselinePen, 0, originBaselineY, w, originBaselineY);
                }
                // Ascender (Nét đứt xanh lá)
                float ascY = originBaselineY - (_ascender * scale);
                using (var ascPen = new Pen(Color.FromArgb(34, 197, 94), 1.2f))
                {
                    ascPen.DashStyle = DashStyle.Dash;
                    g.DrawLine(ascPen, 0, ascY, w, ascY);
                }
                // Descender (Nét đứt đỏ cam)
                float descY = originBaselineY - (_descender * scale);
                using (var descPen = new Pen(Color.FromArgb(239, 68, 68), 1.2f))
                {
                    descPen.DashStyle = DashStyle.Dash;
                    g.DrawLine(descPen, 0, descY, w, descY);
                }

                // LSB line & Advance width line (Nét đứt xanh dương)
                if (_newOutline != null)
                {
                    float lsbX = originX + (_newOutline.LeftSideBearing * scale);
                    float advX = originX + (_advanceWidth * scale);
                    using (var bluePen = new Pen(Color.FromArgb(59, 130, 246), 1.2f))
                    {
                        bluePen.DashStyle = DashStyle.Dot;
                        g.DrawLine(bluePen, lsbX, emSquareTopY - 15, lsbX, emSquareTopY + emSquareHeight + 15);
                        g.DrawLine(bluePen, advX, emSquareTopY - 15, advX, emSquareTopY + emSquareHeight + 15);
                    }
                }

                // 3. Vẽ nhãn văn bản của các đường gióng
                using (var font = new Font("Segoe UI", 8F))
                using (var brush = new SolidBrush(Color.FromArgb(100, 116, 139)))
                {
                    g.DrawString("Baseline (Y=0)", font, brush, 6, originBaselineY - 16);
                    using (var greenBrush = new SolidBrush(Color.FromArgb(22, 101, 52)))
                    {
                        g.DrawString("Ascender (" + _ascender + ")", font, greenBrush, 6, ascY - 16);
                    }
                    using (var redBrush = new SolidBrush(Color.FromArgb(185, 28, 28)))
                    {
                        g.DrawString("Descender (" + _descender + ")", font, redBrush, 6, descY + 2);
                    }
                }

                // 4. Nếu có chữ cũ trong font (Collision comparison), vẽ bóng mờ màu cam phía sau
                if (_existingOutline != null && _existingOutline.Contours != null && _existingOutline.Contours.Count > 0)
                {
                    using (var oldPath = VectorPathRenderer.CreateSmoothGraphicsPath(_existingOutline, originX, originBaselineY, scale))
                    using (var oldBrush = new SolidBrush(Color.FromArgb(60, 234, 88, 12))) // Cam nhạt trong suốt
                    using (var oldPen = new Pen(Color.FromArgb(160, 234, 88, 12), 1f))
                    {
                        oldPen.DashStyle = DashStyle.Dash;
                        g.FillPath(oldBrush, oldPath);
                        g.DrawPath(oldPen, oldPath);
                    }
                }

                // 5. Vẽ Glyph mới từ IDS (Nét mực đen đậm chuẩn, đường cong Bézier mượt mà)
                if (_newOutline != null && _newOutline.Contours != null && _newOutline.Contours.Count > 0)
                {
                    using (var newPath = VectorPathRenderer.CreateSmoothGraphicsPath(_newOutline, originX, originBaselineY, scale))
                    using (var inkBrush = new SolidBrush(Color.FromArgb(15, 23, 42))) // Slate 900
                    {
                        g.FillPath(inkBrush, newPath);
                    }
                }
            }
        }
        #endregion
    }
}
