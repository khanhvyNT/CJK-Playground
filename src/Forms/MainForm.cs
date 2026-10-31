using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CJKPlayground.Models;
using CJKPlayground.Services;
using CJKPlayground.Tests;

namespace CJKPlayground.Forms
{
    public class MainForm : Form
    {
        private FontLoaderService _fontService;
        private OnlineDictionaryService _dictService;
        private UnihanDatabase _unihanDb;
        private HvDicDatabase _hvdicDb;
        private readonly List<CharacterInfo> _basketItems = new List<CharacterInfo>();
        private ToolTip _glyphToolTip;
        private AppSettings _settings;

        // UI Controls - Global
        private MenuStrip _menuStrip;
        private ToolStripMenuItem _menuFile;
        private ToolStripMenuItem _menuTools;
        private ToolStripMenuItem _menuBasket;
        private ToolStripMenuItem _menuLanguage;
        private ToolStripMenuItem _menuHelp;
        private ToolStripMenuItem _menuAutoComposite;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusFontLabel;
        private ToolStripStatusLabel _statusInfoLabel;
        private TabControl _mainTabControl;
        private readonly Font _quocNguFont = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        private readonly Font _idsFont = new Font("SimSun", 9.5F, FontStyle.Regular);

        // Tab 1: Character Map
        private TreeView _tvUnicodeBlocks;
        private FlowLayoutPanel _pnlGlyphGrid;
        private SplitContainer _mainSplit;
        private SplitContainer _gridSplit;
        private SplitContainer _searchSplit;
        private Button _btnPrevPage;
        private Button _btnNextPage;
        private Label _lblPageInfo;
        private TextBox _txtJumpHex;
        private Button _btnJump;
        private int _currentStartCp = 0x4E00;
        private int _currentEndCp = 0x9FFF;
        private int _currentPage = 0;
        private const int PageSize = 120;

        // Tab 1: Character Map Controls
        private Label _lblTreeTitle;

        // Inspector Controls
        private Label _lblInspectorChar;
        private Label _lblInspectorHex;
        private Label _lblInspectorUtf8;
        private Label _lblInspectorUtf16;
        private Label _lblInspectorBlock;
        private Label _lblInspectorStrokes;
        private Label _lblInspectorGlyphId;
        private Button _btnCopyChar;
        private Button _btnCopyHex;
        private Button _btnCopyImage;
        private Button _btnLookupSelected;
        private Button _btnAddToBasket;
        private Button _btnExportSvg;
        private CharacterInfo _selectedChar;

        // Tab 2: Phonetic & Online Search
        private TextBox _txtSearchInput;
        private ComboBox _cbSearchLang;
        private CheckBox _chkPureNom;
        private Label _lblFilterCols;
        private CheckBox _chkColHanViet;
        private CheckBox _chkColNom;
        private CheckBox _chkColPinyin;
        private CheckBox _chkColMeaning;
        private Label _lblFilterRules;
        private CheckBox _chkMatchExactWord;
        private CheckBox _chkMatchExactTone;
        private Button _btnExecuteSearch;
        private Label _lblSearchCount;
        private ListView _lvSearchResults;
        private TextBox _txtSearchDetails;
        private Label _lblSearchCharBig;
        private Label _lblSearchReadings;
        private Button _btnOpenWebSource;
        private Button _btnCopyWebUrl;
        private Label _lblWebSourceCitation;
        private string _currentWebUrl = "";

        // Tab 3: IDS Composer Studio
        private IdsComposerControl _idsComposer;

        // Tab 4: Font Testing Area
        private FontTestControl _fontTestControl;
        private ToolStripMenuItem _menuFontTest;

        public static MainForm Instance { get; private set; }

        public MainForm() : this(null)
        {
        }

