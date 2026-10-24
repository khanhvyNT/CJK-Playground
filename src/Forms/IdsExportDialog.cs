using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Forms
{
    public class IdsExportDialog : Form
    {
        private readonly List<GlyphRenderData> _renderItems;
        private readonly FontFamily _activeFont;
        private readonly IdsColorTheme _theme;

        private TabControl _tabExports;
        private TextBox _txtSvgPreview;
        private CheckBox _chkSvgBg;
        private ComboBox _cbPngResolution;
        private CheckBox _chkPngBg;
        private PictureBox _pbPreview;

        public IdsExportDialog(List<GlyphRenderData> items, FontFamily activeFont, IdsColorTheme theme)
        {
            _renderItems = items ?? new List<GlyphRenderData>();
            _activeFont = activeFont ?? IdsGlyphRenderer.ResolveFontFamily();
            _theme = theme;

            InitializeComponent();
            UpdatePreviews();
        }

        private void InitializeComponent()
        {
            var appIcon = AppIconHelper.GetAppIcon();
            if (appIcon != null) Icon = appIcon;

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            Text = isVi ? "Xuất ký tự IDS (Export Vector & Bitmap)" : "Export IDS Glyph";
            Size = new Size(780, 560);
            MinimumSize = new Size(680, 480);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            _tabExports = new TabControl { Dock = DockStyle.Fill };

            // Tab 1: Vector SVG
            var tabSvg = new TabPage(isVi ? "1. Vector SVG (Chuẩn W3C)" : "1. Vector SVG (W3C Standard)");
            var svgPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };

            var topSvgBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight
            };

            _chkSvgBg = new CheckBox
            {
                Text = isVi ? "Bao gồm màu nền giấy xuyến" : "Include paper background color",
                AutoSize = true,
                Margin = new Padding(0, 6, 16, 0)
            };
            _chkSvgBg.CheckedChanged += (s, e) => UpdatePreviews();

            var btnCopySvg = new Button
            {
                Text = isVi ? "📋 Sao chép mã SVG" : "📋 Copy SVG Code",
                Width = 145,
                Height = 28
            };
            btnCopySvg.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_txtSvgPreview.Text))
                {
                    Clipboard.SetText(_txtSvgPreview.Text);
                    MessageBox.Show(isVi ? "Đã sao chép mã SVG vào bộ nhớ tạm!" : "Copied SVG code to clipboard!",
                        isVi ? "Thành công" : "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            var btnSaveSvg = new Button
            {
                Text = isVi ? "💾 Lưu tệp .svg" : "💾 Save .svg File",
                Width = 130,
                Height = 28,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnSaveSvg.Click += OnSaveSvg;

            topSvgBar.Controls.AddRange(new Control[] { _chkSvgBg, btnCopySvg, btnSaveSvg });

            _txtSvgPreview = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9F),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            svgPnl.Controls.Add(_txtSvgPreview);
            svgPnl.Controls.Add(topSvgBar);
            tabSvg.Controls.Add(svgPnl);

            // Tab 2: Bitmap PNG
            var tabPng = new TabPage(isVi ? "2. Ảnh Bitmap PNG (Độ nét cao)" : "2. Bitmap PNG (High-Res)");
            var pngPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };

            var topPngBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight
            };

            var lblRes = new Label
            {
                Text = isVi ? "Độ phân giải:" : "Resolution:",
                AutoSize = true,
                Margin = new Padding(0, 8, 8, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            _cbPngResolution = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 130,
                Margin = new Padding(0, 4, 16, 0)
            };
            _cbPngResolution.Items.AddRange(new object[] { "128 x 128", "256 x 256", "512 x 512", "1024 x 1024", "2048 x 2048" });
            _cbPngResolution.SelectedIndex = 2; // 512x512
            _cbPngResolution.SelectedIndexChanged += (s, e) => UpdatePreviews();

            _chkPngBg = new CheckBox
            {
                Text = isVi ? "Nền màu chủ đề (Bỏ chọn = Nền trong suốt)" : "Theme background (Uncheck = Transparent)",
                AutoSize = true,
                Margin = new Padding(0, 7, 16, 0),
                Checked = false
            };
            _chkPngBg.CheckedChanged += (s, e) => UpdatePreviews();

            var btnCopyPng = new Button
            {
                Text = isVi ? "📋 Sao chép ảnh PNG" : "📋 Copy PNG Image",
                Width = 150,
                Height = 30
            };
            btnCopyPng.Click += OnCopyPng;

            var btnSavePng = new Button
            {
                Text = isVi ? "💾 Lưu tệp .png" : "💾 Save .png File",
                Width = 130,
                Height = 30,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnSavePng.Click += OnSavePng;

            topPngBar.Controls.AddRange(new Control[] { lblRes, _cbPngResolution, _chkPngBg, btnCopyPng, btnSavePng });

            _pbPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.FixedSingle
            };

            pngPnl.Controls.Add(_pbPreview);
            pngPnl.Controls.Add(topPngBar);
            tabPng.Controls.Add(pngPnl);

            _tabExports.TabPages.Add(tabSvg);
            _tabExports.TabPages.Add(tabPng);

            // Bottom Buttons
            var bottomBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(241, 245, 249)
            };

            var btnClose = new Button { Text = isVi ? "Đóng" : "Close", Width = 85, Height = 32 };
            btnClose.Click += (s, e) => Close();
            bottomBar.Controls.Add(btnClose);

            Controls.Add(_tabExports);
            Controls.Add(bottomBar);
        }

        private void UpdatePreviews()
        {
            var themeCfg = IdsCanvasThemeConfig.GetTheme(_theme);

            // 1. SVG
            string svgBg = _chkSvgBg.Checked
                ? string.Format("#{0:X2}{1:X2}{2:X2}", themeCfg.BgColor.R, themeCfg.BgColor.G, themeCfg.BgColor.B)
                : "transparent";
            string inkColorHex = string.Format("#{0:X2}{1:X2}{2:X2}", themeCfg.InkColor.R, themeCfg.InkColor.G, themeCfg.InkColor.B);

            string svg = IdsGlyphRenderer.GenerateFullSvg(_renderItems, _activeFont, 1000, 1000, inkColorHex, svgBg);
            _txtSvgPreview.Text = svg;

            // 2. PNG
            int res = 512;
            switch (_cbPngResolution.SelectedIndex)
            {
                case 0: res = 128; break;
                case 1: res = 256; break;
                case 2: res = 512; break;
                case 3: res = 1024; break;
                case 4: res = 2048; break;
            }

            Color bg = _chkPngBg.Checked ? themeCfg.BgColor : Color.Transparent;
            if (_pbPreview.Image != null)
            {
                var old = _pbPreview.Image;
                _pbPreview.Image = null;
                old.Dispose();
            }

            // Thumbnail for preview box
            var bmp = IdsGlyphRenderer.RenderHighResBitmap(_renderItems, _activeFont, Math.Min(res, 400), themeCfg.InkColor, bg);
            _pbPreview.Image = bmp;
        }

        private void OnSaveSvg(object s, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = isVi ? "Tệp vector SVG (*.svg)|*.svg" : "SVG Vector File (*.svg)|*.svg";
                sfd.FileName = "cjk_ids_glyph.svg";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(sfd.FileName, _txtSvgPreview.Text, Encoding.UTF8);
                    MessageBox.Show(isVi ? "Đã lưu tệp vector SVG thành công!" : "Saved SVG vector file successfully!",
                        isVi ? "Thành công" : "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void OnCopyPng(object s, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            var themeCfg = IdsCanvasThemeConfig.GetTheme(_theme);
            Color bg = _chkPngBg.Checked ? themeCfg.BgColor : Color.Transparent;
            using (var bmp = IdsGlyphRenderer.RenderHighResBitmap(_renderItems, _activeFont, 512, themeCfg.InkColor, bg))
            {
                Clipboard.SetImage(bmp);
                MessageBox.Show(isVi ? "Đã sao chép ảnh PNG 512x512 vào Clipboard!" : "Copied 512x512 PNG image to clipboard!",
                    isVi ? "Thành công" : "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnSavePng(object s, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            var themeCfg = IdsCanvasThemeConfig.GetTheme(_theme);

            int res = 1024;
            switch (_cbPngResolution.SelectedIndex)
            {
                case 0: res = 128; break;
                case 1: res = 256; break;
                case 2: res = 512; break;
                case 3: res = 1024; break;
                case 4: res = 2048; break;
            }

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = isVi ? "Tệp ảnh PNG (*.png)|*.png" : "PNG Image File (*.png)|*.png";
                sfd.FileName = string.Format("cjk_ids_glyph_{0}x{0}.png", res);
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    Color bg = _chkPngBg.Checked ? themeCfg.BgColor : Color.Transparent;
                    using (var bmp = IdsGlyphRenderer.RenderHighResBitmap(_renderItems, _activeFont, res, themeCfg.InkColor, bg))
                    {
                        bmp.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    MessageBox.Show(isVi ? "Đã lưu ảnh PNG thành công!" : "Saved PNG image file successfully!",
                        isVi ? "Thành công" : "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
    }
}
