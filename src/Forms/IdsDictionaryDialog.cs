using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Forms
{
    public class IdsDictionaryDialog : Form
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private readonly string _currentIds;
        private readonly IdsNode _currentAst;
        private readonly List<GlyphRenderData> _renderItems;
        private readonly FontFamily _activeFont;
        private readonly IdsColorTheme _theme;

        private TabControl _tabMain;
        private ListView _lvSaved;
        private TextBox _txtSearch;
        private TextBox _txtName;
        private TextBox _txtPua;
        private TextBox _txtReading;
        private TextBox _txtMeaning;
        private PictureBox _pbPreview;

        public SavedCustomGlyph LoadedGlyph { get; private set; }

        public IdsDictionaryDialog(
            string currentIds,
            IdsNode currentAst,
            List<GlyphRenderData> renderItems,
            FontFamily activeFont,
            IdsColorTheme theme,
            bool openToSaveTab = false)
        {
            _currentIds = currentIds ?? "";
            _currentAst = currentAst;
            _renderItems = renderItems ?? new List<GlyphRenderData>();
            _activeFont = activeFont ?? IdsGlyphRenderer.ResolveFontFamily();
            _theme = theme;

            InitializeComponent();
            PopulateSavedList();

            if (openToSaveTab && _tabMain.TabPages.Count > 0)
            {
                _tabMain.SelectedIndex = 0;
            }
            else if (_tabMain.TabPages.Count > 1)
            {
                _tabMain.SelectedIndex = 1;
            }
        }

        private void InitializeComponent()
        {
            var appIcon = AppIconHelper.GetAppIcon();
            if (appIcon != null) Icon = appIcon;

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            Text = isVi ? "Từ điển chữ tự chế & Sổ bộ PUA (Glyph Dictionary)" : "Custom Glyph Dictionary & PUA Registry";
            Size = new Size(820, 540);
            MinimumSize = new Size(700, 440);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            _tabMain = new TabControl { Dock = DockStyle.Fill };

            // Tab 1: Save current
            var tabSave = new TabPage(isVi ? "Lưu chữ hiện tại vào từ điển" : "Save Current Glyph");
            var savePnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };

            var pnlForm = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            var lblIntro = new Label
            {
                Text = isVi ? "Đăng ký ký tự tự chế vào Sổ bộ PUA nội bộ (Private Use Area U+E000 - U+F8FF):" : "Register custom glyph into internal PUA Registry (Private Use Area):",
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 260,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(0, 8, 0, 0)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Name
            table.Controls.Add(new Label { Text = isVi ? "Tên gọi chữ:" : "Glyph Name:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _txtName = new TextBox { Dock = DockStyle.Fill, Text = !string.IsNullOrEmpty(_currentIds) ? (isVi ? "Chữ " + _currentIds : "Glyph " + _currentIds) : "" };
            table.Controls.Add(_txtName, 1, 0);

            // IDS Formula (ReadOnly)
            table.Controls.Add(new Label { Text = isVi ? "Công thức IDS:" : "IDS Formula:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            var txtFormula = new TextBox { Dock = DockStyle.Fill, Text = _currentIds, ReadOnly = true, BackColor = Color.FromArgb(248, 250, 252) };
            table.Controls.Add(txtFormula, 1, 1);

            // PUA Code
            table.Controls.Add(new Label { Text = isVi ? "Mã PUA gán:" : "Assigned PUA:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            int count = IdsDictionaryStorage.Load().Count;
            _txtPua = new TextBox { Dock = DockStyle.Fill, Text = string.Format("U+{0:X4}", 0xE000 + count) };
            table.Controls.Add(_txtPua, 1, 2);

            // Reading
            table.Controls.Add(new Label { Text = isVi ? "Phiên âm / Âm đọc:" : "Reading:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            _txtReading = new TextBox { Dock = DockStyle.Fill };
            table.Controls.Add(_txtReading, 1, 3);

            // Meaning
            table.Controls.Add(new Label { Text = isVi ? "Ý nghĩa / Giải thích:" : "Meaning / Note:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            _txtMeaning = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 60 };
            table.Controls.Add(_txtMeaning, 1, 4);

            var pnlSaveBtn = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(130, 8, 0, 0) };
            var btnDoSave = new Button
            {
                Text = isVi ? "💾 Lưu chữ này vào từ điển" : "💾 Save to Dictionary",
                Width = 200,
                Height = 34,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnDoSave.Click += OnSaveCurrentGlyph;
            pnlSaveBtn.Controls.Add(btnDoSave);

            pnlForm.Controls.Add(pnlSaveBtn);
            pnlForm.Controls.Add(table);
            pnlForm.Controls.Add(lblIntro);

            // Right preview in Tab 1
            var rightPreview = new Panel { Dock = DockStyle.Right, Width = 160, Padding = new Padding(8) };
            _pbPreview = new PictureBox
            {
                Dock = DockStyle.Top,
                Height = 160,
                SizeMode = PictureBoxSizeMode.CenterImage,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(252, 248, 238)
            };
            var themeCfg = IdsCanvasThemeConfig.GetTheme(_theme);
            _pbPreview.Image = IdsGlyphRenderer.RenderHighResBitmap(_renderItems, _activeFont, 160, themeCfg.InkColor, themeCfg.BgColor);
            rightPreview.Controls.Add(_pbPreview);

            savePnl.Controls.Add(pnlForm);
            savePnl.Controls.Add(rightPreview);
            tabSave.Controls.Add(savePnl);

            // Tab 2: Browse saved
            var tabBrowse = new TabPage(isVi ? "Danh mục chữ tự chế đã lưu" : "Saved Custom Glyphs");
            var browsePnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };

            var topBar = new Panel { Dock = DockStyle.Top, Height = 38 };
            var lblFind = new Label { Text = isVi ? "Tìm kiếm:" : "Search:", AutoSize = true, Location = new Point(0, 10), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _txtSearch = new TextBox { Location = new Point(68, 7), Width = 260 };
            _txtSearch.TextChanged += (s, e) => PopulateSavedList();

            var btnImport = new Button { Text = isVi ? "📥 Nhập JSON..." : "📥 Import JSON...", Location = new Point(340, 6), Width = 120, Height = 28 };
            btnImport.Click += OnImportJson;
            var btnExport = new Button { Text = isVi ? "📤 Xuất JSON..." : "📤 Export JSON...", Location = new Point(470, 6), Width = 120, Height = 28 };
            btnExport.Click += OnExportJson;

            topBar.Controls.AddRange(new Control[] { lblFind, _txtSearch, btnImport, btnExport });

            _lvSaved = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9.5F)
            };
            _lvSaved.Columns.Add(isVi ? "Tên chữ" : "Name", 180);
            _lvSaved.Columns.Add(isVi ? "Công thức IDS" : "IDS", 130);
            _lvSaved.Columns.Add(isVi ? "Mã PUA" : "PUA Code", 90);
            _lvSaved.Columns.Add(isVi ? "Phiên âm" : "Reading", 110);
            _lvSaved.Columns.Add(isVi ? "Ý nghĩa" : "Meaning", 200);

            _lvSaved.DoubleClick += (s, e) => OnLoadSelected();

            var actionBrowseBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };

            var btnDelete = new Button { Text = isVi ? "🗑️ Xóa đã chọn" : "🗑️ Delete Selected", Width = 130, Height = 32 };
            btnDelete.Click += OnDeleteSelected;

            var btnLoad = new Button
            {
                Text = isVi ? "🎨 Nạp vào bàn vẽ" : "🎨 Load to Stage",
                Width = 145,
                Height = 32,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnLoad.Click += (s, e) => OnLoadSelected();

            actionBrowseBar.Controls.AddRange(new Control[] { btnDelete, btnLoad });

            browsePnl.Controls.Add(_lvSaved);
            browsePnl.Controls.Add(topBar);
            browsePnl.Controls.Add(actionBrowseBar);
            tabBrowse.Controls.Add(browsePnl);

            _tabMain.TabPages.Add(tabSave);
            _tabMain.TabPages.Add(tabBrowse);

            // Bottom Dialog Bar
            var bottomBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(241, 245, 249)
            };
            var btnClose = new Button { Text = isVi ? "Đóng" : "Close", Width = 85, Height = 30 };
            btnClose.Click += (s, e) => Close();
            bottomBar.Controls.Add(btnClose);

            Controls.Add(_tabMain);
            Controls.Add(bottomBar);
        }

        private void PopulateSavedList()
        {
            string q = _txtSearch.Text.Trim().ToLowerInvariant();
            var list = IdsDictionaryStorage.Load();

            _lvSaved.Items.Clear();
            _lvSaved.BeginUpdate();

            foreach (var item in list)
            {
                if (!string.IsNullOrEmpty(q))
                {
                    bool match = (item.Name != null && item.Name.ToLowerInvariant().Contains(q)) ||
                                 (item.IdsString != null && item.IdsString.ToLowerInvariant().Contains(q)) ||
                                 (item.PuaCode != null && item.PuaCode.ToLowerInvariant().Contains(q)) ||
                                 (item.Reading != null && item.Reading.ToLowerInvariant().Contains(q)) ||
                                 (item.Meaning != null && item.Meaning.ToLowerInvariant().Contains(q));
                    if (!match) continue;
                }

                var lvi = new ListViewItem(item.Name ?? "");
                lvi.SubItems.Add(item.IdsString ?? "");
                lvi.SubItems.Add(item.PuaCode ?? "");
                lvi.SubItems.Add(item.Reading ?? "");
                lvi.SubItems.Add(item.Meaning ?? "");
                lvi.Tag = item;
                _lvSaved.Items.Add(lvi);
            }

            _lvSaved.EndUpdate();
        }

        private void OnSaveCurrentGlyph(object s, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            if (string.IsNullOrEmpty(_currentIds))
            {
                MessageBox.Show(isVi ? "Công thức IDS đang trống!" : "IDS formula is empty!",
                    isVi ? "Thông báo" : "Notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string name = _txtName.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = (isVi ? "Chữ " : "Glyph ") + _currentIds;

            string pua = _txtPua.Text.Trim();
            if (string.IsNullOrEmpty(pua)) pua = "U+E000";

            string astJson = "";
            try { astJson = Json.Serialize(_currentAst); } catch { }

            var glyph = new SavedCustomGlyph
            {
                Id = "glyph_" + DateTime.UtcNow.Ticks,
                Name = name,
                IdsString = _currentIds,
                PuaCode = pua,
                Reading = _txtReading.Text.Trim(),
                Meaning = _txtMeaning.Text.Trim(),
                CreatedAt = DateTime.UtcNow.Ticks,
                AstJson = astJson
            };

            IdsDictionaryStorage.AddOrUpdate(glyph);
            PopulateSavedList();

            MessageBox.Show(isVi ? "Đã lưu chữ tự chế vào từ điển thành công!" : "Saved custom glyph to dictionary successfully!",
                isVi ? "Thành công" : "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

            _tabMain.SelectedIndex = 1;
        }

        private void OnLoadSelected()
        {
            if (_lvSaved.SelectedItems.Count == 0) return;
            LoadedGlyph = _lvSaved.SelectedItems[0].Tag as SavedCustomGlyph;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnDeleteSelected(object s, EventArgs e)
        {
            if (_lvSaved.SelectedItems.Count == 0) return;
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            var item = _lvSaved.SelectedItems[0].Tag as SavedCustomGlyph;
            if (item == null) return;

            string prompt = isVi
                ? string.Format("Bạn có chắc chắn muốn xóa chữ '{0}' ({1}) không?", item.Name, item.PuaCode)
                : string.Format("Are you sure you want to delete '{0}' ({1})?", item.Name, item.PuaCode);

            if (MessageBox.Show(prompt, isVi ? "Xác nhận xóa" : "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                IdsDictionaryStorage.Remove(item.Id);
                PopulateSavedList();
            }
        }

        private void OnExportJson(object s, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "JSON Files (*.json)|*.json";
                sfd.FileName = "cjk_custom_dictionary.json";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    var list = IdsDictionaryStorage.Load();
                    string json = Json.Serialize(list);
                    File.WriteAllText(sfd.FileName, json, Encoding.UTF8);
                    MessageBox.Show(isVi ? "Đã xuất từ điển JSON thành công!" : "Exported dictionary JSON successfully!",
                        isVi ? "Thành công" : "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void OnImportJson(object s, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "JSON Files (*.json)|*.json";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string json = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                        var imported = Json.Deserialize<List<SavedCustomGlyph>>(json);
                        if (imported != null && imported.Count > 0)
                        {
                            foreach (var item in imported)
                            {
                                IdsDictionaryStorage.AddOrUpdate(item);
                            }
                            PopulateSavedList();
                            MessageBox.Show(isVi ? string.Format("Đã nạp {0} chữ từ file JSON!", imported.Count) : string.Format("Imported {0} glyphs from JSON!", imported.Count),
                                isVi ? "Thành công" : "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show((isVi ? "Lỗi đọc JSON: " : "Error reading JSON: ") + ex.Message,
                            isVi ? "Lỗi" : "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}