        public MainForm(Action<int, string> progress)
        {
            Instance = this;
            var report = progress ?? ((p, m) => { });

            _settings = SettingsManager.Load();
            if (!string.IsNullOrEmpty(_settings.Language) && _settings.Language.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                LocalizationManager.SetLanguage(SupportedLanguage.English);
            }
            else
            {
                LocalizationManager.SetLanguage(SupportedLanguage.Vietnamese);
            }

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            report(8, isVi ? "Đang tải cấu hình và ngôn ngữ..." : "Loading settings and language...");
            report(22, isVi ? "Đang khởi tạo Font Engine & Composite Font..." : "Initializing Font Engine & Composite Font...");
            _fontService = new FontLoaderService();
            _fontService.AutoCompositeEnabled = _settings.AutoCompositeFont;

            report(36, isVi ? "Đang kết nối dịch vụ từ điển trực tuyến..." : "Initializing online lookup services...");
            _dictService = new OnlineDictionaryService();

            report(50, isVi ? "Đang nạp cơ sở dữ liệu Unihan & Từ điển Hán-Việt Thi Viện..." : "Loading Unihan & HVDic Thi Vien databases...");
            _unihanDb = new UnihanDatabase();
            _hvdicDb = new HvDicDatabase();
            Action updateDbStatus = () =>
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            int uCount = _unihanDb != null ? _unihanDb.TotalRecords : 0;
                            int hCount = _hvdicDb != null ? _hvdicDb.TotalRecords : 0;
                            _statusInfoLabel.Text = isVi
                                ? string.Format("Cơ sở dữ liệu: Unihan ({0:N0} chữ) | HVDic Thi viện ({1:N0} mục từ)", uCount, hCount)
                                : string.Format("Databases: Unihan ({0:N0} chars) | HVDic Thi Vien ({1:N0} entries)", uCount, hCount);
                        }));
                    }
                    catch { }
                }
            };
            _unihanDb.DatabaseLoaded += updateDbStatus;
            _hvdicDb.DatabaseLoaded += updateDbStatus;
            _glyphToolTip = new ToolTip
            {
                InitialDelay = 200,
                ReshowDelay = 100,
                AutoPopDelay = 10000,
                ShowAlways = true
            };

            report(65, isVi ? "Đang xây dựng giao diện ứng dụng..." : "Building user interface controls...");
            InitializeForm();

            // Restore settings if present
            if (_settings.LastStartCp > 0 && _settings.LastEndCp >= _settings.LastStartCp)
            {
                _currentStartCp = _settings.LastStartCp;
                _currentEndCp = _settings.LastEndCp;
                _currentPage = _settings.LastPage;
            }

            if (!string.IsNullOrEmpty(_settings.LastFontPath) && File.Exists(_settings.LastFontPath))
            {
                report(75, isVi ? "Đang nạp phông chữ phiên làm việc trước..." : "Loading previous font file...");
                try { _fontService.LoadFontFromFile(_settings.LastFontPath); } catch { }
            }

            report(82, isVi ? "Đang nạp danh mục khối Unicode..." : "Loading Unicode blocks tree...");
            LoadBlockTree();

            report(90, isVi ? "Đang kết xuất lưới ký tự (Glyph Grid)..." : "Rendering glyph grid cells...");
            RefreshGlyphGrid();

            report(96, isVi ? "Đang hoàn tất chuẩn bị hệ thống..." : "Finalizing system state...");
            UpdateStatus();

            LocalizationManager.LanguageChanged += OnLanguageChanged;
            FormClosing += OnMainFormClosing;

            report(100, isVi ? "Khởi động thành công!" : "Ready!");
        }

        private void InitializeForm()
        {
            UpdateWindowTitle();
            var appIcon = AppIconHelper.GetAppIcon();
            if (appIcon != null)
            {
                Icon = appIcon;
            }

            var workArea = Screen.PrimaryScreen.WorkingArea;
            int initW = Math.Min(Math.Max(980, _settings.WindowWidth), workArea.Width - 40);
            int initH = Math.Min(Math.Max(640, _settings.WindowHeight), workArea.Height - 40);
            Size = new Size(initW, initH);
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterScreen;
            if (_settings.IsMaximized) WindowState = FormWindowState.Maximized;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            DoubleBuffered = true;
            AllowDrop = true;

            // Drag & Drop font files directly into application
            DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        string ext = Path.GetExtension(files[0]).ToLowerInvariant();
                        if (ext == ".ttf" || ext == ".otf" || ext == ".ttc")
                        {
                            e.Effect = DragDropEffects.Copy;
                            return;
                        }
                    }
                }
                e.Effect = DragDropEffects.None;
            };

            DragDrop += (s, e) =>
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    try
                    {
                        var info = _fontService.LoadFontFromFile(files[0]);
                        _settings.LastFontPath = files[0];
                        UpdateStatus();
                        LoadBlockTree();
                        RefreshGlyphGrid();
                        string successMsg = string.Format(LocalizationManager.Get("FontDragDropLoaded", "Đã nạp thành công font: {0}"), info.FamilyName);
                        MessageBox.Show(successMsg, LocalizationManager.Get("SuccessTitle", "Thành công"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        string errMsg = string.Format(LocalizationManager.Get("FontDragDropError", "Lỗi nạp font: {0}"), ex.Message);
                        MessageBox.Show(errMsg, LocalizationManager.Get("ErrorTitle", "Lỗi"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };

            // 1. MenuStrip
            _menuStrip = new MenuStrip();

            // File Menu
            _menuFile = new ToolStripMenuItem(LocalizationManager.Get("MenuFile", "Tập tin"));
            _menuFile.DropDownItems.Add(LocalizationManager.Get("MenuOpenFont", "Mở font từ máy tính (.ttf, .otf)..."), null, OnOpenFontFile);
            _menuFile.DropDownItems.Add(LocalizationManager.Get("MenuSelectSystem", "Chọn Font CJK hệ thống..."), null, OnSelectSystemFont);
            _menuFile.DropDownItems.Add(new ToolStripSeparator());
            _menuFile.DropDownItems.Add(LocalizationManager.Get("MenuExit", "Thoát"), null, (s, e) => Close());

            // Tools Menu
            _menuTools = new ToolStripMenuItem(LocalizationManager.Get("MenuTools", "Công cụ"));
            _menuBasket = new ToolStripMenuItem(LocalizationManager.Get("MenuBasket", "Giỏ ký tự đã lưu"), null, OnOpenBasketForm);
            _menuTools.DropDownItems.Add(_menuBasket);
            _menuTools.DropDownItems.Add(LocalizationManager.Get("MenuReport", "Báo cáo độ phủ font (Font Coverage Report)..."), null, OnOpenFontReportForm);
            _menuTools.DropDownItems.Add(new ToolStripSeparator());
            _menuFontTest = new ToolStripMenuItem(LocalizationManager.Get("MenuFontTest", "Khu Vực Test Font (Font Sandbox)..."), null, (s, e) => SwitchToFontTestTab()) { ShortcutKeys = Keys.Control | Keys.T };
            _menuTools.DropDownItems.Add(_menuFontTest);
            _menuTools.DropDownItems.Add(new ToolStripSeparator());
            _menuAutoComposite = new ToolStripMenuItem(LocalizationManager.Get("MenuAutoComposite", "Tự động chọn Font hệ thống theo khối (Composite System Font)"), null, OnToggleAutoCompositeFont) { Checked = _settings.AutoCompositeFont };
            _menuTools.DropDownItems.Add(_menuAutoComposite);

            // Language Menu - Only Vietnamese & English
            _menuLanguage = new ToolStripMenuItem(LocalizationManager.Get("MenuLanguage", "Ngôn ngữ"));
            _menuLanguage.DropDownItems.Add("🇻🇳 Tiếng Việt", null, (s, e) => ChangeLanguageAndRestart("vi"));
            _menuLanguage.DropDownItems.Add("🇬🇧 English", null, (s, e) => ChangeLanguageAndRestart("en"));

            // Help Menu
            _menuHelp = new ToolStripMenuItem(LocalizationManager.Get("MenuHelp", "Trợ giúp"));
            _menuHelp.DropDownItems.Add(LocalizationManager.Get("MenuAbout", "Giới thiệu CJK Playground"), null, (s, e) =>
            {
                using (var dlg = new AboutDialog(_unihanDb != null ? _unihanDb.TotalRecords : 0, _hvdicDb != null ? _hvdicDb.TotalRecords : 0))
                {
                    dlg.ShowDialog(this);
                }
            });

            _menuStrip.Items.AddRange(new ToolStripItem[] { _menuFile, _menuTools, _menuLanguage, _menuHelp });
            Controls.Add(_menuStrip);
            MainMenuStrip = _menuStrip;

            // 2. StatusStrip
            _statusStrip = new StatusStrip();
            _statusFontLabel = new ToolStripStatusLabel { Text = LocalizationManager.Get("StatusFontLoading", "Font: Đang tải..."), AutoSize = true };
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            int initialUnihan = _unihanDb != null ? _unihanDb.TotalRecords : 0;
            int initialHvdic = _hvdicDb != null ? _hvdicDb.TotalRecords : 0;
            _statusInfoLabel = new ToolStripStatusLabel
            {
                Text = isVi
                    ? string.Format("Cơ sở dữ liệu: Unihan ({0:N0} chữ) | HVDic Thi viện ({1:N0} mục từ)", initialUnihan, initialHvdic)
                    : string.Format("Databases: Unihan ({0:N0} chars) | HVDic Thi Vien ({1:N0} entries)", initialUnihan, initialHvdic),
                Spring = true,
                TextAlign = ContentAlignment.MiddleRight
            };
            _statusStrip.Items.AddRange(new ToolStripItem[] { _statusFontLabel, _statusInfoLabel });
            Controls.Add(_statusStrip);

            // 3. TabControl
            _mainTabControl = new TabControl { Dock = DockStyle.Fill };
            _mainTabControl.TabPages.Add(CreateCharMapTab());
            _mainTabControl.TabPages.Add(CreateSearchTab());
            _mainTabControl.TabPages.Add(CreateIdsComposerTab());
            _mainTabControl.TabPages.Add(CreateFontTestTab());
            _mainTabControl.SelectedIndexChanged += (s, e) =>
            {
                UpdateWindowTitle();
                if (_mainTabControl.SelectedIndex == 1)
                {
                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (_searchSplit != null && _searchSplit.Width > 500)
                            {
                                int target = _searchSplit.Width - 325;
                                if (target >= _searchSplit.Panel1MinSize && target <= _searchSplit.Width - _searchSplit.Panel2MinSize)
                                {
                                    _searchSplit.SplitterDistance = target;
                                }
                            }
                        }
                        catch { }
                    }));
                }
            };
            Controls.Add(_mainTabControl);
            _mainTabControl.BringToFront();
            UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            string tabName = null;
            if (_mainTabControl != null && _mainTabControl.SelectedTab != null)
            {
                tabName = _mainTabControl.SelectedTab.Text;
            }

            if (!string.IsNullOrEmpty(tabName))
            {
                Text = string.Format("CJK Playground - {0}", tabName);
            }
            else
            {
                Text = "CJK Playground";
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            AdjustLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            AdjustLayout();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            AdjustLayout();
        }

        private void AdjustLayout()
        {
            try
            {
                if (_mainSplit != null && _mainSplit.Width > 300)
                {
                    _mainSplit.Panel1MinSize = 160;
                    int target = (int)(_mainSplit.Width * 0.22);
                    if (target < 180) target = 180;
                    if (target > 260) target = 260;
                    if (target < _mainSplit.Width - 180)
                    {
                        _mainSplit.SplitterDistance = target;
                    }
                }
            }
            catch { }

            try
            {
                if (_gridSplit != null && _gridSplit.Width > 400)
                {
                    _gridSplit.Panel1MinSize = 220;
                    _gridSplit.Panel2MinSize = 220;
                    int target = _gridSplit.Width - 290;
                    if (target < _gridSplit.Panel1MinSize) target = _gridSplit.Panel1MinSize;
                    if (target > _gridSplit.Width - _gridSplit.Panel2MinSize) target = _gridSplit.Width - _gridSplit.Panel2MinSize;
                    _gridSplit.SplitterDistance = target;
                }
            }
            catch { }

            try
            {
                if (_searchSplit != null && _searchSplit.Width > 500)
                {
                    int target = _searchSplit.Width - 325;
                    if (target > 100 && target < _searchSplit.Width - 100)
                    {
                        _searchSplit.SplitterDistance = target;
                    }
                }
            }
            catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.O))
            {
                OnOpenFontFile(null, EventArgs.Empty);
                return true;
            }
            if (keyData == (Keys.Control | Keys.F))
            {
                _mainTabControl.SelectedIndex = 1;
                _txtSearchInput.Focus();
                _txtSearchInput.SelectAll();
                return true;
            }
            if (keyData == (Keys.Control | Keys.T))
            {
                SwitchToFontTestTab();
                return true;
            }
            if (keyData == (Keys.Control | Keys.B))
            {
                OnOpenBasketForm(null, EventArgs.Empty);
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                OnExportSvg();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.C))
            {
                OnCopyBitmapImage();
                return true;
            }
            if (keyData == Keys.PageUp)
            {
                if (_mainTabControl.SelectedIndex == 0 && _currentPage > 0)
                {
                    _currentPage--;
                    RefreshGlyphGrid();
                    return true;
                }
            }
            if (keyData == Keys.PageDown)
            {
                if (_mainTabControl.SelectedIndex == 0)
                {
                    _currentPage++;
                    RefreshGlyphGrid();
                    return true;
                }
            }
            if (keyData == Keys.F1)
            {
                _mainTabControl.SelectedIndex = 0;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnMainFormClosing(object sender, FormClosingEventArgs e)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.IsMaximized = (WindowState == FormWindowState.Maximized);
            _settings.LastStartCp = _currentStartCp;
            _settings.LastEndCp = _currentEndCp;
            _settings.LastPage = _currentPage;
            _settings.AutoCompositeFont = _fontService.AutoCompositeEnabled;
            SettingsManager.Save(_settings);
        }

        private void OnLanguageChanged()
        {
            if (_menuFile != null) _menuFile.Text = LocalizationManager.Get("MenuFile");
            if (_menuTools != null) _menuTools.Text = LocalizationManager.Get("MenuTools");
            if (_menuBasket != null) UpdateBasketCounter();
            if (_menuAutoComposite != null) _menuAutoComposite.Text = LocalizationManager.Get("MenuAutoComposite");
            if (_menuLanguage != null) _menuLanguage.Text = LocalizationManager.Get("MenuLanguage");
            if (_menuHelp != null) _menuHelp.Text = LocalizationManager.Get("MenuHelp");

            if (_mainTabControl != null && _mainTabControl.TabPages.Count >= 4)
            {
                _mainTabControl.TabPages[0].Text = LocalizationManager.Get("TabCharMap");
                _mainTabControl.TabPages[1].Text = LocalizationManager.Get("TabSearch");
                _mainTabControl.TabPages[2].Text = LocalizationManager.Get("TabIds");
                _mainTabControl.TabPages[3].Text = LocalizationManager.Get("TabFontTest");
            }

            UpdateWindowTitle();

            if (_lblTreeTitle != null) _lblTreeTitle.Text = LocalizationManager.Get("BlockTreeHeader", "Khối Unicode");

            if (_btnCopyChar != null) _btnCopyChar.Text = LocalizationManager.Get("CopyChar");
            if (_btnCopyHex != null) _btnCopyHex.Text = LocalizationManager.Get("CopyHex");
            if (_btnCopyImage != null) _btnCopyImage.Text = LocalizationManager.Get("CopyImage");
            if (_btnLookupSelected != null) _btnLookupSelected.Text = LocalizationManager.Get("LookupChar");
            if (_btnAddToBasket != null) _btnAddToBasket.Text = LocalizationManager.Get("AddToBasket");
            if (_btnExportSvg != null) _btnExportSvg.Text = LocalizationManager.Get("ExportSvg");

            if (_btnPrevPage != null) _btnPrevPage.Text = LocalizationManager.Get("PagePrev");
            if (_btnNextPage != null) _btnNextPage.Text = LocalizationManager.Get("PageNext");
            if (_btnJump != null) _btnJump.Text = LocalizationManager.Get("JumpBtn");

            if (_btnExecuteSearch != null) _btnExecuteSearch.Text = LocalizationManager.Get("SearchBtn");
            if (_chkPureNom != null) _chkPureNom.Text = LocalizationManager.Get("PureNomCheck");
            if (_lblFilterCols != null) _lblFilterCols.Text = LocalizationManager.Get("FilterColsLabel", "Khớp cột:");
            if (_chkColHanViet != null) _chkColHanViet.Text = LocalizationManager.Get("FilterColHanViet", "Hán Việt");
            if (_chkColNom != null) _chkColNom.Text = LocalizationManager.Get("FilterColNom", "IDS");
            if (_chkColPinyin != null) _chkColPinyin.Text = LocalizationManager.Get("FilterColPinyin", "Pinyin");
            if (_chkColMeaning != null) _chkColMeaning.Text = LocalizationManager.Get("FilterColMeaning", "Nghĩa từ điển");
            if (_lblFilterRules != null) _lblFilterRules.Text = LocalizationManager.Get("FilterRulesLabel", "| Quy tắc:");
            if (_chkMatchExactWord != null) _chkMatchExactWord.Text = LocalizationManager.Get("MatchExactWord", "Khớp toàn từ");
            if (_chkMatchExactTone != null) _chkMatchExactTone.Text = LocalizationManager.Get("MatchExactTone", "Đúng dấu thanh");

            if (_btnOpenWebSource != null) _btnOpenWebSource.Text = LocalizationManager.Get("OpenWebSource", "🌐 Mở trang Web nguồn");
            if (_btnCopyWebUrl != null) _btnCopyWebUrl.Text = LocalizationManager.Get("CopyWebUrl", "📋 Sao chép URL");

            UpdateSearchTableColumns();
        }

        private void UpdateSearchTableColumns()
        {
            if (_lvSearchResults != null && _lvSearchResults.Columns.Count >= 9)
            {
                _lvSearchResults.Columns[0].Text = LocalizationManager.Get("ColChar", "Chữ / Từ");
                _lvSearchResults.Columns[1].Text = LocalizationManager.Get("ColHex", "Mã Hex");
                _lvSearchResults.Columns[2].Text = LocalizationManager.Get("ColSource", "Nguồn dữ liệu");
                _lvSearchResults.Columns[3].Text = LocalizationManager.Get("ColMatched", "Đọc khớp");
                _lvSearchResults.Columns[4].Text = LocalizationManager.Get("ColHanViet", "Hán Việt");
                _lvSearchResults.Columns[5].Text = LocalizationManager.Get("ColNom", "IDS");
                _lvSearchResults.Columns[6].Text = LocalizationManager.Get("ColPinyin", "Pinyin");
                _lvSearchResults.Columns[7].Text = LocalizationManager.Get("ColStrokes", "Số nét");
                _lvSearchResults.Columns[8].Text = LocalizationManager.Get("ColWebSource", "Nguồn Web (URL / Trích dẫn)");
            }
        }

        private void ChangeLanguageAndRestart(string langCode)
        {
            string current = _settings.Language ?? "vi";
            if (string.Equals(current, langCode, StringComparison.OrdinalIgnoreCase))
                return;

            _settings.Language = langCode;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.IsMaximized = (WindowState == FormWindowState.Maximized);
            _settings.LastStartCp = _currentStartCp;
            _settings.LastEndCp = _currentEndCp;
            _settings.LastPage = _currentPage;
            _settings.AutoCompositeFont = _fontService.AutoCompositeEnabled;
            SettingsManager.Save(_settings);

            string msg;
            string title;
            if (string.Equals(langCode, "en", StringComparison.OrdinalIgnoreCase))
            {
                msg = LocalizationManager.Get("RestartMsgEn", "Language changed to English. The application will restart to apply the new interface.");
                title = LocalizationManager.Get("RestartTitleEn", "Language Changed");
            }
            else
            {
                msg = LocalizationManager.Get("RestartMsgVi", "Đã thay đổi ngôn ngữ sang Tiếng Việt. Ứng dụng sẽ khởi động lại để áp dụng giao diện mới.");
                title = LocalizationManager.Get("RestartTitleVi", "Đổi ngôn ngữ");
            }

            MessageBox.Show(this, msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Application.Restart();
            Environment.Exit(0);
        }

        private void OnRunDiagnostics(object sender, EventArgs e)
        {
            string lingReport;
            bool lingOk = LinguisticTests.RunAllTests(out lingReport);

            string otReport;
            bool otOk = OpenTypeTests.RunAllTests(out otReport);

            string header = LocalizationManager.Get("DiagnosticsReportHeader", "=== KẾT QUẢ CHẨN ĐOÁN HỆ THỐNG CJK PLAYGROUND ===\n\n");
            string fullReport = header + lingReport + "\n\n" + otReport;

            bool allOk = lingOk && otOk;
            string diagTitle = allOk
                ? LocalizationManager.Get("DiagnosticsSuccessTitle", "Chẩn đoán thành công (19/19 Tests Passed)")
                : LocalizationManager.Get("DiagnosticsErrorTitle", "Chẩn đoán có lỗi");
            MessageBox.Show(fullReport, diagTitle,
                MessageBoxButtons.OK, allOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void OnOpenBasketForm(object sender, EventArgs e)
        {
            using (var form = new CharacterBasketForm(_basketItems, _fontService, UpdateBasketCounter))
            {
                form.ShowDialog(this);
            }
            UpdateBasketCounter();
        }

        private void OnOpenFontReportForm(object sender, EventArgs e)
        {
            using (var form = new FontReportForm(_fontService))
            {
                form.ShowDialog(this);
            }
        }

        private void UpdateBasketCounter()
        {
            _menuBasket.Text = string.Format(LocalizationManager.Get("MenuBasketCountFmt", "🧺 Giỏ ký tự ({0})"), _basketItems.Count);
        }

        #region Tab 1: Character Map
        private TabPage CreateCharMapTab()
        {
            var tab = new TabPage(LocalizationManager.Get("TabCharMap", "Bảng mã ký tự (Character Map)"));

            // Main Split: Left TreeView (BabelMap tree), Right Grid & Inspector
            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1
            };

            // Left: TreeView Header & Tree
            var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _lblTreeTitle = new Label
            {
                Text = LocalizationManager.Get("BlockTreeHeader", "Khối Unicode"),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(8, 8, 0, 0)
            };
            _tvUnicodeBlocks = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9F),
                FullRowSelect = true,
                HideSelection = false,
                ShowLines = true
            };
            _tvUnicodeBlocks.AfterSelect += (s, e) =>
            {
                var block = e.Node.Tag as UnicodeBlock;
                if (block != null)
                {
                    _currentStartCp = block.StartCp;
                    _currentEndCp = block.EndCp;
                    _currentPage = 0;
                    if (_fontService.AutoCompositeEnabled)
                    {
                        if (_fontService.AutoSelectFontForBlock(block))
                        {
                            UpdateStatus();
                        }
                    }
                    RefreshGlyphGrid();
                }
            };
            leftPanel.Controls.Add(_tvUnicodeBlocks);
            leftPanel.Controls.Add(_lblTreeTitle);
            _mainSplit.Panel1.Controls.Add(leftPanel);

            // Right: Paging Bar + Split Grid/Inspector
            var rightPanel = new Panel { Dock = DockStyle.Fill };

            // FlowLayoutPanel prevents ANY control collision or overlapping!
            var topPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(10, 6, 10, 6),
                BackColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.FixedSingle
            };

            _btnPrevPage = new Button
            {
                Text = LocalizationManager.Get("PagePrev", "◄ Trước"),
                Width = 80,
                Height = 28,
                Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.System
            };
            _btnPrevPage.Click += (s, e) => { if (_currentPage > 0) { _currentPage--; RefreshGlyphGrid(); } };

            _lblPageInfo = new Label
            {
                Text = string.Format(LocalizationManager.Get("PageInfoFmt", "Trang {0} / {1} ({2:N0} ký tự)"), 1, 1, 0),
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            _btnNextPage = new Button
            {
                Text = LocalizationManager.Get("PageNext", "Sau ►"),
                Width = 80,
                Height = 28,
                Margin = new Padding(0, 0, 20, 0),
                FlatStyle = FlatStyle.System
            };
            _btnNextPage.Click += (s, e) => { _currentPage++; RefreshGlyphGrid(); };

            var lblJump = new Label
            {
                Text = LocalizationManager.Get("JumpToHex", "Nhảy tới Hex:"),
                AutoSize = true,
                Margin = new Padding(0, 6, 6, 0)
            };

            _txtJumpHex = new TextBox
            {
                Width = 80,
                Margin = new Padding(0, 3, 6, 0),
                Text = "4E00",
                Font = new Font("Consolas", 10F)
            };
            _txtJumpHex.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) OnJumpToHex(); };

            _btnJump = new Button
            {
                Text = LocalizationManager.Get("JumpBtn", "Đi"),
                Width = 50,
                Height = 28,
                Margin = new Padding(0, 0, 0, 0),
                FlatStyle = FlatStyle.System
            };
            _btnJump.Click += (s, e) => OnJumpToHex();

            topPanel.Controls.AddRange(new Control[] {
                _btnPrevPage, _lblPageInfo, _btnNextPage, lblJump, _txtJumpHex, _btnJump
            });

            // Split Container: Left = Grid, Right = Inspector
            _gridSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };

            // Grid
            _pnlGlyphGrid = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(12)
            };
            _gridSplit.Panel1.Controls.Add(_pnlGlyphGrid);

            // Right Inspector
            var inspectorPanel = CreateInspectorPanel();
            _gridSplit.Panel2.Controls.Add(inspectorPanel);

            rightPanel.Controls.Add(_gridSplit);
            rightPanel.Controls.Add(topPanel);
            _gridSplit.BringToFront();
            _mainSplit.Panel2.Controls.Add(rightPanel);

            tab.Controls.Add(_mainSplit);
            return tab;
        }

        private Panel CreateInspectorPanel()
        {
            var pnl = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(14),
                AutoScroll = true
            };

            var lblTitle = new Label
            {
                Text = LocalizationManager.Get("InspectorTitle", "CHI TIẾT KÝ TỰ (INSPECTOR)"),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Dock = DockStyle.Top,
                Height = 26
            };

            // Big Preview Box
            var previewBox = new Panel
            {
                Dock = DockStyle.Top,
                Height = 140,
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle
            };
            _lblInspectorChar = new Label
            {
                Text = "字",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("SimSun", 70F, FontStyle.Regular)
            };
            previewBox.Controls.Add(_lblInspectorChar);

            // Spacing
            var spacer1 = new Panel { Dock = DockStyle.Top, Height = 8 };

            // Info Labels Container
            var infoPnl = new Panel { Dock = DockStyle.Top, Height = 175 };
            _lblInspectorHex = new Label { Text = string.Format(LocalizationManager.Get("InspectorHexFmt", "Mã điểm: {0} ({1})"), "U+5B57", "23383"), Dock = DockStyle.Top, Height = 22, Font = new Font("Consolas", 9.5F, FontStyle.Bold) };
            _lblInspectorUtf8 = new Label { Text = "UTF-8: E5 AD 97", Dock = DockStyle.Top, Height = 22, Font = new Font("Consolas", 9F) };
            _lblInspectorUtf16 = new Label { Text = "UTF-16: 5B57", Dock = DockStyle.Top, Height = 22, Font = new Font("Consolas", 9F) };
            _lblInspectorBlock = new Label { Text = string.Format(LocalizationManager.Get("InspectorBlockFmt", "Khối: {0}{1}"), "CJK Unified Ideographs", ""), Dock = DockStyle.Top, Height = 22, ForeColor = Color.FromArgb(100, 116, 139) };
            _lblInspectorStrokes = new Label { Text = LocalizationManager.Get("InspectorStrokesEmpty", "Số nét / Bộ thủ: --"), Dock = DockStyle.Top, Height = 22 };
            _lblInspectorGlyphId = new Label { Text = LocalizationManager.Get("InspectorGlyphIdEmpty", "Glyph ID: -- | Độ rộng: --"), Dock = DockStyle.Top, Height = 22, Font = new Font("Consolas", 9F) };

            infoPnl.Controls.Add(_lblInspectorGlyphId);
            infoPnl.Controls.Add(_lblInspectorStrokes);
            infoPnl.Controls.Add(_lblInspectorBlock);
            infoPnl.Controls.Add(_lblInspectorUtf16);
            infoPnl.Controls.Add(_lblInspectorUtf8);
            infoPnl.Controls.Add(_lblInspectorHex);

            // Action Buttons
            var btnPnl = new Panel { Dock = DockStyle.Top, Height = 230 };

            _btnCopyChar = new Button { Text = LocalizationManager.Get("CopyChar", "📋 Sao chép ký tự"), Dock = DockStyle.Top, Height = 32 };
            _btnCopyChar.Click += (s, e) => {
                if (_selectedChar != null) {
                    Clipboard.SetText(_selectedChar.Character);
                    _statusInfoLabel.Text = string.Format(LocalizationManager.Get("StatusCopiedCharFmt", "Đã sao chép vào Clipboard: {0}"), _selectedChar.Character);
                }
            };

            _btnCopyHex = new Button { Text = LocalizationManager.Get("CopyHex", "🔢 Sao chép U+XXXX"), Dock = DockStyle.Top, Height = 32 };
            _btnCopyHex.Click += (s, e) => {
                if (_selectedChar != null) {
                    Clipboard.SetText(_selectedChar.HexCode);
                    _statusInfoLabel.Text = string.Format(LocalizationManager.Get("StatusCopiedHexFmt", "Đã sao chép mã hex: {0}"), _selectedChar.HexCode);
                }
            };

            _btnCopyImage = new Button { Text = LocalizationManager.Get("CopyImage", "🖼️ Sao chép ảnh (Bitmap)"), Dock = DockStyle.Top, Height = 32 };
            _btnCopyImage.Click += (s, e) => OnCopyBitmapImage();

            _btnExportSvg = new Button { Text = LocalizationManager.Get("ExportSvg", "💾 Xuất vector SVG"), Dock = DockStyle.Top, Height = 32 };
            _btnExportSvg.Click += (s, e) => OnExportSvg();

            _btnAddToBasket = new Button { Text = LocalizationManager.Get("AddToBasket", "🧺 Thêm vào giỏ"), Dock = DockStyle.Top, Height = 32 };
            _btnAddToBasket.Click += (s, e) => {
                if (_selectedChar != null) {
                    bool exists = _basketItems.Exists(x => x.CodePoint == _selectedChar.CodePoint);
                    if (!exists) {
                        _basketItems.Add(_selectedChar);
                        UpdateBasketCounter();
                        _statusInfoLabel.Text = string.Format(LocalizationManager.Get("StatusAddedBasketFmt", "Đã thêm {0} vào giỏ ký tự!"), _selectedChar.Character);
                    } else {
                        _statusInfoLabel.Text = string.Format(LocalizationManager.Get("StatusAlreadyInBasketFmt", "{0} đã có sẵn trong giỏ."), _selectedChar.Character);
                    }
                }
            };

            _btnLookupSelected = new Button { Text = LocalizationManager.Get("LookupChar", "🔍 Tra từ điển chữ này"), Dock = DockStyle.Top, Height = 32 };
            _btnLookupSelected.Click += (s, e) => {
                if (_selectedChar != null) {
                    _mainTabControl.SelectedIndex = 1;
                    _txtSearchInput.Text = _selectedChar.Character;
                    ExecuteSearchAsync(_selectedChar.Character);
                }
            };

            btnPnl.Controls.Add(_btnLookupSelected);
            btnPnl.Controls.Add(_btnAddToBasket);
            btnPnl.Controls.Add(_btnExportSvg);
            btnPnl.Controls.Add(_btnCopyImage);
            btnPnl.Controls.Add(_btnCopyHex);
            btnPnl.Controls.Add(_btnCopyChar);

            pnl.Controls.Add(btnPnl);
            pnl.Controls.Add(infoPnl);
            pnl.Controls.Add(spacer1);
            pnl.Controls.Add(previewBox);
            pnl.Controls.Add(lblTitle);
            return pnl;
        }

        private void OnCopyBitmapImage()
        {
            if (_selectedChar == null) return;
            try
            {
                using (var bmp = _fontService.RenderGlyphBitmap(_selectedChar.CodePoint, _selectedChar.Character, 512))
                {
                    Clipboard.SetImage(bmp);
                    _statusInfoLabel.Text = string.Format(LocalizationManager.Get("StatusCopiedBitmapFmt", "Đã sao chép ảnh bitmap 512x512 của chữ {0} vào Clipboard!"), _selectedChar.Character);
                }
            }
            catch (Exception ex)
            {
                bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                string msg = (isVi ? "Lỗi sao chép ảnh: " : "Error copying bitmap: ") + ex.Message;
                string title = isVi ? "Lỗi" : "Error";
                MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadBlockTree()
        {
            _tvUnicodeBlocks.BeginUpdate();
            _tvUnicodeBlocks.Nodes.Clear();

            var plane0Node = new TreeNode(LocalizationManager.Get("Plane0Node", "📁 Plane 0: BMP (Đa ngữ cơ bản)")) { Tag = null };
            var plane2Node = new TreeNode(LocalizationManager.Get("Plane2Node", "📁 Plane 2: SIP (Chữ Nôm & CJK mở rộng)")) { Tag = null };
            var plane3Node = new TreeNode(LocalizationManager.Get("Plane3Node", "📁 Plane 3: TIP (Chữ Hán mở rộng G/H)")) { Tag = null };

            TreeNode defaultSelectedNode = null;
            var nodeBlockList = new List<Tuple<TreeNode, UnicodeBlock>>();

            foreach (var b in UnicodeCatalog.Blocks)
            {
                var node = new TreeNode(b.Name) { Tag = b };
                nodeBlockList.Add(Tuple.Create(node, b));

                if (b.StartCp == _currentStartCp)
                    defaultSelectedNode = node;

                if (b.Plane == 0) plane0Node.Nodes.Add(node);
                else if (b.Plane == 2) plane2Node.Nodes.Add(node);
                else if (b.Plane == 3) plane3Node.Nodes.Add(node);
            }

            _tvUnicodeBlocks.Nodes.Add(plane0Node);
            _tvUnicodeBlocks.Nodes.Add(plane2Node);
            _tvUnicodeBlocks.Nodes.Add(plane3Node);

            plane0Node.Expand();
            plane2Node.Expand();

            if (defaultSelectedNode != null)
            {
                _tvUnicodeBlocks.SelectedNode = defaultSelectedNode;
            }

            _tvUnicodeBlocks.EndUpdate();

            // Asynchronously update coverage percentages without slowing down startup
            Task.Factory.StartNew(() =>
            {
                try
                {
                    if (_fontService.OpenTypeReader == null) return;
                    var supportedSet = _fontService.OpenTypeReader.GetSupportedCodePoints();
                    if (supportedSet.Count == 0) return;

                    var counts = new Dictionary<UnicodeBlock, int>();
                    foreach (var pair in nodeBlockList)
                    {
                        var b = pair.Item2;
                        int present = 0;
                        for (int cp = b.StartCp; cp <= b.EndCp; cp++)
                        {
                            if (supportedSet.Contains(cp)) present++;
                        }
                        counts[b] = present;
                    }

                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                _tvUnicodeBlocks.BeginUpdate();
                                foreach (var pair in nodeBlockList)
                                {
                                    int present;
                                    if (counts.TryGetValue(pair.Item2, out present))
                                    {
                                        pair.Item1.Text = string.Format("{0} [{1:F0}%]", pair.Item2.Name, (present * 100.0 / pair.Item2.TotalCharacters));
                                    }
                                }
                                _tvUnicodeBlocks.EndUpdate();
                            }
                            catch { }
                        }));
                    }
                }
                catch { }
            });
        }

        private void RefreshGlyphGrid()
        {
            _pnlGlyphGrid.SuspendLayout();
            _pnlGlyphGrid.Controls.Clear();

            int totalChars = _currentEndCp - _currentStartCp + 1;
            int totalPages = (int)Math.Ceiling((double)totalChars / PageSize);
            if (_currentPage < 0) _currentPage = 0;
            if (_currentPage >= totalPages && totalPages > 0) _currentPage = totalPages - 1;

            _lblPageInfo.Text = string.Format(LocalizationManager.Get("PageInfoFmt", "Trang {0} / {1} ({2:N0} ký tự)"), _currentPage + 1, Math.Max(1, totalPages), totalChars);
            _btnPrevPage.Enabled = _currentPage > 0;
            _btnNextPage.Enabled = _currentPage < totalPages - 1;

            int pageStart = _currentStartCp + (_currentPage * PageSize);
            int pageEnd = Math.Min(pageStart + PageSize - 1, _currentEndCp);

            for (int cp = pageStart; cp <= pageEnd; cp++)
            {
                var charInfo = new CharacterInfo(cp);
                var btn = new Button
                {
                    Text = charInfo.Character,
                    Font = _fontService.GetFontForCodePoint(cp, 20F),
                    Width = 52,
                    Height = 52,
                    Margin = new Padding(3),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.White,
                    Tag = charInfo
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(241, 245, 249);
                btn.Click += (s, e) => SelectCharacter((CharacterInfo)((Button)s).Tag);

                // Rich Tooltip on mouse hover
                var rec = _unihanDb.GetByCodePoint(cp);
                var sbTip = new StringBuilder();
                sbTip.AppendLine(string.Format("{0}  ({1})", charInfo.Character, charInfo.HexCode));
                sbTip.AppendLine(LocalizationManager.Get("TipBlock", "Khối:") + " " + charInfo.BlockName);
                if (rec != null)
                {
                    if (rec.Vietnamese.Count > 0)
                        sbTip.AppendLine(LocalizationManager.Get("TipSinoVietnamese", "Hán Việt:") + " " + string.Join(", ", rec.Vietnamese));
                    else if (_hvdicDb != null)
                    {
                        var hvList = _hvdicDb.GetEntriesByCodePoint(cp);
                        if (hvList.Count > 0)
                            sbTip.AppendLine(LocalizationManager.Get("TipSinoVietnamese", "Hán Việt:") + " " + hvList[0].HanViet);
                    }
                    if (rec.Nom.Count > 0)
                        sbTip.AppendLine(LocalizationManager.Get("TipNom", "Chữ Nôm:") + " " + string.Join(", ", rec.Nom));
                    if (rec.Mandarin.Count > 0)
                        sbTip.AppendLine("Pinyin: " + string.Join(", ", rec.Mandarin));
                    else if (_hvdicDb != null)
                    {
                        var hvList = _hvdicDb.GetEntriesByCodePoint(cp);
                        if (hvList.Count > 0 && !string.IsNullOrEmpty(hvList[0].Pinyin))
                            sbTip.AppendLine("Pinyin: " + hvList[0].Pinyin);
                    }
                    if (rec.TotalStrokes > 0 || !string.IsNullOrEmpty(rec.Radical))
                        sbTip.AppendLine(string.Format(LocalizationManager.Get("TipRadicalStrokesFmt", "Bộ: {0} ({1} nét)"), rec.Radical, rec.TotalStrokes));
                }
                else if (_hvdicDb != null)
                {
                    var hvList = _hvdicDb.GetEntriesByCodePoint(cp);
                    if (hvList.Count > 0)
                    {
                        sbTip.AppendLine(LocalizationManager.Get("TipSinoVietnamese", "Hán Việt:") + " " + hvList[0].HanViet);
                        if (!string.IsNullOrEmpty(hvList[0].Pinyin))
                            sbTip.AppendLine("Pinyin: " + hvList[0].Pinyin);
                    }
                }
                int gId, adv;
                if (_fontService.GetGlyphInfo(cp, out gId, out adv))
                {
                    sbTip.Append(string.Format("Glyph ID: #{0} | Advance: {1}", gId, adv));
                }
                _glyphToolTip.SetToolTip(btn, sbTip.ToString().TrimEnd());

                _pnlGlyphGrid.Controls.Add(btn);

                if (cp == pageStart && _selectedChar == null)
                {
                    SelectCharacter(charInfo);
                }
            }

            _pnlGlyphGrid.ResumeLayout();
        }

        private void SelectCharacter(CharacterInfo info)
        {
            _selectedChar = info;
            _lblInspectorChar.Font = _fontService.GetFontForCodePoint(info.CodePoint, 70F);
            _lblInspectorChar.Text = info.Character;
            _lblInspectorHex.Text = string.Format(LocalizationManager.Get("InspectorHexFmt", "Mã điểm: {0} ({1})"), info.HexCode, info.CodePoint);
            _lblInspectorUtf8.Text = "UTF-8: " + info.Utf8Hex;
            _lblInspectorUtf16.Text = "UTF-16: " + info.Utf16Hex;

            var rec = _unihanDb.GetByCodePoint(info.CodePoint);
            bool isNom = (rec != null && rec.Nom != null && rec.Nom.Count > 0);
            string classification = isNom ? LocalizationManager.Get("ClassNomBadge", " • [Chữ Nôm]") : LocalizationManager.Get("ClassHanziBadge", " • [Chữ Hán]");
            _lblInspectorBlock.Text = string.Format(LocalizationManager.Get("InspectorBlockFmt", "Khối: {0}{1}"), info.BlockName, classification);
            if (rec != null)
            {
                _lblInspectorStrokes.Text = string.Format(LocalizationManager.Get("InspectorStrokesFmt", "Số nét: {0} | Bộ thủ: {1}"), rec.TotalStrokes > 0 ? rec.TotalStrokes.ToString() : "--", !string.IsNullOrEmpty(rec.Radical) ? rec.Radical : "--");
                if (rec.Vietnamese.Count > 0) info.HanViet = string.Join(", ", rec.Vietnamese);
            }
            else
            {
                _lblInspectorStrokes.Text = LocalizationManager.Get("InspectorStrokesEmpty", "Số nét / Bộ thủ: --");
            }
            if (string.IsNullOrEmpty(info.HanViet) && _hvdicDb != null)
            {
                var hvList = _hvdicDb.GetEntriesByCodePoint(info.CodePoint);
                if (hvList.Count > 0 && !string.IsNullOrEmpty(hvList[0].HanViet))
                {
                    info.HanViet = hvList[0].HanViet;
                }
            }

            int gId, adv;
            if (_fontService.GetGlyphInfo(info.CodePoint, out gId, out adv))
            {
                _lblInspectorGlyphId.Text = string.Format(LocalizationManager.Get("InspectorGlyphIdFmt", "Glyph ID: #{0} | Độ rộng: {1}"), gId, adv);
            }
            else
            {
                _lblInspectorGlyphId.Text = LocalizationManager.Get("InspectorGlyphIdEmpty", "Glyph ID: -- | Độ rộng: --");
            }

            _statusInfoLabel.Text = string.Format(LocalizationManager.Get("StatusSelectedCharFmt", "Ký tự: {0} ({1})"), info.Character, info.HexCode);
        }

        private void OnJumpToHex()
        {
            string hex = _txtJumpHex.Text.Trim().Replace("U+", "").Replace("0x", "");
            int cp;
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out cp))
            {
                var block = UnicodeCatalog.FindBlock(cp);
                if (block != null)
                {
                    _currentStartCp = block.StartCp;
                    _currentEndCp = block.EndCp;
                    int offset = cp - _currentStartCp;
                    _currentPage = offset / PageSize;
                    if (_fontService.AutoCompositeEnabled)
                    {
                        if (_fontService.AutoSelectFontForBlock(block))
                        {
                            UpdateStatus();
                        }
                    }
                    RefreshGlyphGrid();
                    SelectCharacter(new CharacterInfo(cp));
                }
                else
                {
                    _currentStartCp = cp;
                    _currentEndCp = cp + 120;
                    _currentPage = 0;
                    if (_fontService.AutoCompositeEnabled)
                    {
                        if (_fontService.AutoSelectFontForRange(_currentStartCp, _currentEndCp))
                        {
                            UpdateStatus();
                        }
                    }
                    RefreshGlyphGrid();
                    SelectCharacter(new CharacterInfo(cp));
                }
            }
            else
            {
                MessageBox.Show(LocalizationManager.Get("InvalidHexError", "Mã Hex không hợp lệ."), LocalizationManager.Get("ErrorTitle", "Lỗi"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        #endregion

        #region Tab 2: Phonetic & Online Search
        private TabPage CreateSearchTab()
        {
            var tab = new TabPage(LocalizationManager.Get("TabSearch", "Tra cứu âm đọc & Từ điển Online"));

            // Top Search Bar
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblPrompt = new Label
            {
                Text = LocalizationManager.Get("SearchPrompt", "Nhập phiên âm (thiên, tiān, trời, ly), tiền tố (v:, nom:, py:, d:), mã Hex hoặc dán chữ Hán/Nôm:"),
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 4),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            var flowRow1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(0, 4, 0, 2)
            };

            _txtSearchInput = new TextBox
            {
                Width = 280,
                Font = new Font("Segoe UI", 10F),
                Margin = new Padding(0, 2, 8, 2)
            };
            _txtSearchInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) ExecuteSearchAsync(_txtSearchInput.Text); };

            _cbSearchLang = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 180,
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(0, 2, 8, 2)
            };
            _cbSearchLang.Items.AddRange(new object[] {
                LocalizationManager.Get("LangAll", "Tất cả ngữ hệ"),
                LocalizationManager.Get("LangVn", "Tiếng Việt (v:)"),
                LocalizationManager.Get("LangPy", "Pinyin / Quan thoại (py:)"),
                LocalizationManager.Get("LangCantonese", "Quảng Đông (c:)"),
                LocalizationManager.Get("LangJa", "Tiếng Nhật (ja:)"),
                LocalizationManager.Get("LangKo", "Tiếng Hàn (ko:)"),
                LocalizationManager.Get("LangMeaning", "Nghĩa từ điển (d:)")
            });
            _cbSearchLang.SelectedIndex = 0;
            _cbSearchLang.SelectedIndexChanged += (s, e) => { if (!string.IsNullOrEmpty(_txtSearchInput.Text)) ExecuteSearchAsync(_txtSearchInput.Text); };

            _btnExecuteSearch = new Button
            {
                Text = LocalizationManager.Get("SearchBtn", "🔍 Tra cứu"),
                Width = 105,
                Height = 28,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Margin = new Padding(0, 1, 12, 2),
                FlatStyle = FlatStyle.System
            };
            _btnExecuteSearch.Click += (s, e) => ExecuteSearchAsync(_txtSearchInput.Text);

            _chkPureNom = new CheckBox
            {
                Text = LocalizationManager.Get("PureNomCheck", "Chỉ Chữ Nôm (Unihan/Online)"),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 83, 9),
                Margin = new Padding(0, 6, 8, 2)
            };
            _chkPureNom.CheckedChanged += (s, e) => { if (!string.IsNullOrEmpty(_txtSearchInput.Text)) ExecuteSearchAsync(_txtSearchInput.Text); };

            flowRow1.Controls.AddRange(new Control[] {
                _txtSearchInput, _cbSearchLang, _btnExecuteSearch, _chkPureNom
            });

            // Flow 2: Granular Column Checkboxes & Match Rules
            var flowRow2 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(0, 2, 0, 4)
            };

            _lblFilterCols = new Label
            {
                Text = LocalizationManager.Get("FilterColsLabel", "Khớp cột:"),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Margin = new Padding(0, 5, 4, 2)
            };

            _chkColHanViet = new CheckBox
            {
                Text = LocalizationManager.Get("FilterColHanViet", "Hán Việt"),
                AutoSize = true,
                Checked = true,
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 4, 8, 2)
            };
            _chkColHanViet.CheckedChanged += (s, e) => { if (!string.IsNullOrEmpty(_txtSearchInput.Text)) ExecuteSearchAsync(_txtSearchInput.Text); };

            _chkColNom = new CheckBox
            {
                Text = LocalizationManager.Get("FilterColNom", "IDS"),
                AutoSize = true,
                Checked = true,
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 4, 8, 2)
            };
            _chkColNom.CheckedChanged += (s, e) => { if (!string.IsNullOrEmpty(_txtSearchInput.Text)) ExecuteSearchAsync(_txtSearchInput.Text); };

            _chkColPinyin = new CheckBox
            {
                Text = LocalizationManager.Get("FilterColPinyin", "Pinyin"),
                AutoSize = true,
                Checked = true,
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 4, 8, 2)
            };
            _chkColPinyin.CheckedChanged += (s, e) => { if (!string.IsNullOrEmpty(_txtSearchInput.Text)) ExecuteSearchAsync(_txtSearchInput.Text); };

            _chkColMeaning = new CheckBox
            {
                Text = LocalizationManager.Get("FilterColMeaning", "Nghĩa từ điển"),
                AutoSize = true,
                Checked = false,
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 4, 12, 2)
            };
            _chkColMeaning.CheckedChanged += (s, e) => { if (!string.IsNullOrEmpty(_txtSearchInput.Text)) ExecuteSearchAsync(_txtSearchInput.Text); };

            _lblFilterRules = new Label
            {
                Text = LocalizationManager.Get("FilterRulesLabel", "| Quy tắc:"),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Margin = new Padding(0, 5, 4, 2)
            };

            _chkMatchExactWord = new CheckBox
            {
                Text = LocalizationManager.Get("MatchExactWord", "Khớp toàn từ"),
                AutoSize = true,
                Checked = false,
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 4, 8, 2)
            };
            _chkMatchExactWord.CheckedChanged += (s, e) => { if (!string.IsNullOrEmpty(_txtSearchInput.Text)) ExecuteSearchAsync(_txtSearchInput.Text); };

            _chkMatchExactTone = new CheckBox
            {
                Text = LocalizationManager.Get("MatchExactTone", "Đúng dấu thanh"),
                AutoSize = true,
                Checked = false,
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 4, 8, 2)
            };
            _chkMatchExactTone.CheckedChanged += (s, e) => { if (!string.IsNullOrEmpty(_txtSearchInput.Text)) ExecuteSearchAsync(_txtSearchInput.Text); };

            flowRow2.Controls.AddRange(new Control[] {
                _lblFilterCols, _chkColHanViet, _chkColNom, _chkColPinyin, _chkColMeaning,
                _lblFilterRules, _chkMatchExactWord, _chkMatchExactTone
            });

            _lblSearchCount = new Label
            {
                Text = "",
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(2, 4, 0, 2),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139)
            };

            topPanel.Controls.Add(_lblSearchCount);
            topPanel.Controls.Add(flowRow2);
            topPanel.Controls.Add(flowRow1);
            topPanel.Controls.Add(lblPrompt);

            // Split: Results List (Left) & Definition Details (Right)
            _searchSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2
            };

            _lvSearchResults = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = _quocNguFont,
                OwnerDraw = true
            };

            var imgList = new ImageList { ImageSize = new Size(1, 26) };
            _lvSearchResults.SmallImageList = imgList;

            _lvSearchResults.Columns.Add(LocalizationManager.Get("ColChar", "Chữ / Từ"), 70);
            _lvSearchResults.Columns.Add(LocalizationManager.Get("ColHex", "Mã Hex"), 80);
            _lvSearchResults.Columns.Add(LocalizationManager.Get("ColSource", "Nguồn dữ liệu"), 160);
            _lvSearchResults.Columns.Add(LocalizationManager.Get("ColMatched", "Đọc khớp"), 90);
            _lvSearchResults.Columns.Add(LocalizationManager.Get("ColHanViet", "Hán Việt"), 110);
            _lvSearchResults.Columns.Add(LocalizationManager.Get("ColNom", "IDS"), 120);
            _lvSearchResults.Columns.Add(LocalizationManager.Get("ColPinyin", "Pinyin"), 80);
            _lvSearchResults.Columns.Add(LocalizationManager.Get("ColStrokes", "Số nét"), 55);
            _lvSearchResults.Columns.Add(LocalizationManager.Get("ColWebSource", "Nguồn Web (URL / Trích dẫn)"), 230);

            _lvSearchResults.DrawColumnHeader += (s, e) => e.DrawDefault = true;
            _lvSearchResults.DrawItem += (s, e) => { /* SubItem handles drawing */ };
            _lvSearchResults.DrawSubItem += (s, e) =>
            {
                bool isSelected = (e.ItemState & ListViewItemStates.Selected) != 0;
                Color backColor = isSelected ? SystemColors.Highlight : (e.ItemIndex % 2 == 1 ? Color.FromArgb(248, 250, 252) : Color.White);
                Color textColor = isSelected ? SystemColors.HighlightText : e.SubItem.ForeColor;

                using (var brush = new SolidBrush(backColor))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }

                using (var pen = new Pen(Color.FromArgb(235, 238, 242)))
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom - 1);
                }

                // Column 0 is Chinese / Nom ideograph -> CJK Typography Font
                // Column 5 is IDS structure -> SimSun / CJK Typography Font for IDC & radicals
                // Columns 1..4, 6..8 are Vietnamese Quoc Ngu / Latin readings / Hex / URLs -> Segoe UI
                Font fontToUse = (e.ColumnIndex == 0) ? e.Item.Font : (e.ColumnIndex == 5 ? _idsFont : _quocNguFont);

                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                Rectangle textRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 10), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, fontToUse, textRect, textColor, flags);
            };

            _lvSearchResults.SelectedIndexChanged += OnSearchResultSelected;
            _searchSplit.Panel1.Controls.Add(_lvSearchResults);

            var detailPnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12) };
            var topHeader = new Panel { Dock = DockStyle.Top, Height = 145 };

            _lblSearchCharBig = new Label
            {
                Text = "--",
                Width = 90,
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("SimSun", 42F, FontStyle.Regular)
            };
            _lblSearchReadings = new Label
            {
                Text = LocalizationManager.Get("SearchDetailsPrompt", "Chọn một chữ trong danh sách kết quả để xem định nghĩa chi tiết từ điển..."),
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F),
                Padding = new Padding(10, 8, 0, 0)
            };

            var actionWebBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(6, 4, 6, 4)
            };

            _btnOpenWebSource = new Button
            {
                Text = LocalizationManager.Get("OpenWebSource", "🌐 Mở trang Web nguồn"),
                AutoSize = true,
                Height = 28,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.System,
                Enabled = false
            };
            _btnOpenWebSource.Click += OnOpenWebSourceClicked;

            _btnCopyWebUrl = new Button
            {
                Text = LocalizationManager.Get("CopyWebUrl", "📋 Sao chép URL"),
                AutoSize = true,
                Height = 28,
                Font = new Font("Segoe UI", 9F),
                FlatStyle = FlatStyle.System,
                Enabled = false
            };
            _btnCopyWebUrl.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_currentWebUrl))
                {
                    Clipboard.SetText(_currentWebUrl);
                    _statusInfoLabel.Text = string.Format(LocalizationManager.Get("StatusCopiedUrlFmt", "Đã sao chép link nguồn vào bộ nhớ tạm: {0}"), _currentWebUrl);
                }
            };

            _lblWebSourceCitation = new Label
            {
                Text = "",
                AutoSize = true,
                Margin = new Padding(10, 6, 0, 0),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = Color.FromArgb(71, 85, 105)
            };

            actionWebBar.Controls.AddRange(new Control[] { _btnOpenWebSource, _btnCopyWebUrl, _lblWebSourceCitation });
            topHeader.Controls.AddRange(new Control[] { _lblSearchReadings, _lblSearchCharBig, actionWebBar });

            _txtSearchDetails = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 9.5F),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            detailPnl.Controls.Add(_txtSearchDetails);
            detailPnl.Controls.Add(topHeader);
            _searchSplit.Panel2.Controls.Add(detailPnl);

            tab.Controls.Add(_searchSplit);
            tab.Controls.Add(topPanel);
            _searchSplit.BringToFront();

            tab.SizeChanged += (s, e) => AdjustSearchSplitter();
            tab.VisibleChanged += (s, e) => { if (tab.Visible) AdjustSearchSplitter(); };
            return tab;
        }

        private void AdjustSearchSplitter()
        {
            if (_searchSplit == null || _searchSplit.IsDisposed) return;
            try
            {
                int totalW = _searchSplit.Width;
                if (totalW > 500)
                {
                    int desiredRightWidth = 325;
                    int targetDist = totalW - desiredRightWidth;
                    if (targetDist > 100 && targetDist < totalW - 100)
                    {
                        _searchSplit.SplitterDistance = targetDist;
                    }
                }
            }
            catch { }
        }

        private async void ExecuteSearchAsync(string text)
        {
            string raw = (text ?? "").Trim();
            if (string.IsNullOrEmpty(raw)) return;

            _btnExecuteSearch.Enabled = false;
            _statusInfoLabel.Text = LocalizationManager.Get("StatusSearching", "Đang tra cứu cơ sở dữ liệu Unihan & Online API...");
            _lvSearchResults.Items.Clear();
            _txtSearchDetails.Text = LocalizationManager.Get("SearchDetailsSearching", "Đang tra cứu cơ sở dữ liệu và gọi Online API...");
            _currentWebUrl = "";
            _btnOpenWebSource.Enabled = false;
            _btnCopyWebUrl.Enabled = false;
            _lblWebSourceCitation.Text = "";

            string targetLang = "all";
            switch (_cbSearchLang.SelectedIndex)
            {
                case 1: targetLang = "vietnamese"; break;
                case 2: targetLang = "mandarin"; break;
                case 3: targetLang = "cantonese"; break;
                case 4: targetLang = "japanese"; break;
                case 5: targetLang = "korean"; break;
                case 6: targetLang = "semantic"; break;
            }

            var filterOptions = new SearchFilterOptions
            {
                TargetLanguage = targetLang,
                PureNomOnly = _chkPureNom.Checked,
                MatchColHanViet = _chkColHanViet.Checked,
                MatchColNom = _chkColNom.Checked,
                MatchColPinyin = _chkColPinyin.Checked,
                MatchColMeaning = _chkColMeaning.Checked,
                MatchExactWord = _chkMatchExactWord.Checked,
                MatchExactTone = _chkMatchExactTone.Checked,
                Limit = 150
            };

            var localMatches = _unihanDb.SearchByPronunciation(raw, filterOptions);
            var hvdicMatches = (_hvdicDb != null && _hvdicDb.IsLoaded) ? _hvdicDb.Search(raw, filterOptions) : new List<HvDicSearchResult>();
            var seenCp = new HashSet<int>();
            var lviMap = new Dictionary<int, ListViewItem>();

            _lvSearchResults.BeginUpdate();

            foreach (var m in localMatches)
            {
                bool isNom = m.Record.IsPureNom;
                if (_chkPureNom.Checked && !isNom)
                    continue;

                seenCp.Add(m.Record.CodePoint);
                var lvi = new ListViewItem(m.Record.Character);
                lvi.UseItemStyleForSubItems = false;
                lvi.Font = _fontService.GetFontForCodePoint(m.Record.CodePoint, 12F);

                bool hasHvDic = (_hvdicDb != null && _hvdicDb.GetEntriesByCodePoint(m.Record.CodePoint).Count > 0);
                string tagSource;
                if (hasHvDic)
                {
                    tagSource = isNom
                        ? LocalizationManager.Get("TagCombinedNom", "[Unihan + Online] Chữ Nôm")
                        : LocalizationManager.Get("TagCombinedHvDic", "[Unihan + HVDic]");
                }
                else
                {
                    tagSource = isNom
                        ? LocalizationManager.Get("TagUnihanNom", "[Unihan] Chữ Nôm")
                        : LocalizationManager.Get("TagUnihanHanzi", "[Unihan] Chữ Hán");
                }

                var subHex = lvi.SubItems.Add(m.Record.HexCode);
                subHex.Font = _quocNguFont;

                var subSrc = lvi.SubItems.Add(tagSource);
                subSrc.Font = _quocNguFont;
                if (hasHvDic) subSrc.ForeColor = Color.FromArgb(2, 132, 199);

                var subReading = lvi.SubItems.Add(m.MatchedReading);
                subReading.Font = _quocNguFont;

                var subHv = lvi.SubItems.Add(string.Join(", ", m.Record.Vietnamese));
                subHv.Font = _quocNguFont;

                string ids = HanIdsCorpusIndex.GetExpandedIdsForCodePoint(m.Record.CodePoint);
                var subIds = lvi.SubItems.Add(!string.IsNullOrEmpty(ids) ? ids : "--");
                subIds.Font = _idsFont;

                var subPy = lvi.SubItems.Add(string.Join(", ", m.Record.Mandarin));
                subPy.Font = _quocNguFont;

                var subStrokes = lvi.SubItems.Add(m.Record.TotalStrokes > 0 ? m.Record.TotalStrokes.ToString() : "--");
                subStrokes.Font = _quocNguFont;

                var subUrl = lvi.SubItems.Add(hasHvDic
                    ? LocalizationManager.Get("TagHvDicSource", "Nội bộ (HVDic Thi viện .wrd)")
                    : LocalizationManager.Get("TagLocalUnihan", "Nội bộ (Embedded Unihan DB)"));
                subUrl.Font = _quocNguFont;

                lvi.Tag = m.Record;
                _lvSearchResults.Items.Add(lvi);
                lviMap[m.Record.CodePoint] = lvi;
            }

            int hvdicAddedCount = 0;
            if (hvdicMatches != null && !_chkPureNom.Checked)
            {
                foreach (var hm in hvdicMatches)
                {
                    if (hm.HanItem.CodePoint > 0 && seenCp.Contains(hm.HanItem.CodePoint))
                        continue;

                    if (hm.HanItem.CodePoint > 0)
                        seenCp.Add(hm.HanItem.CodePoint);

                    var lvi = new ListViewItem(hm.HanItem.Text);
                    lvi.UseItemStyleForSubItems = false;
                    lvi.Font = _fontService.GetFontForCodePoint(hm.HanItem.CodePoint, 12F);

                    string tagSource = LocalizationManager.Get("TagHvDic", "[HVDic Thi viện]");

                    var subHex = lvi.SubItems.Add(hm.HanItem.CodePoint > 0 ? "U+" + hm.HanItem.CodePoint.ToString("X4") : "");
                    subHex.Font = _quocNguFont;

                    var subSrc = lvi.SubItems.Add(tagSource);
                    subSrc.Font = _quocNguFont;
                    subSrc.ForeColor = Color.FromArgb(2, 132, 199);

                    var subReading = lvi.SubItems.Add(hm.MatchedReading);
                    subReading.Font = _quocNguFont;

                    var subHv = lvi.SubItems.Add(hm.Entry.HanViet);
                    subHv.Font = _quocNguFont;

                    string ids = (hm.HanItem.CodePoint > 0) ? HanIdsCorpusIndex.GetExpandedIdsForCodePoint(hm.HanItem.CodePoint) : null;
                    var subIds = lvi.SubItems.Add(!string.IsNullOrEmpty(ids) ? ids : "--");
                    subIds.Font = _idsFont;

                    var subPy = lvi.SubItems.Add(hm.Entry.Pinyin ?? "");
                    subPy.Font = _quocNguFont;

                    int strokes = 0;
                    if (hm.HanItem.CodePoint > 0)
                    {
                        var uRec = _unihanDb.GetByCodePoint(hm.HanItem.CodePoint);
                        if (uRec != null && uRec.TotalStrokes > 0) strokes = uRec.TotalStrokes;
                    }
                    var subStrokes = lvi.SubItems.Add(strokes > 0 ? strokes.ToString() : "--");
                    subStrokes.Font = _quocNguFont;

                    var subUrl = lvi.SubItems.Add(LocalizationManager.Get("TagHvDicSource", "Nội bộ (HVDic Thi viện .wrd)"));
                    subUrl.Font = _quocNguFont;

                    lvi.Tag = hm;
                    _lvSearchResults.Items.Add(lvi);
                    if (hm.HanItem.CodePoint > 0) lviMap[hm.HanItem.CodePoint] = lvi;
                    hvdicAddedCount++;
                }
            }

            _lvSearchResults.EndUpdate();

            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            _lblSearchCount.Text = isVi
                ? string.Format("Cơ sở dữ liệu nội bộ: {0} kết quả (Unihan: {1} | HVDic: {2}) | Đang kết nối Online API...", _lvSearchResults.Items.Count, localMatches.Count, hvdicAddedCount)
                : string.Format("Internal Databases: {0} results (Unihan: {1} | HVDic: {2}) | Connecting to Online API...", _lvSearchResults.Items.Count, localMatches.Count, hvdicAddedCount);

            if (_lvSearchResults.Items.Count > 0)
            {
                DisplaySearchResultDetails(_lvSearchResults.Items[0].Tag);
            }

            // Concurrent Online Query (Thi Vien + Wiktionary)
            bool onlineOk = false;
            int onlineCount = 0;
            try
            {
                var onlineResult = await _dictService.LookupAsync(raw);
                if (onlineResult != null && onlineResult.Items != null && onlineResult.Items.Count > 0)
                {
                    onlineOk = true;
                    onlineCount = onlineResult.Items.Count;
                    _lvSearchResults.BeginUpdate();

                    int validOnlineCount = 0;
                    foreach (var item in onlineResult.Items)
                    {
                        bool isPlane2OrPua = item.CodePoint >= 0x20000 || (item.CodePoint >= 0xE000 && item.CodePoint <= 0xF8FF);
                        var localRec = item.CodePoint > 0 ? _unihanDb.GetByCodePoint(item.CodePoint) : null;
                        bool localIsPureNom = (localRec != null && localRec.IsPureNom);

                        // LINGUISTIC RULE: Chữ Nôm thuần túy xuất hiện từ Plane 2 (Ext B..F: U+20000+) hoặc PUA.
                        // Các chữ trong BMP (U+4E00 - U+9FFF) có âm Hán-Việt là Chữ Hán đọc theo âm Việt!
                        bool onlineIsNom = (isPlane2OrPua && !string.IsNullOrEmpty(item.Nom)) || localIsPureNom;

                        if (_chkPureNom.Checked && !onlineIsNom)
                        {
                            continue; // STRICTLY EXCLUDE NON-NOM / HANZI!
                        }

                        // Column and Matching checks for Online API
                        bool anyColSelected = filterOptions.MatchColHanViet || filterOptions.MatchColNom || filterOptions.MatchColPinyin || filterOptions.MatchColMeaning;
                        if (anyColSelected && !OnlineDictionaryService.IsSingleIdeograph(raw))
                        {
                            bool colMatched = false;
                            if (filterOptions.MatchColHanViet && !string.IsNullOrEmpty(item.HanViet))
                            {
                                if (IsReadingMatch(item.HanViet, raw, filterOptions.MatchExactWord, filterOptions.MatchExactTone))
                                    colMatched = true;
                            }
                            if (filterOptions.MatchColNom && !string.IsNullOrEmpty(item.Nom))
                            {
                                if (IsReadingMatch(item.Nom, raw, filterOptions.MatchExactWord, filterOptions.MatchExactTone))
                                    colMatched = true;
                            }
                            if (filterOptions.MatchColPinyin && !string.IsNullOrEmpty(item.Pinyin))
                            {
                                if (IsReadingMatch(item.Pinyin, raw, filterOptions.MatchExactWord, filterOptions.MatchExactTone))
                                    colMatched = true;
                            }
                            if (filterOptions.MatchColMeaning && item.Definitions != null && item.Definitions.Count > 0)
                            {
                                colMatched = true;
                            }
                            if (!colMatched)
                            {
                                continue;
                            }
                        }

                        // Strict language verification for phonetic/text queries (preserve single ideograph lookups)
                        if (targetLang != "all" && !OnlineDictionaryService.IsSingleIdeograph(raw))
                        {
                            if (targetLang == "vietnamese")
                            {
                                bool hasVn = !string.IsNullOrEmpty(item.HanViet) || !string.IsNullOrEmpty(item.Nom);
                                if (!hasVn) continue;
                            }
                            else if (targetLang == "mandarin")
                            {
                                bool hasPy = !string.IsNullOrEmpty(item.Pinyin);
                                if (!hasPy) continue;
                            }
                            else
                            {
                                continue;
                            }
                        }

                        validOnlineCount++;

                        if (item.CodePoint > 0 && lviMap.ContainsKey(item.CodePoint))
                        {
                            // Existing local row enriched by Online API
                            var lvi = lviMap[item.CodePoint];
                            var rec = item.CodePoint > 0 ? _unihanDb.GetByCodePoint(item.CodePoint) : null;
                            bool recIsNom = (rec != null && rec.IsPureNom) || (isPlane2OrPua && !string.IsNullOrEmpty(item.Nom));
                            lvi.SubItems[2].Text = recIsNom
                                ? LocalizationManager.Get("TagCombinedNom", "[Unihan + Online] Chữ Nôm")
                                : LocalizationManager.Get("TagCombinedHanzi", "[Unihan + Online] Chữ Hán");
                            lvi.SubItems[8].Text = item.WebUrl;
                            lvi.ForeColor = Color.FromArgb(14, 116, 144); // Teal
                            if ((lvi.SubItems[5].Text == "--" || string.IsNullOrEmpty(lvi.SubItems[5].Text)) && item.CodePoint > 0)
                            {
                                string ids = HanIdsCorpusIndex.GetExpandedIdsForCodePoint(item.CodePoint);
                                if (!string.IsNullOrEmpty(ids)) lvi.SubItems[5].Text = ids;
                            }
                            if (string.IsNullOrEmpty(lvi.SubItems[4].Text) && !string.IsNullOrEmpty(item.HanViet))
                                lvi.SubItems[4].Text = item.HanViet;
                        }
                        else if (item.CodePoint == 0 || seenCp.Add(item.CodePoint))
                        {
                            // New row discovered from Online API!
                            var lvi = new ListViewItem(item.Character);
                            lvi.UseItemStyleForSubItems = false;
                            lvi.Font = _fontService.GetFontForCodePoint(item.CodePoint, 12F);

                            string onlineTag = onlineIsNom
                                ? LocalizationManager.Get("TagOnlineNom", "[Online API] Chữ Nôm")
                                : LocalizationManager.Get("TagOnlineHanzi", "[Online API] Chữ Hán");

                            var subHex = lvi.SubItems.Add(item.HexCode);
                            subHex.Font = _quocNguFont;

                            var subSrc = lvi.SubItems.Add(onlineTag);
                            subSrc.Font = _quocNguFont;
                            subSrc.ForeColor = Color.FromArgb(16, 120, 72);

                            var subReading = lvi.SubItems.Add(item.MatchedReading);
                            subReading.Font = _quocNguFont;

                            var subHv = lvi.SubItems.Add(item.HanViet ?? "");
                            subHv.Font = _quocNguFont;

                            string ids = (item.CodePoint > 0) ? HanIdsCorpusIndex.GetExpandedIdsForCodePoint(item.CodePoint) : null;
                            var subIds = lvi.SubItems.Add(!string.IsNullOrEmpty(ids) ? ids : "--");
                            subIds.Font = _idsFont;

                            var subPy = lvi.SubItems.Add(item.Pinyin ?? "");
                            subPy.Font = _quocNguFont;

                            var subStrokes = lvi.SubItems.Add(item.TotalStrokes > 0 ? item.TotalStrokes.ToString() : "--");
                            subStrokes.Font = _quocNguFont;

                            var subUrl = lvi.SubItems.Add(item.WebUrl ?? "");
                            subUrl.Font = _quocNguFont;

                            lvi.ForeColor = Color.FromArgb(16, 120, 72); // Emerald Green for Online API
                            lvi.Tag = item;
                            _lvSearchResults.Items.Add(lvi);
                            if (item.CodePoint > 0) lviMap[item.CodePoint] = lvi;
                        }
                    }

                    _lvSearchResults.EndUpdate();
                    onlineCount = validOnlineCount;

                    // If local was empty or first search, display top online result
                    if (localMatches.Count == 0 && _lvSearchResults.Items.Count > 0)
                    {
                        DisplaySearchResultDetails(_lvSearchResults.Items[0].Tag);
                    }
                }
                else if (onlineResult != null && onlineResult.Definitions.Count > 0 && localMatches.Count == 0 && !_chkPureNom.Checked)
                {
                    onlineOk = true;
                    // Word definition from Wiktionary/Thi Vien
                    var wordItem = new OnlineSearchResultItem
                    {
                        Character = raw,
                        DataSource = isVi ? "[Online API] Wiktionary & Thi Viện" : "[Online API] Wiktionary & Thi Vien",
                        MatchedReading = raw,
                        WebUrl = onlineResult.PrimaryWebUrl ?? ("https://hvdic.thivien.net/hv/" + Uri.EscapeDataString(raw)),
                        WebCitation = isVi ? "Thi Viện & Wiktionary Tiếng Việt" : "Thi Vien & Vietnamese Wiktionary",
                        Definitions = onlineResult.Definitions
                    };
                    DisplaySearchResultDetails(wordItem);
                }
            }
            catch
            {
                onlineOk = false;
            }
            finally
            {
                _btnExecuteSearch.Enabled = true;
                if (onlineOk && onlineCount > 0)
                {
                    _lblSearchCount.Text = isVi
                        ? string.Format("Tìm thấy {0} kết quả (Unihan: {1} | HVDic: {2} | Online API: {3})", _lvSearchResults.Items.Count, localMatches.Count, hvdicAddedCount, onlineCount)
                        : string.Format("Found {0} results (Unihan: {1} | HVDic: {2} | Online API: {3})", _lvSearchResults.Items.Count, localMatches.Count, hvdicAddedCount, onlineCount);
                }
                else
                {
                    _lblSearchCount.Text = isVi
                        ? string.Format("Tìm thấy {0} kết quả (Unihan: {1} | HVDic Thi viện: {2}) [Nội bộ hoàn tất]", _lvSearchResults.Items.Count, localMatches.Count, hvdicAddedCount)
                        : string.Format("Found {0} results (Unihan: {1} | HVDic Thi Vien: {2}) [Internal complete]", _lvSearchResults.Items.Count, localMatches.Count, hvdicAddedCount);
                }
                _statusInfoLabel.Text = string.Format(LocalizationManager.Get("StatusSearchCompleteFmt", "Tra cứu hoàn tất ({0} kết quả)"), _lvSearchResults.Items.Count);
            }
        }

        private static bool IsReadingMatch(string field, string query, bool exactWord, bool exactTone)
        {
            if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(query)) return false;
            string cleanQ = query.Trim().ToLowerInvariant();
            string qNorm = LinguisticsUtils.NormalizeVietnameseTonePlacement(cleanQ);
            string qToneless = LinguisticsUtils.StripVietnameseDiacritics(qNorm);

            string[] tokens = field.Split(new char[] { ',', ';', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var tok in tokens)
            {
                string r = tok.Trim().ToLowerInvariant();
                string rNorm = LinguisticsUtils.NormalizeVietnameseTonePlacement(r);
                if (exactTone)
                {
                    if (exactWord)
                    {
                        if (rNorm == qNorm) return true;
                    }
                    else
                    {
                        if (rNorm.StartsWith(qNorm)) return true;
                    }
                }
                else
                {
                    string rToneless = LinguisticsUtils.StripVietnameseDiacritics(rNorm);
                    if (exactWord)
                    {
                        if (rToneless == qToneless) return true;
                    }
                    else
                    {
                        if (rToneless.StartsWith(qToneless)) return true;
                    }
                }
            }
            return false;
        }

        private async void OnSearchResultSelected(object s, EventArgs e)
        {
            if (_lvSearchResults.SelectedItems.Count > 0)
            {
                var lvi = _lvSearchResults.SelectedItems[0];
                object tag = lvi.Tag;
                DisplaySearchResultDetails(tag);

                string charText = lvi.SubItems[0].Text;
                if (!string.IsNullOrEmpty(charText))
                {
                    try
                    {
                        var charOnline = await _dictService.LookupAsync(charText);
                        if (charOnline != null && (charOnline.Definitions.Count > 0 || charOnline.NomReadings.Count > 0))
                        {
                            DisplaySearchResultDetails(tag, charOnline);
                        }
                    }
                    catch { }
                }
            }
        }

        private void DisplaySearchResultDetails(object obj, DictionaryLookupResult liveOnline = null)
        {
            if (obj == null) return;
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            string character = "";
            int codePoint = 0;
            string hexCode = "";
            string dataSource = "";
            string webUrl = "";
            string webCitation = "";
            string hanViet = "";
            string nom = "";
            string pinyin = "";
            int totalStrokes = 0;
            string radical = "";
            List<DictionaryDefinition> defs = new List<DictionaryDefinition>();
            UnihanRecord unihanRec = null;

            if (obj is UnihanRecord)
            {
                unihanRec = (UnihanRecord)obj;
                character = unihanRec.Character;
                codePoint = unihanRec.CodePoint;
                hexCode = unihanRec.HexCode;
                dataSource = isVi ? "[Unihan DB Nội bộ]" : "[Local Unihan DB]";
                webUrl = "https://hvdic.thivien.net/whv/" + Uri.EscapeDataString(character);
                webCitation = isVi ? "Cơ sở dữ liệu Unihan toàn diện (Embedded Portable Resource)" : "Comprehensive Unihan Database (Embedded Portable Resource)";
                hanViet = unihanRec.Vietnamese.Count > 0 ? string.Join(", ", unihanRec.Vietnamese) : "--";
                nom = unihanRec.Nom.Count > 0 ? string.Join(", ", unihanRec.Nom) : "--";
                pinyin = unihanRec.Mandarin.Count > 0 ? string.Join(", ", unihanRec.Mandarin) : "--";
                totalStrokes = unihanRec.TotalStrokes;
                radical = unihanRec.Radical;
            }
            else if (obj is HvDicSearchResult)
            {
                var hm = (HvDicSearchResult)obj;
                character = hm.HanItem.Text;
                codePoint = hm.HanItem.CodePoint;
                hexCode = codePoint > 0 ? "U+" + codePoint.ToString("X4") : "";
                dataSource = isVi ? "[Nội bộ] HVDic Thi viện" : "[Internal] HVDic Thi Vien";
                webUrl = "https://hvdic.thivien.net/whv/" + Uri.EscapeDataString(character);
                webCitation = isVi ? "Từ điển Hán-Việt Thi Viện (HVDic.wrd nội bộ)" : "Thi Vien Sino-Vietnamese Dictionary (Internal HVDic.wrd)";
                hanViet = hm.Entry.HanViet;
                nom = "--";
                pinyin = !string.IsNullOrEmpty(hm.Entry.Pinyin) ? hm.Entry.Pinyin : "--";

                if (codePoint > 0)
                {
                    unihanRec = _unihanDb.GetByCodePoint(codePoint);
                    if (unihanRec != null)
                    {
                        if (unihanRec.Vietnamese.Count > 0 && hanViet == "--") hanViet = string.Join(", ", unihanRec.Vietnamese);
                        if (unihanRec.Nom.Count > 0) nom = string.Join(", ", unihanRec.Nom);
                        if (unihanRec.Mandarin.Count > 0 && pinyin == "--") pinyin = string.Join(", ", unihanRec.Mandarin);
                        if (totalStrokes <= 0) totalStrokes = unihanRec.TotalStrokes;
                        if (string.IsNullOrEmpty(radical)) radical = unihanRec.Radical;
                    }
                }
            }
            else if (obj is OnlineSearchResultItem)
            {
                var item = (OnlineSearchResultItem)obj;
                character = item.Character;
                codePoint = item.CodePoint;
                hexCode = item.HexCode;
                dataSource = item.DataSource;
                webUrl = item.WebUrl;
                webCitation = item.WebCitation;
                hanViet = !string.IsNullOrEmpty(item.HanViet) ? item.HanViet : "--";
                nom = !string.IsNullOrEmpty(item.Nom) ? item.Nom : "--";
                pinyin = !string.IsNullOrEmpty(item.Pinyin) ? item.Pinyin : "--";
                totalStrokes = item.TotalStrokes;
                radical = item.Radical;
                defs.AddRange(item.Definitions);

                // Check if Unihan has this character too
                if (codePoint > 0)
                {
                    unihanRec = _unihanDb.GetByCodePoint(codePoint);
                    if (unihanRec != null)
                    {
                        if (hanViet == "--" && unihanRec.Vietnamese.Count > 0) hanViet = string.Join(", ", unihanRec.Vietnamese);
                        if (nom == "--" && unihanRec.Nom.Count > 0) nom = string.Join(", ", unihanRec.Nom);
                        if (pinyin == "--" && unihanRec.Mandarin.Count > 0) pinyin = string.Join(", ", unihanRec.Mandarin);
                        if (totalStrokes <= 0) totalStrokes = unihanRec.TotalStrokes;
                        if (string.IsNullOrEmpty(radical)) radical = unihanRec.Radical;
                    }
                }
            }

            if (liveOnline != null)
            {
                if (!string.IsNullOrEmpty(liveOnline.PrimaryWebUrl)) webUrl = liveOnline.PrimaryWebUrl;
                if (liveOnline.Definitions.Count > 0)
                {
                    foreach (var d in liveOnline.Definitions)
                    {
                        if (!defs.Exists(x => x.Source == d.Source && x.Text == d.Text))
                            defs.Add(d);
                    }
                }
                if (nom == "--" && liveOnline.NomReadings.Count > 0)
                    nom = string.Join(", ", liveOnline.NomReadings);
                if (hanViet == "--" && liveOnline.HanVietReadings.Count > 0)
                    hanViet = string.Join(", ", liveOnline.HanVietReadings);
                if (totalStrokes <= 0 && liveOnline.StrokeCount > 0)
                    totalStrokes = liveOnline.StrokeCount;
                if (string.IsNullOrEmpty(radical) && !string.IsNullOrEmpty(liveOnline.Radical))
                    radical = liveOnline.Radical;
            }

            // Supplement readings from HVDic if still missing
            if (_hvdicDb != null && !string.IsNullOrEmpty(character))
            {
                var hvEntries = _hvdicDb.GetEntriesByChar(character);
                if (hvEntries.Count > 0)
                {
                    if (hanViet == "--" && !string.IsNullOrEmpty(hvEntries[0].HanViet))
                    {
                        hanViet = hvEntries[0].HanViet;
                    }
                    if (pinyin == "--" && !string.IsNullOrEmpty(hvEntries[0].Pinyin))
                    {
                        pinyin = hvEntries[0].Pinyin;
                    }
                }
            }

            bool isPlane2OrPua = (codePoint >= 0x20000 || (codePoint >= 0xE000 && codePoint <= 0xF8FF));
            bool isNom = (unihanRec != null && unihanRec.IsPureNom) ||
                         (isPlane2OrPua && nom != "--" && !string.IsNullOrEmpty(nom));
            if (!isPlane2OrPua && (unihanRec == null || !unihanRec.IsPureNom))
            {
                // In BMP, characters with Han-Viet readings are Chinese characters (Chữ Hán)
                isNom = false;
                nom = "--";
            }
            string classification = isNom
                ? LocalizationManager.Get("ClassNomFull", "Chữ Nôm (Việt Nam)")
                : LocalizationManager.Get("ClassHanziFull", "Chữ Hán (CJK Ideograph)");

            _currentWebUrl = webUrl;
            _btnOpenWebSource.Enabled = !string.IsNullOrEmpty(webUrl);
            _btnCopyWebUrl.Enabled = !string.IsNullOrEmpty(webUrl);
            _lblWebSourceCitation.Text = LocalizationManager.Get("WebSourceCitationPrefix", "Nguồn: ") + (webCitation ?? webUrl);

            _lblSearchCharBig.Font = _fontService.GetFontForCodePoint(codePoint, 48F);
            _lblSearchCharBig.Text = character;

            _lblSearchReadings.Text = string.Format(
                LocalizationManager.Get("SearchHeaderSummaryFmt"),
                !string.IsNullOrEmpty(hexCode) ? hexCode : "--",
                totalStrokes > 0 ? totalStrokes.ToString() : "--",
                !string.IsNullOrEmpty(radical) ? radical : "--",
                dataSource,
                classification,
                hanViet,
                nom,
                pinyin
            );

            var sb = new StringBuilder();
            sb.AppendLine("========================================================================");
            sb.AppendLine(string.Format(LocalizationManager.Get("SectionTitleChar"), character, !string.IsNullOrEmpty(hexCode) ? hexCode : (isVi ? "Từ ngữ" : "Word")));
            sb.AppendLine(string.Format(LocalizationManager.Get("SectionClassification"), classification));
            sb.AppendLine(string.Format(LocalizationManager.Get("SectionDataSource"), dataSource));
            if (!string.IsNullOrEmpty(webUrl)) sb.AppendLine(string.Format(LocalizationManager.Get("SectionWebUrl"), webUrl));
            if (!string.IsNullOrEmpty(webCitation)) sb.AppendLine(string.Format(LocalizationManager.Get("SectionWebCitation"), webCitation));
            sb.AppendLine("========================================================================");
            sb.AppendLine();

            sb.AppendLine(LocalizationManager.Get("SectionReadingsHeader"));
            sb.AppendLine(string.Format(LocalizationManager.Get("ReadingHanViet"), hanViet));
            sb.AppendLine(string.Format(LocalizationManager.Get("ReadingNom"), nom));
            sb.AppendLine(string.Format(LocalizationManager.Get("ReadingPinyin"), pinyin));
            string idsFormula = (codePoint > 0) ? HanIdsCorpusIndex.GetExpandedIdsForCodePoint(codePoint) : null;
            if (!string.IsNullOrEmpty(idsFormula))
            {
                sb.AppendLine(string.Format(isVi ? "Cấu trúc IDS: {0}" : "IDS Formula: {0}", idsFormula));
            }
            if (unihanRec != null)
            {
                if (unihanRec.Cantonese.Count > 0) sb.AppendLine(string.Format(LocalizationManager.Get("ReadingCantonese"), string.Join(", ", unihanRec.Cantonese)));
                if (unihanRec.Japanese.Count > 0) sb.AppendLine(string.Format(LocalizationManager.Get("ReadingJapanese"), string.Join(", ", unihanRec.Japanese)));
                if (unihanRec.KoreanRomaja.Count > 0) sb.AppendLine(string.Format(LocalizationManager.Get("ReadingKorean"), string.Join(", ", unihanRec.KoreanRomaja)));
            }
            sb.AppendLine();

            if (unihanRec != null && (!string.IsNullOrEmpty(unihanRec.ViDefinition) || !string.IsNullOrEmpty(unihanRec.Definition)))
            {
                sb.AppendLine(LocalizationManager.Get("SectionLocalUnihanHeader"));
                if (!string.IsNullOrEmpty(unihanRec.ViDefinition))
                    sb.AppendLine(string.Format(LocalizationManager.Get("DefVi"), unihanRec.ViDefinition));
                if (!string.IsNullOrEmpty(unihanRec.Definition))
                    sb.AppendLine(string.Format(LocalizationManager.Get("DefEn"), unihanRec.Definition));
                sb.AppendLine();
            }

            // HVDic Thi Viện definitions (Offline internal source)
            if (_hvdicDb != null && !string.IsNullOrEmpty(character))
            {
                string hvdicFormatted = _hvdicDb.GetFormattedDefinition(character, isVi);
                if (!string.IsNullOrEmpty(hvdicFormatted))
                {
                    sb.AppendLine(hvdicFormatted);
                    sb.AppendLine();
                }
            }

            if (defs.Count > 0)
            {
                sb.AppendLine(LocalizationManager.Get("SectionOnlineDictHeader"));
                foreach (var def in defs)
                {
                    sb.AppendLine(string.Format("[{0}]", def.Source.ToUpper()));
                    sb.AppendLine(def.Text);
                    sb.AppendLine();
                }
            }

            _txtSearchDetails.Text = sb.ToString();
        }
        #endregion

        #region Tab 3: IDS Composer Studio
        private TabPage CreateIdsComposerTab()
        {
            var tab = new TabPage(LocalizationManager.Get("TabIds", "Ghép chữ IDS"));
            _idsComposer = new IdsComposerControl
            {
                Dock = DockStyle.Fill
            };
            tab.Controls.Add(_idsComposer);
            return tab;
        }

        private TabPage CreateFontTestTab()
        {
            var tab = new TabPage(LocalizationManager.Get("TabFontTest", "Khu Vực Test Font"));
            _fontTestControl = new FontTestControl(_fontService)
            {
                Dock = DockStyle.Fill
            };
            tab.Controls.Add(_fontTestControl);
            return tab;
        }

        public void SwitchToFontTestTab(string fontPath = null)
        {
            if (_mainTabControl != null && _mainTabControl.TabPages.Count >= 4)
            {
                _mainTabControl.SelectedIndex = 3;
            }
            if (!string.IsNullOrEmpty(fontPath) && _fontTestControl != null)
            {
                _fontTestControl.LoadFontFile(fontPath);
            }
        }
        #endregion

        #region File & Font Operations
        private void OnOpenFontFile(object s, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = isVi ? "Tệp font (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc|Tất cả tệp (*.*)|*.*" : "Font Files (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc|All Files (*.*)|*.*";
                ofd.Title = isVi ? "Chọn file font TTF / OTF / TTC để mở trong CJK Playground" : "Select TTF / OTF / TTC font file to open in CJK Playground";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var info = _fontService.LoadFontFromFile(ofd.FileName);
                        _settings.LastFontPath = ofd.FileName;
                        UpdateStatus();
                        LoadBlockTree();
                        RefreshGlyphGrid();
                        string msg = isVi ? ("Đã nạp thành công font: " + info.FamilyName) : ("Successfully loaded font: " + info.FamilyName);
                        string title = isVi ? "Thành công" : "Success";
                        MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        string msg = (isVi ? "Lỗi nạp font: " : "Font load error: ") + ex.Message;
                        string title = isVi ? "Lỗi" : "Error";
                        MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void OnSelectSystemFont(object s, EventArgs e)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
            var cjkFonts = _fontService.GetSystemCjkFonts();
            if (cjkFonts.Count == 0)
            {
                MessageBox.Show(LocalizationManager.Get("SysFontNotFound", "Không tìm thấy font CJK hệ thống phổ biến."), isVi ? "Thông báo" : "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new Form { Text = LocalizationManager.Get("SysFontTitle", "Chọn font CJK hệ thống"), Size = new Size(350, 320), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                var lb = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5F) };
                foreach (var f in cjkFonts) lb.Items.Add(f);
                lb.SelectedIndex = 0;

                var btnSelect = new Button { Text = LocalizationManager.Get("SysFontSelectBtn", "Chọn font này"), Dock = DockStyle.Bottom, Height = 36, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
                btnSelect.Click += (btnS, btnE) => {
                    _fontService.SelectSystemFont(lb.SelectedItem.ToString());
                    UpdateStatus();
                    LoadBlockTree();
                    RefreshGlyphGrid();
                    form.Close();
                };

                form.Controls.Add(lb);
                form.Controls.Add(btnSelect);
                form.ShowDialog();
            }
        }

        private void OnExportSvg()
        {
            if (_selectedChar == null) return;
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = isVi ? "Ảnh vector SVG (*.svg)|*.svg" : "SVG Image (*.svg)|*.svg";
                sfd.FileName = string.Format("{0}_{1}.svg", _selectedChar.Character, _selectedChar.HexCode);
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    string svg = _fontService.ExportGlyphSvg(_selectedChar.CodePoint, _selectedChar.Character);
                    File.WriteAllText(sfd.FileName, svg, Encoding.UTF8);
                    _statusInfoLabel.Text = string.Format(LocalizationManager.Get("StatusExportedSvgFmt", "Đã xuất vector SVG: {0}"), Path.GetFileName(sfd.FileName));
                }
            }
        }

        private void OnToggleAutoCompositeFont(object s, EventArgs e)
        {
            _fontService.AutoCompositeEnabled = !_fontService.AutoCompositeEnabled;
            _settings.AutoCompositeFont = _fontService.AutoCompositeEnabled;
            _menuAutoComposite.Checked = _fontService.AutoCompositeEnabled;
            if (_fontService.AutoCompositeEnabled)
            {
                _fontService.AutoSelectFontForRange(_currentStartCp, _currentEndCp, true);
            }
            UpdateStatus();
            RefreshGlyphGrid();
        }

        private void OnOpenWebSourceClicked(object s, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentWebUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _currentWebUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                    string msg = (isVi ? "Không thể mở trình duyệt: " : "Cannot open web browser: ") + ex.Message;
                    string title = isVi ? "Thông báo" : "Notice";
                    MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void UpdateStatus()
        {
            var font = _fontService.CurrentFont;
            if (font != null)
            {
                string extra = font.SupportedCharsCount > 0
                    ? string.Format(LocalizationManager.Get("StatusFontExtraFmt", " | {0} glyphs | {1} ký tự | em={2}"), font.NumGlyphs, font.SupportedCharsCount, font.UnitsPerEm)
                    : "";
                string mode = _fontService.AutoCompositeEnabled
                    ? LocalizationManager.Get("StatusCompositeAuto", "[Composite Tự động]")
                    : LocalizationManager.Get("StatusCompositeFixed", "[Cố định]");
                string fontType = font.IsCustomFont
                    ? LocalizationManager.Get("StatusFontCustom", "(Tệp riêng)")
                    : LocalizationManager.Get("StatusFontSystem", "(Hệ thống)");
                _statusFontLabel.Text = string.Format("Font: {0} {1} {2}{3}", font.FamilyName, fontType, mode, extra);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_quocNguFont != null) _quocNguFont.Dispose();
                if (_idsFont != null) _idsFont.Dispose();
            }
            base.Dispose(disposing);
        }
        #endregion
    }
}
