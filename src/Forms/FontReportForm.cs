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
    public class FontReportForm : Form
    {
        private readonly FontLoaderService _fontService;
        private ListView _lvBlocks;
        private TextBox _txtMetadata;
        private RadioButton _rbSingleFont;
        private RadioButton _rbSystemComposite;

        public FontReportForm(FontLoaderService fontService)
        {
            _fontService = fontService;
            InitializeComponent();
            GenerateReport();
        }

        private void InitializeComponent()
        {
            var appIcon = AppIconHelper.GetAppIcon();
            if (appIcon != null) Icon = appIcon;

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            Text = LocalizationManager.Get("ReportTitle", "Báo cáo độ phủ font (Font Coverage Report)");
            Size = new Size(860, 650);
            MinimumSize = new Size(720, 500);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);

            // Mode Selector Bar (Top)
            var modePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblMode = new Label
            {
                Text = LocalizationManager.Get("ReportModeLabel", "Chế độ phân tích độ phủ:"),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(12, 12),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            string activeFontName = (_fontService.CurrentFont != null && !string.IsNullOrEmpty(_fontService.CurrentFont.FamilyName))
                ? _fontService.CurrentFont.FamilyName
                : (isVi ? "Font đang chọn" : "Active Font");

            _rbSingleFont = new RadioButton
            {
                Text = string.Format(LocalizationManager.Get("ReportModeSingleFmt", "1. Font hiện tại ({0})"), activeFontName),
                AutoSize = true,
                Location = new Point(190, 10),
                Checked = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
            _rbSingleFont.CheckedChanged += (s, e) => { if (_rbSingleFont.Checked) GenerateReport(); };

            _rbSystemComposite = new RadioButton
            {
                Text = LocalizationManager.Get("ReportModeSystem", "2. Toàn bộ Font hệ thống Windows (Composite System)"),
                AutoSize = true,
                Location = new Point(460, 10),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
            _rbSystemComposite.CheckedChanged += (s, e) => { if (_rbSystemComposite.Checked) GenerateReport(); };

            modePanel.Controls.AddRange(new Control[] { lblMode, _rbSingleFont, _rbSystemComposite });

            // Top Header: Metadata
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 140, Padding = new Padding(12, 8, 12, 8) };
            _txtMetadata = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            topPanel.Controls.Add(_txtMetadata);

            // Center: ListView
            _lvBlocks = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9.5F)
            };
            _lvBlocks.Columns.Add(LocalizationManager.Get("ReportColBlock", "Khối Unicode"), 280);
            _lvBlocks.Columns.Add(LocalizationManager.Get("ReportColRange", "Dải mã"), 150);
            _lvBlocks.Columns.Add(LocalizationManager.Get("ReportColChars", "Ký tự có / Tổng"), 130);
            _lvBlocks.Columns.Add(LocalizationManager.Get("ReportColCoverage", "Độ phủ (%)"), 110);
            _lvBlocks.Columns.Add(LocalizationManager.Get("ReportColPlane", "Mặt phẳng"), 90);

            // Bottom Buttons
            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };

            var btnClose = new Button { Text = LocalizationManager.Get("BtnClose", "Đóng"), Width = 80, Height = 32 };
            btnClose.Click += (s, e) => Close();

            var btnSaveReport = new Button { Text = LocalizationManager.Get("ReportSaveBtn", "💾 Lưu báo cáo (.md)"), Width = 150, Height = 32 };
            btnSaveReport.Click += OnSaveReport;

            var btnCopyReport = new Button { Text = LocalizationManager.Get("ReportCopyBtn", "📋 Sao chép báo cáo"), Width = 150, Height = 32 };
            btnCopyReport.Click += (s, e) =>
            {
                string text = BuildMarkdownReport();
                Clipboard.SetText(text);
                string copyMsg = isVi ? "Đã sao chép nội dung báo cáo độ phủ vào Clipboard!" : "Copied font coverage report to clipboard!";
                string copyTitle = isVi ? "Thành công" : "Success";
                MessageBox.Show(copyMsg, copyTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            bottomPanel.Controls.AddRange(new Control[] { btnClose, btnSaveReport, btnCopyReport });

            Controls.Add(_lvBlocks);
            Controls.Add(topPanel);
            Controls.Add(modePanel);
            Controls.Add(bottomPanel);
        }

        private void GenerateReport()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            bool isSystemMode = _rbSystemComposite != null && _rbSystemComposite.Checked;
            var font = _fontService.CurrentFont;
            var reader = _fontService.OpenTypeReader;

            HashSet<int> supportedSet;
            var sb = new StringBuilder();

            if (!isSystemMode)
            {
                // Mode 1: Single Font
                supportedSet = reader != null ? reader.GetSupportedCodePoints() : new HashSet<int>();

                sb.AppendLine(isVi ? "=== CHẾ ĐỘ 1: ĐỘ PHỦ THEO FONT ĐƠN LẺ (SINGLE FONT) ===" : "=== MODE 1: SINGLE ACTIVE FONT COVERAGE ===");
                sb.AppendLine(string.Format(isVi ? "Tên họ font (Family): {0}" : "Font Family: {0}", font != null ? font.FamilyName : "--"));
                if (reader != null)
                {
                    sb.AppendLine(string.Format(isVi ? "Tên đầy đủ: {0} | Subfamily: {1}" : "Full Name: {0} | Subfamily: {1}", reader.Header.FullName, reader.Header.SubfamilyName));
                    sb.AppendLine(string.Format(isVi ? "Phiên bản: {0} | Bản quyền: {1}" : "Version: {0} | Copyright: {1}", reader.Header.Version, reader.Header.Copyright));
                    sb.AppendLine(string.Format("UnitsPerEm: {0} | Ascender: {1} | Descender: {2} | LineGap: {3}",
                        reader.Header.UnitsPerEm, reader.Header.Ascender, reader.Header.Descender, reader.Header.LineGap));
                    sb.AppendLine(string.Format(isVi ? "Tổng số glyphs: {0:N0} | Ký tự Unicode ánh xạ trong font: {1:N0}" : "Total Glyphs: {0:N0} | Mapped Unicode Characters: {1:N0}",
                        reader.Header.NumGlyphs, reader.SupportedCharacterCount));
                }
                else
                {
                    sb.AppendLine(isVi ? "Không có thông tin OpenType chi tiết cho font này." : "No OpenType metadata available for this font.");
                }
            }
            else
            {
                // Mode 2: System Composite (All Windows CJK fonts)
                List<string> scannedFonts;
                supportedSet = _fontService.GetSystemCompositeSupportedCodePoints(out scannedFonts);

                sb.AppendLine(isVi ? "=== CHẾ ĐỘ 2: ĐỘ PHỦ TỔNG HỢP TOÀN BỘ FONT WINDOWS (SYSTEM COMPOSITE) ===" : "=== MODE 2: WINDOWS SYSTEM COMPOSITE FONT COVERAGE ===");
                sb.AppendLine(string.Format(isVi ? "Tổng số ký tự Unicode độc bản được hỗ trợ toàn hệ thống: {0:N0} ký tự" : "Total unique Unicode characters supported across system: {0:N0}", supportedSet.Count));
                sb.AppendLine(string.Format(isVi ? "Danh sách {0} font Windows đã được quét và hợp nhất:" : "List of {0} scanned and merged Windows fonts:", scannedFonts.Count));
                foreach (var sf in scannedFonts)
                {
                    sb.AppendLine("  • " + sf);
                }
            }

            _txtMetadata.Text = sb.ToString();

            // Populate Blocks
            _lvBlocks.Items.Clear();
            _lvBlocks.BeginUpdate();

            foreach (var block in UnicodeCatalog.Blocks)
            {
                int present = 0;
                int total = block.TotalCharacters;

                if (supportedSet.Count > 0)
                {
                    for (int cp = block.StartCp; cp <= block.EndCp; cp++)
                    {
                        if (supportedSet.Contains(cp)) present++;
                    }
                }

                double pct = total > 0 ? (present * 100.0 / total) : 0;

                var lvi = new ListViewItem(block.Name);
                lvi.SubItems.Add(block.RangeString);
                lvi.SubItems.Add(string.Format("{0:N0} / {1:N0}", present, total));
                lvi.SubItems.Add(string.Format("{0:F1} %", pct));
                lvi.SubItems.Add(block.PlaneName);

                if (pct >= 99.0) lvi.ForeColor = Color.DarkGreen;
                else if (pct >= 50.0) lvi.ForeColor = Color.DarkBlue;
                else if (pct > 0.0) lvi.ForeColor = Color.FromArgb(180, 83, 9); // Amber
                else lvi.ForeColor = Color.FromArgb(148, 163, 184); // Gray

                _lvBlocks.Items.Add(lvi);
            }

            _lvBlocks.EndUpdate();
        }

        private string BuildMarkdownReport()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            bool isSystemMode = _rbSystemComposite != null && _rbSystemComposite.Checked;
            var font = _fontService.CurrentFont;
            var reader = _fontService.OpenTypeReader;

            var sb = new StringBuilder();
            sb.AppendLine("# CJK Playground Font Coverage Report");
            sb.AppendLine(string.Format(isVi ? "- **Chế độ báo cáo:** {0}" : "- **Report Mode:** {0}",
                isSystemMode
                    ? (isVi ? "Độ phủ tổng hợp toàn bộ Font hệ thống Windows (Composite System)" : "Windows System Composite Coverage")
                    : (isVi ? "Độ phủ của Font đơn lẻ (Single Active Font)" : "Single Active Font Coverage")));
            if (!isSystemMode)
            {
                sb.AppendLine(string.Format(isVi ? "- **Tên font:** {0}" : "- **Font Family:** {0}", font != null ? font.FamilyName : "--"));
                if (reader != null)
                {
                    sb.AppendLine(string.Format("- **UnitsPerEm:** {0}", reader.Header.UnitsPerEm));
                    sb.AppendLine(string.Format(isVi ? "- **Tổng số glyphs:** {0:N0}" : "- **Total Glyphs:** {0:N0}", reader.Header.NumGlyphs));
                    sb.AppendLine(string.Format(isVi ? "- **Ký tự ánh xạ:** {0:N0}" : "- **Mapped Characters:** {0:N0}", reader.SupportedCharacterCount));
                    sb.AppendLine(string.Format(isVi ? "- **Phiên bản:** {0}" : "- **Version:** {0}", reader.Header.Version));
                }
            }
            else
            {
                List<string> scanned;
                var set = _fontService.GetSystemCompositeSupportedCodePoints(out scanned);
                sb.AppendLine(string.Format(isVi ? "- **Tổng ký tự độc bản được hỗ trợ:** {0:N0}" : "- **Total unique characters supported:** {0:N0}", set.Count));
                sb.AppendLine(string.Format(isVi ? "- **Số lượng font Windows hợp nhất:** {0}" : "- **Number of merged Windows fonts:** {0}", scanned.Count));
                sb.AppendLine((isVi ? "- **Các font Windows:** " : "- **Windows Fonts:** ") + string.Join("; ", scanned));
            }
            sb.AppendLine(string.Format(isVi ? "- **Thời gian báo cáo:** {0}" : "- **Report Timestamp:** {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine();
            if (isVi)
            {
                sb.AppendLine("| Khối Unicode | Dải mã | Ký tự có / Tổng | Tỷ lệ (%) | Mặt phẳng |");
                sb.AppendLine("| :--- | :---: | :---: | :---: | :--- |");
            }
            else
            {
                sb.AppendLine("| Unicode Block | Code Range | Supported / Total | Coverage (%) | Plane |");
                sb.AppendLine("| :--- | :---: | :---: | :---: | :--- |");
            }

            foreach (ListViewItem lvi in _lvBlocks.Items)
            {
                sb.AppendLine(string.Format("| {0} | `{1}` | {2} | **{3}** | {4} |",
                    lvi.Text, lvi.SubItems[1].Text, lvi.SubItems[2].Text, lvi.SubItems[3].Text, lvi.SubItems[4].Text));
            }

            return sb.ToString();
        }

        private void OnSaveReport(object s, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = isVi ? "Báo cáo Markdown (*.md)|*.md|Tệp văn bản (*.txt)|*.txt" : "Markdown Report (*.md)|*.md|Text File (*.txt)|*.txt";
                sfd.FileName = "cjk_playground_font_coverage.md";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    string md = BuildMarkdownReport();
                    File.WriteAllText(sfd.FileName, md, Encoding.UTF8);
                    string msg = isVi ? "Đã lưu tệp báo cáo độ phủ font!" : "Saved font coverage report file successfully!";
                    string title = isVi ? "Thành công" : "Success";
                    MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
    }
}
