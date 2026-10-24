using System;
using System.Drawing;
using System.Windows.Forms;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Forms
{
    public class IdsPresetGalleryDialog : Form
    {
        private ListView _lvPresets;
        private TextBox _txtSearch;
        private ComboBox _cbCategory;
        private TextBox _txtDetails;
        private Button _btnSelect;
        private Button _btnClose;

        public IdsPreset SelectedPreset { get; private set; }

        public IdsPresetGalleryDialog()
        {
            InitializeComponent();
            PopulateCategories();
            FilterPresets();
        }

        private void InitializeComponent()
        {
            var appIcon = AppIconHelper.GetAppIcon();
            if (appIcon != null) Icon = appIcon;

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            Text = isVi ? "Thư viện chữ mẫu IDS (Preset Gallery)" : "IDS Preset Gallery";
            Size = new Size(880, 580);
            MinimumSize = new Size(740, 480);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            // Top Filter Panel
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var lblCat = new Label
            {
                Text = isVi ? "Chủ đề:" : "Category:",
                AutoSize = true,
                Location = new Point(12, 14),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            _cbCategory = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(75, 11),
                Width = 220
            };
            _cbCategory.SelectedIndexChanged += (s, e) => FilterPresets();

            var lblSearch = new Label
            {
                Text = isVi ? "Tìm kiếm:" : "Search:",
                AutoSize = true,
                Location = new Point(315, 14),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            _txtSearch = new TextBox
            {
                Location = new Point(385, 11),
                Width = 250
            };
            _txtSearch.TextChanged += (s, e) => FilterPresets();

            topPanel.Controls.AddRange(new Control[] { lblCat, _cbCategory, lblSearch, _txtSearch });

            // Split Container for List and Details
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2
            };

            Shown += (s, e) =>
            {
                try
                {
                    if (split.Width > 500)
                    {
                        int target = split.Width - 300;
                        if (target >= split.Panel1MinSize && target <= split.Width - split.Panel2MinSize)
                        {
                            split.SplitterDistance = target;
                        }
                    }
                }
                catch { }
            };

            _lvPresets = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9.5F)
            };
            _lvPresets.Columns.Add(isVi ? "Ký tự" : "Glyph", 70);
            _lvPresets.Columns.Add(isVi ? "Tên chữ mẫu" : "Preset Name", 210);
            _lvPresets.Columns.Add(isVi ? "Công thức IDS" : "IDS Formula", 150);
            _lvPresets.Columns.Add(isVi ? "Phiên âm" : "Reading", 100);

            _lvPresets.SelectedIndexChanged += (s, e) => ShowPresetDetails();
            _lvPresets.DoubleClick += (s, e) => { OnSelectPreset(); };

            split.Panel1.Controls.Add(_lvPresets);

            // Right Panel: Details
            var rightPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            var lblDetHead = new Label
            {
                Text = isVi ? "Chi tiết chữ mẫu:" : "Preset Details:",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Height = 24
            };
            _txtDetails = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(248, 250, 252),
                Font = new Font("Segoe UI", 9.5F)
            };
            rightPnl.Controls.Add(_txtDetails);
            rightPnl.Controls.Add(lblDetHead);
            split.Panel2.Controls.Add(rightPnl);

            // Bottom Buttons
            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(241, 245, 249)
            };

            _btnClose = new Button
            {
                Text = isVi ? "Đóng" : "Close",
                Width = 85,
                Height = 32
            };
            _btnClose.Click += (s, e) => Close();

            _btnSelect = new Button
            {
                Text = isVi ? "Chọn chữ mẫu này" : "Load Preset",
                Width = 145,
                Height = 32,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSelect.Click += (s, e) => OnSelectPreset();

            bottomPanel.Controls.AddRange(new Control[] { _btnClose, _btnSelect });

            Controls.Add(split);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);
        }

        private void PopulateCategories()
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            _cbCategory.Items.Clear();
            _cbCategory.Items.Add(isVi ? "Tất cả danh mục" : "All Categories");
            _cbCategory.Items.Add(isVi ? "Chữ Nôm Việt Nam" : "Vietnamese Nom");
            _cbCategory.Items.Add(isVi ? "Chữ CJK Kinh Điển" : "CJK Classics");
            _cbCategory.Items.Add(isVi ? "Chữ Đại Tự & Phức Tạp" : "Complex Monuments");
            _cbCategory.SelectedIndex = 0;
        }

        private void FilterPresets()
        {
            string q = _txtSearch.Text.Trim().ToLowerInvariant();
            int catIdx = _cbCategory.SelectedIndex;

            _lvPresets.Items.Clear();
            _lvPresets.BeginUpdate();

            var fontCjk = new Font("SimSun", 14F, FontStyle.Regular);

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            foreach (var p in IdsPresetService.Presets)
            {
                if (catIdx == 1 && p.Category != "vietnamese_nom") continue;
                if (catIdx == 2 && p.Category != "cjk_classic") continue;
                if (catIdx == 3 && p.Category != "complex_monuments") continue;

                string dispName = p.GetDisplayName(isVi);
                string dispMeaning = p.GetDisplayMeaning(isVi);

                if (!string.IsNullOrEmpty(q))
                {
                    bool match = dispName.ToLowerInvariant().Contains(q) ||
                                 p.Ids.ToLowerInvariant().Contains(q) ||
                                 p.Reading.ToLowerInvariant().Contains(q) ||
                                 dispMeaning.ToLowerInvariant().Contains(q);
                    if (!match) continue;
                }

                var lvi = new ListViewItem(p.ResultChar ?? "□");
                lvi.Font = fontCjk;
                lvi.SubItems.Add(dispName);
                lvi.SubItems.Add(p.Ids);
                lvi.SubItems.Add(p.Reading);
                lvi.Tag = p;
                _lvPresets.Items.Add(lvi);
            }

            _lvPresets.EndUpdate();
            if (_lvPresets.Items.Count > 0)
            {
                _lvPresets.Items[0].Selected = true;
            }
            else
            {
                _txtDetails.Text = "";
            }
        }

        private void ShowPresetDetails()
        {
            if (_lvPresets.SelectedItems.Count == 0) return;
            var p = _lvPresets.SelectedItems[0].Tag as IdsPreset;
            if (p == null) return;

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            string s = string.Format(
                (isVi ? "Tên mẫu: {0}\r\n" +
                        "Ký tự kết quả: {1}\r\n" +
                        "Công thức IDS: {2}\r\n" +
                        "Mã PUA gợi ý: {3}\r\n" +
                        "Âm đọc: {4}\r\n\r\n" +
                        "Ý nghĩa / Cấu tạo:\r\n{5}"
                      : "Preset: {0}\r\n" +
                        "Result Glyph: {1}\r\n" +
                        "IDS Formula: {2}\r\n" +
                        "Suggested PUA: {3}\r\n" +
                        "Reading: {4}\r\n\r\n" +
                        "Meaning / Composition:\r\n{5}"),
                p.GetDisplayName(isVi), p.ResultChar, p.Ids, p.PuaCode, p.Reading, p.GetDisplayMeaning(isVi));

            _txtDetails.Text = s;
        }

        private void OnSelectPreset()
        {
            if (_lvPresets.SelectedItems.Count == 0) return;
            SelectedPreset = _lvPresets.SelectedItems[0].Tag as IdsPreset;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
