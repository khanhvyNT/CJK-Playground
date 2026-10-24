using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Forms
{
    public class CharacterBasketForm : Form
    {
        private readonly List<CharacterInfo> _basketItems;
        private readonly FontLoaderService _fontService;
        private ListView _lvItems;
        private Label _lblCount;
        private Action _onBasketChanged;

        public CharacterBasketForm(List<CharacterInfo> items, FontLoaderService fontService, Action onBasketChanged)
        {
            _basketItems = items;
            _fontService = fontService;
            _onBasketChanged = onBasketChanged;

            InitializeComponent();
            RefreshList();
        }

        private void InitializeComponent()
        {
            var appIcon = AppIconHelper.GetAppIcon();
            if (appIcon != null) Icon = appIcon;

            Text = LocalizationManager.Get("BasketTitle", "Giỏ ký tự (Character Basket)");
            Size = new Size(720, 520);
            MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(12, 10, 12, 0) };
            _lblCount = new Label
            {
                Text = string.Format(LocalizationManager.Get("BasketCountFmt", "Số ký tự trong giỏ: {0}"), 0),
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(12, 12)
            };
            topPanel.Controls.Add(_lblCount);

            // ListView
            _lvItems = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 10F)
            };
            _lvItems.Columns.Add(LocalizationManager.Get("BasketColChar", "Chữ"), 70);
            _lvItems.Columns.Add(LocalizationManager.Get("BasketColHex", "Mã Hex"), 90);
            _lvItems.Columns.Add(LocalizationManager.Get("BasketColBlock", "Khối Unicode"), 220);
            _lvItems.Columns.Add(LocalizationManager.Get("BasketColReading", "Hán Việt"), 120);

            // Bottom Buttons Panel
            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };

            var btnClose = new Button { Text = LocalizationManager.Get("BtnClose", "Đóng"), Width = 80, Height = 32 };
            btnClose.Click += (s, e) => Close();

            var btnExportZip = new Button
            {
                Text = LocalizationManager.Get("ExportZip", "💾 Tải gói ZIP (.zip)"),
                Width = 145,
                Height = 32,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnExportZip.Click += OnExportZip;

            var btnExportTxt = new Button
            {
                Text = LocalizationManager.Get("ExportTxt", "📄 Xuất file .txt"),
                Width = 120,
                Height = 32
            };
            btnExportTxt.Click += OnExportTxt;

            var btnCopyChars = new Button
            {
                Text = LocalizationManager.Get("CopyAllChars", "📋 Sao chép chữ"),
                Width = 125,
                Height = 32
            };
            btnCopyChars.Click += (s, e) =>
            {
                var sb = new StringBuilder();
                foreach (var item in _basketItems) sb.Append(item.Character);
                if (sb.Length > 0)
                {
                    Clipboard.SetText(sb.ToString());
                    bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                    string msg = isVi
                        ? "Đã sao chép " + _basketItems.Count + " ký tự vào Clipboard!"
                        : "Copied " + _basketItems.Count + " characters to clipboard!";
                    string title = isVi ? "Thành công" : "Success";
                    MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            var btnRemove = new Button
            {
                Text = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese ? "🗑️ Xóa đã chọn" : "🗑️ Delete Selected",
                Width = 120,
                Height = 32
            };
            btnRemove.Click += OnRemoveSelected;

            var btnClearAll = new Button
            {
                Text = LocalizationManager.Get("ClearBasket", "🧹 Xóa toàn bộ"),
                Width = 110,
                Height = 32
            };
            btnClearAll.Click += OnClearAll;

            bottomPanel.Controls.AddRange(new Control[] {
                btnClose, btnExportZip, btnExportTxt, btnCopyChars, btnRemove, btnClearAll
            });

            Controls.Add(_lvItems);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);
        }

        private void RefreshList()
        {
            _lvItems.Items.Clear();
            var font = _fontService.CreateFontInstance(14F);

            foreach (var item in _basketItems)
            {
                var lvi = new ListViewItem(item.Character);
                lvi.Font = font;
                lvi.SubItems.Add(item.HexCode);
                lvi.SubItems.Add(item.BlockName);
                lvi.SubItems.Add(!string.IsNullOrEmpty(item.HanViet) ? item.HanViet : "--");
                lvi.Tag = item;
                _lvItems.Items.Add(lvi);
            }

            _lblCount.Text = string.Format(LocalizationManager.Get("BasketCountFmt", "Số ký tự trong giỏ: {0}"), _basketItems.Count);
            if (_onBasketChanged != null) _onBasketChanged();
        }

        private void OnRemoveSelected(object s, EventArgs e)
        {
            if (_lvItems.SelectedItems.Count == 0) return;

            var toRemove = new List<CharacterInfo>();
            foreach (ListViewItem lvi in _lvItems.SelectedItems)
            {
                var ci = lvi.Tag as CharacterInfo;
                if (ci != null) toRemove.Add(ci);
            }

            foreach (var item in toRemove)
            {
                _basketItems.Remove(item);
            }

            RefreshList();
        }

        private void OnClearAll(object s, EventArgs e)
        {
            if (_basketItems.Count == 0) return;
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            string prompt = isVi ? "Bạn có chắc chắn muốn xóa toàn bộ chữ trong giỏ không?" : "Are you sure you want to clear all characters in the basket?";
            string title = isVi ? "Xác nhận" : "Confirm";
            if (MessageBox.Show(prompt, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _basketItems.Clear();
                RefreshList();
            }
        }

        private void OnExportTxt(object s, EventArgs e)
        {
            if (_basketItems.Count == 0) return;
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = isVi ? "Tệp văn bản (*.txt)|*.txt" : "Text File (*.txt)|*.txt";
                sfd.FileName = "cjk_playground_basket.txt";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("# CJK Playground Exported Character Basket");
                    sb.AppendLine(isVi ? ("# Ngày xuất: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) : ("# Export date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                    sb.AppendLine(string.Format(isVi ? "# Tổng số ký tự: {0}" : "# Total characters: {0}", _basketItems.Count));
                    sb.AppendLine();
                    sb.AppendLine(isVi ? "## Danh sách ký tự:" : "## Character List:");
                    foreach (var it in _basketItems) sb.Append(it.Character);
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.AppendLine(isVi ? "## Chi tiết mã điểm Unicode:" : "## Unicode Codepoint Details:");
                    foreach (var it in _basketItems)
                    {
                        sb.AppendLine(string.Format("{0}\t{1}\t{2}\t{3}", it.Character, it.HexCode, it.BlockName, it.HanViet ?? ""));
                    }

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show(isVi ? "Đã xuất tệp văn bản thành công!" : "Exported text file successfully!", isVi ? "Thông báo" : "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void OnExportZip(object s, EventArgs e)
        {
            if (_basketItems.Count == 0) return;
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = isVi ? "Tập tin nén ZIP (*.zip)|*.zip" : "ZIP Archive (*.zip)|*.zip";
                sfd.FileName = "cjk_playground_svg_icons.zip";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        if (File.Exists(sfd.FileName)) File.Delete(sfd.FileName);

                        using (var zipStream = new FileStream(sfd.FileName, FileMode.Create))
                        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                        {
                            foreach (var it in _basketItems)
                            {
                                string svgContent = _fontService.ExportGlyphSvg(it.CodePoint, it.Character);
                                string entryName = string.Format("{0}_{1}.svg", it.Character, it.HexCode);

                                var entry = archive.CreateEntry(entryName);
                                using (var entryStream = entry.Open())
                                using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
                                {
                                    writer.Write(svgContent);
                                }
                            }
                        }

                        string successMsg = isVi
                            ? string.Format("Đã xuất thành công gói ZIP gồm {0} tệp vector SVG!", _basketItems.Count)
                            : string.Format("Successfully exported ZIP package with {0} SVG vector files!", _basketItems.Count);
                        string title = isVi ? "Xuất ZIP thành công" : "ZIP Export Success";
                        MessageBox.Show(successMsg, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        string errMsg = (isVi ? "Lỗi xuất ZIP: " : "Error exporting ZIP: ") + ex.Message;
                        string title = isVi ? "Lỗi" : "Error";
                        MessageBox.Show(errMsg, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}
