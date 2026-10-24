using System;
using System.Drawing;
using System.Windows.Forms;
using CJKPlayground.Services;

namespace CJKPlayground.Forms
{
    public class AboutDialog : Form
    {
        private class InfoEntry
        {
            public string Title { get; set; }
            public string Desc { get; set; }

            public InfoEntry(string title, string desc)
            {
                Title = title;
                Desc = desc;
            }
        }

        public AboutDialog(int unihanTotalRecords = 0, int hvdicTotalRecords = 0)
        {
            InitializeComponent(unihanTotalRecords, hvdicTotalRecords);
        }

        private void InitializeComponent(int unihanTotalRecords, int hvdicTotalRecords = 0)
        {
            bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;

            Text = isVi ? "Giới thiệu CJK Playground" : "About CJK Playground";
            Size = new Size(660, 620);
            MinimumSize = new Size(600, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9F);

            var appIcon = AppIconHelper.GetAppIcon();
            if (appIcon != null)
            {
                Icon = appIcon;
            }

            // 1. Top Header Banner
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 104,
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var borderHeader = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(226, 232, 240)
            };
            pnlHeader.Controls.Add(borderHeader);

            if (appIcon != null)
            {
                var pbIcon = new PictureBox
                {
                    Location = new Point(18, 16),
                    Size = new Size(52, 52),
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    Image = new Bitmap(appIcon.ToBitmap(), new Size(48, 48))
                };
                pnlHeader.Controls.Add(pbIcon);
            }

            int textLeft = (appIcon != null) ? 80 : 20;

            var lblTitle = new Label
            {
                Text = "CJK Playground",
                Location = new Point(textLeft, 10),
                AutoSize = true,
                UseMnemonic = false,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };
            pnlHeader.Controls.Add(lblTitle);

            var lblVersion = new Label
            {
                Text = isVi ? "Phiên bản 1.0 (Bản chính thức)" : "Version 1.0 (Official Release)",
                Location = new Point(textLeft + 2, 37),
                AutoSize = true,
                UseMnemonic = false,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139)
            };
            pnlHeader.Controls.Add(lblVersion);

            var lblTagline = new Label
            {
                Text = isVi
                    ? "Bộ công cụ khám phá, tra cứu & thiết kế kiểu chữ CJK - Chữ Nôm"
                    : "Professional CJK & Vietnamese Nom Typography and Linguistic Studio",
                Location = new Point(textLeft + 2, 55),
                AutoSize = true,
                UseMnemonic = false,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = Color.FromArgb(37, 99, 235)
            };
            pnlHeader.Controls.Add(lblTagline);

            // Header Quick Credit Links
            var pnlLinksHeader = new FlowLayoutPanel
            {
                Location = new Point(textLeft + 2, 76),
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            var lblAuthorHeader = new Label
            {
                Text = isVi ? "Tác giả: Nguyễn Tuấn Vũ  •" : "Author: Nguyễn Tuấn Vũ  •",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Margin = new Padding(0, 1, 4, 0)
            };
            pnlLinksHeader.Controls.Add(lblAuthorHeader);

            var lnkGhHeader = new LinkLabel
            {
                Text = "GitHub (khanhvyNT)",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                LinkColor = Color.FromArgb(37, 99, 235),
                ActiveLinkColor = Color.FromArgb(29, 78, 216),
                VisitedLinkColor = Color.FromArgb(37, 99, 235),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 1, 4, 0)
            };
            lnkGhHeader.LinkClicked += (s, e) => OpenUrl("https://github.com/khanhvyNT");
            pnlLinksHeader.Controls.Add(lnkGhHeader);

            var lblDotHeader = new Label
            {
                Text = "•",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Margin = new Padding(0, 1, 4, 0)
            };
            pnlLinksHeader.Controls.Add(lblDotHeader);

            var lnkFbHeader = new LinkLabel
            {
                Text = "Facebook (oerlikon01)",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                LinkColor = Color.FromArgb(24, 119, 242),
                ActiveLinkColor = Color.FromArgb(13, 85, 184),
                VisitedLinkColor = Color.FromArgb(24, 119, 242),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 1, 0, 0)
            };
            lnkFbHeader.LinkClicked += (s, e) => OpenUrl("https://www.facebook.com/oerlikon01");
            pnlLinksHeader.Controls.Add(lnkFbHeader);

            pnlHeader.Controls.Add(pnlLinksHeader);

            // 2. Bottom Action Bar
            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                BackColor = Color.FromArgb(245, 246, 248)
            };

            var borderBottom = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = Color.FromArgb(226, 232, 240)
            };
            pnlBottom.Controls.Add(borderBottom);

            var lblCopyright = new Label
            {
                Text = isVi
                    ? "© Dự án CJK Playground. Mở cho cộng đồng nghiên cứu & giáo dục."
                    : "© CJK Playground Project. Open for linguistic research & education.",
                Location = new Point(14, 16),
                AutoSize = true,
                UseMnemonic = false,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139)
            };
            pnlBottom.Controls.Add(lblCopyright);

            var btnClose = new Button
            {
                Text = isVi ? "Đóng" : "Close",
                Width = 85,
                Height = 28,
                Location = new Point(535, 10),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.System,
                DialogResult = DialogResult.OK
            };
            pnlBottom.Controls.Add(btnClose);
            AcceptButton = btnClose;
            CancelButton = btnClose;

            // 3. Center Content Tabs
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F)
            };

            // Tab 1: Key Features
            var tabFeatures = new TabPage(isVi ? "Tính năng nổi bật" : "Key Features")
            {
                BackColor = Color.White,
                Padding = new Padding(12)
            };

            var pnlFeaturesList = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(6, 6, 14, 8)
            };

            var features = isVi
                ? new InfoEntry[]
                {
                    new InfoEntry("✦ Khám phá ký tự Unicode (Unicode Explorer)",
                     "Duyệt toàn diện kho chữ Hán & Chữ Nôm trên cả ba mặt phẳng BMP, SIP (Plane 2) và TIP (Plane 3) với thông tin bộ thủ, số nét và phân loại trực quan."),

                    new InfoEntry("✦ Tra cứu âm đọc đa ngữ & Từ điển",
                     "Tra cứu đối chiếu phiên âm Hán Việt, Chữ Nôm, Pinyin, Tiếng Nhật, Hàn Quốc kết hợp định nghĩa chi tiết từ các nguồn từ điển trực tuyến uy tín."),

                    new InfoEntry("✦ Xưởng ghép chữ IDS (Typography Studio)",
                     "Ghép và tùy biến cấu trúc chữ theo 12 cấu trúc toán tử IDC, tích hợp thuật toán quang học tự động tinh chỉnh bố cục chuẩn ấn loát Đông Á."),

                    new InfoEntry("✦ Quản lý bộ sưu tập & Xuất bản vector",
                     "Lưu trữ chữ tự tạo vào sổ bộ ký tự riêng (PUA), thu thập ký tự vào giỏ và xuất đồ họa vector SVG hoặc ảnh sắc nét phục vụ in ấn, nghiên cứu."),

                    new InfoEntry("✦ Trải nghiệm tối ưu & Giao diện song ngữ",
                     "Giao diện hiện đại, phản hồi tức thời, hỗ trợ đầy đủ thao tác phím tắt tiện lợi cùng ngôn ngữ Tiếng Việt và English.")
                }
                : new InfoEntry[]
                {
                    new InfoEntry("✦ Comprehensive Unicode Explorer",
                     "Browse extensive CJK and Nom ideographs across BMP, SIP (Plane 2), and TIP (Plane 3) with stroke counts, radical indexing, and block navigation."),

                    new InfoEntry("✦ Multilingual Phonetic & Dictionary Search",
                     "Cross-reference Sino-Vietnamese, Nom, Pinyin, Japanese, and Korean readings alongside rich online dictionary definitions."),

                    new InfoEntry("✦ IDS Composition & Typography Studio",
                     "Visually assemble and refine composite ideographs with 12 IDC operators and an automated optical layout beautifier."),

                    new InfoEntry("✦ Export & Collection Management",
                     "Organize custom glyphs in a private character registry (PUA), manage character baskets, and export scalable SVG vectors for publishing."),

                    new InfoEntry("✦ Modern & Bilingual Experience",
                     "Smooth native desktop performance, comprehensive keyboard shortcuts, and full Vietnamese & English interface support.")
                };

            for (int i = features.Length - 1; i >= 0; i--)
            {
                AddInfoItem(pnlFeaturesList, features[i].Title, features[i].Desc, Color.FromArgb(15, 23, 42));
            }

            tabFeatures.Controls.Add(pnlFeaturesList);
            tabControl.TabPages.Add(tabFeatures);

            // Tab 2: Technical Specifications
            var tabSpecs = new TabPage(isVi ? "Thông tin kỹ thuật" : "Technical Specs")
            {
                BackColor = Color.White,
                Padding = new Padding(12)
            };

            var pnlSpecsList = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(6, 6, 14, 8)
            };

            int recs = unihanTotalRecords > 0 ? unihanTotalRecords : 67917;
            int hvdicRecs = hvdicTotalRecords > 0 ? hvdicTotalRecords : 15202;

            var specs = isVi
                ? new InfoEntry[]
                {
                    new InfoEntry("✦ Kiến trúc nền tảng",
                     "Ứng dụng máy tính Win32 nguyên bản (.NET Framework), chạy trực tiếp độc lập, tối ưu tốc độ và không yêu cầu môi trường phụ trợ."),

                    new InfoEntry("✦ Cơ sở dữ liệu Unihan",
                     string.Format("Tích hợp sẵn bộ cơ sở dữ liệu Unihan toàn diện với {0:N0} mục từ, cho phép tra cứu ngữ nghĩa và âm đọc hoàn toàn ngoại tuyến.", recs)),

                    new InfoEntry("✦ Từ điển Hán-Việt Thi Viện (Nội bộ)",
                     string.Format("Tích hợp trực tiếp cơ sở dữ liệu HVDic.wrd với {0:N0} mục từ, hỗ trợ tra cứu âm đọc Hán Việt, Pinyin, dị thể Phồn/Giản và định nghĩa hoàn toàn ngoại tuyến.", hvdicRecs)),

                    new InfoEntry("✦ Trình phân tích phông chữ OpenType",
                     "Bộ giải mã nhị phân OpenType TrueType/CFF hỗ trợ đọc trực tiếp bảng cmap Format 4 và Format 12 (Plane 2 Chữ Nôm)."),

                    new InfoEntry("✦ Đồ họa & Xuất bản vector",
                     "Engine dựng đường cong vector Bézier phân giải cao, hỗ trợ xuất định dạng vector SVG chuẩn W3C và ảnh bitmap đa kích cỡ."),

                    new InfoEntry("✦ Kết nối dữ liệu từ điển",
                     "Tích hợp kết nối trực tiếp cơ sở dữ liệu từ điển trực tuyến Thi Viện và Wiktionary để cập nhật thông tin ngữ học phong phú.")
                }
                : new InfoEntry[]
                {
                    new InfoEntry("✦ Desktop Architecture",
                     "Native Win32 desktop application (.NET Framework) with instant startup, high performance, and zero external framework overhead."),

                    new InfoEntry("✦ Unihan Character Database",
                     string.Format("Embedded comprehensive Unihan database with {0:N0} indexed entries for complete offline linguistic lookups.", recs)),

                    new InfoEntry("✦ HVDic Thi Vien Dictionary (Internal)",
                     string.Format("Embedded native HVDic.wrd database with {0:N0} entries for instant offline Sino-Vietnamese, Pinyin, script variant, and definition lookups.", hvdicRecs)),

                    new InfoEntry("✦ OpenType Font Parsing Engine",
                     "Binary OpenType TrueType/CFF parser with native support for cmap Format 4 and Format 12 (Plane 2 Nom characters)."),

                    new InfoEntry("✦ Vector Rendering & Export",
                     "High-precision Bézier vector engine supporting W3C standard SVG vector exports and multi-scale raster bitmaps."),

                    new InfoEntry("✦ Online Dictionary Lookups",
                     "Integrated direct lookups with online Thi Vien and Wiktionary databases for enriched linguistic definitions.")
                };

            for (int i = specs.Length - 1; i >= 0; i--)
            {
                AddInfoItem(pnlSpecsList, specs[i].Title, specs[i].Desc, Color.FromArgb(51, 65, 85));
            }

            tabSpecs.Controls.Add(pnlSpecsList);
            tabControl.TabPages.Add(tabSpecs);

            // Tab 3: Authors & Sources
            var tabCredits = new TabPage(isVi ? "Tác giả & Nguồn" : "Authors & Sources")
            {
                BackColor = Color.White,
                Padding = new Padding(12)
            };

            var pnlCreditsList = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(6, 6, 14, 8)
            };

            // Added in reverse order because DockStyle.Top stacks upward:
            // 3. Data Sources & Font Attribution Card (bottom)
            AddDataSourcesCard(pnlCreditsList, isVi);

            // 2. Open Source & Repositories Card (middle)
            AddInfoItem(
                pnlCreditsList,
                isVi ? "✦ Mã nguồn mở & Kho lưu trữ (Repositories)" : "✦ Open Source & Repositories",
                isVi
                    ? "Mã nguồn ứng dụng được quản lý công khai trên GitHub. Mọi phản hồi, đóng góp và báo cáo lỗi luôn được chào đón."
                    : "Application source code is publicly maintained on GitHub. Feedback, contributions, and bug reports are warmly welcomed.",
                Color.FromArgb(15, 23, 42)
            );

            // 1. Author & Developer Card (top)
            AddCreditCard(
                pnlCreditsList,
                isVi ? "✦ Tác giả & Nhà phát triển: Nguyễn Tuấn Vũ" : "✦ Author & Developer: Nguyễn Tuấn Vũ",
                isVi
                    ? "Tác giả sáng lập và phát triển dự án CJK Playground. Thiết kế kiến trúc phần mềm, xây dựng cơ chế tra cứu Unicode, bộ giải mã phông chữ OpenType nhị phân và thuật toán bố cục quang học cho Xưởng ghép chữ IDS."
                    : "Author and developer of CJK Playground. Designed the system architecture, built the Unicode explorer, binary OpenType font parser, and optical layout algorithm for the IDS Typography Studio.",
                "https://github.com/khanhvyNT",
                "https://www.facebook.com/oerlikon01",
                isVi
            );

            tabCredits.Controls.Add(pnlCreditsList);
            tabControl.TabPages.Add(tabCredits);

            // Dynamic wrap handler
            Action updateAllWraps = () =>
            {
                UpdateWrap(pnlFeaturesList);
                UpdateWrap(pnlSpecsList);
                UpdateWrap(pnlCreditsList);
            };

            pnlFeaturesList.ClientSizeChanged += (s, e) => UpdateWrap(pnlFeaturesList);
            pnlSpecsList.ClientSizeChanged += (s, e) => UpdateWrap(pnlSpecsList);
            pnlCreditsList.ClientSizeChanged += (s, e) => UpdateWrap(pnlCreditsList);
            Shown += (s, e) => updateAllWraps();
            tabControl.SelectedIndexChanged += (s, e) => updateAllWraps();

            // Initial wrap pass
            updateAllWraps();

            // Assemble Form
            Controls.Add(tabControl);
            Controls.Add(pnlHeader);
            Controls.Add(pnlBottom);
        }

        private static void UpdateWrap(Panel parent)
        {
            if (parent == null) return;
            int availWidth = parent.ClientSize.Width;
            if (availWidth < 100) availWidth = 580;

            int maxContentWidth = Math.Max(260, availWidth - 28);

            parent.SuspendLayout();
            foreach (Control c in parent.Controls)
            {
                var pnl = c as Panel;
                if (pnl != null)
                {
                    foreach (Control inner in pnl.Controls)
                    {
                        var lbl = inner as Label;
                        if (lbl != null)
                        {
                            if ("desc".Equals(lbl.Tag))
                            {
                                lbl.MaximumSize = new Size(Math.Max(200, maxContentWidth - 24), 0);
                            }
                            else if ("title".Equals(lbl.Tag))
                            {
                                lbl.MaximumSize = new Size(maxContentWidth, 0);
                            }
                        }
                    }
                }
            }
            parent.ResumeLayout(true);
        }

        private static void AddInfoItem(Panel parent, string title, string description, Color titleColor)
        {
            var pnl = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(0, 4, 0, 8)
            };

            var lblDesc = new Label
            {
                Text = description,
                Dock = DockStyle.Top,
                AutoSize = true,
                UseMnemonic = false,
                Tag = "desc",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105),
                Padding = new Padding(22, 2, 0, 0),
                MaximumSize = new Size(540, 0)
            };

            var lblTitle = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                UseMnemonic = false,
                Tag = "title",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = titleColor,
                MaximumSize = new Size(560, 0)
            };

            pnl.Controls.Add(lblDesc);
            pnl.Controls.Add(lblTitle);
            parent.Controls.Add(pnl);
        }

        private static void AddCreditCard(Panel parent, string title, string description, string githubUrl, string facebookUrl, bool isVi)
        {
            var pnl = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(12, 10, 12, 12),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var border = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(226, 232, 240)
            };
            pnl.Controls.Add(border);

            var pnlLinks = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0),
                Padding = new Padding(0, 6, 0, 0)
            };

            if (!string.IsNullOrEmpty(facebookUrl))
            {
                var lnkFb = new LinkLabel
                {
                    Text = "✦ Facebook: " + facebookUrl,
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 9.25F, FontStyle.Bold),
                    LinkColor = Color.FromArgb(24, 119, 242),
                    ActiveLinkColor = Color.FromArgb(13, 85, 184),
                    VisitedLinkColor = Color.FromArgb(24, 119, 242),
                    Cursor = Cursors.Hand,
                    Padding = new Padding(22, 3, 0, 2)
                };
                lnkFb.LinkClicked += (s, e) => OpenUrl(facebookUrl);
                pnlLinks.Controls.Add(lnkFb);
            }

            if (!string.IsNullOrEmpty(githubUrl))
            {
                var lnkGh = new LinkLabel
                {
                    Text = "✦ GitHub: " + githubUrl,
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 9.25F, FontStyle.Bold),
                    LinkColor = Color.FromArgb(37, 99, 235),
                    ActiveLinkColor = Color.FromArgb(29, 78, 216),
                    VisitedLinkColor = Color.FromArgb(37, 99, 235),
                    Cursor = Cursors.Hand,
                    Padding = new Padding(22, 3, 0, 2)
                };
                lnkGh.LinkClicked += (s, e) => OpenUrl(githubUrl);
                pnlLinks.Controls.Add(lnkGh);
            }

            var lblDesc = new Label
            {
                Text = description,
                Dock = DockStyle.Top,
                AutoSize = true,
                UseMnemonic = false,
                Tag = "desc",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105),
                Padding = new Padding(0, 4, 0, 0),
                MaximumSize = new Size(540, 0)
            };

            var lblTitle = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                UseMnemonic = false,
                Tag = "title",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                MaximumSize = new Size(560, 0)
            };

            pnl.Controls.Add(pnlLinks);
            pnl.Controls.Add(lblDesc);
            pnl.Controls.Add(lblTitle);
            parent.Controls.Add(pnl);
        }

        private static void AddDataSourcesCard(Panel parent, bool isVi)
        {
            var pnl = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(12, 10, 12, 12),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var border = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(226, 232, 240)
            };
            pnl.Controls.Add(border);

            var pnlLinks = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0),
                Padding = new Padding(0, 4, 0, 0)
            };

            // Reverse order for DockStyle.Top inside pnlLinks so order from top to bottom is:
            // 1. Thi Vien
            // 2. Nom Na Tong
            // 3. Unicode Consortium
            // 4. Wiktionary

            var lnkWik = new LinkLabel
            {
                Text = isVi
                    ? "✦ Wiktionary & Cộng đồng Hán-Nôm (vi.wiktionary.org)"
                    : "✦ Wiktionary & Han-Nom Linguistics Community (vi.wiktionary.org)",
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                LinkColor = Color.FromArgb(37, 99, 235),
                ActiveLinkColor = Color.FromArgb(29, 78, 216),
                VisitedLinkColor = Color.FromArgb(37, 99, 235),
                Cursor = Cursors.Hand,
                Padding = new Padding(22, 3, 0, 3)
            };
            lnkWik.LinkClicked += (s, e) => OpenUrl("https://vi.wiktionary.org/");

            var lnkUni = new LinkLabel
            {
                Text = isVi
                    ? "✦ Unicode Consortium: Cơ sở dữ liệu Unihan (home.unicode.org)"
                    : "✦ Unicode Consortium: Unihan Database (home.unicode.org)",
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                LinkColor = Color.FromArgb(37, 99, 235),
                ActiveLinkColor = Color.FromArgb(29, 78, 216),
                VisitedLinkColor = Color.FromArgb(37, 99, 235),
                Cursor = Cursors.Hand,
                Padding = new Padding(22, 3, 0, 3)
            };
            lnkUni.LinkClicked += (s, e) => OpenUrl("https://home.unicode.org/");

            var lnkNom = new LinkLabel
            {
                Text = isVi
                    ? "✦ Phông chữ Nôm Na Tống (Nom Na Tong): https://nomfoundation.org/\n  (Hội Bảo tồn di sản chữ Nôm Việt Nam)"
                    : "✦ Nom Na Tong Font: https://nomfoundation.org/\n  (Vietnamese Nôm Preservation Foundation)",
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                LinkColor = Color.FromArgb(13, 148, 136),
                ActiveLinkColor = Color.FromArgb(15, 118, 110),
                VisitedLinkColor = Color.FromArgb(13, 148, 136),
                Cursor = Cursors.Hand,
                Padding = new Padding(22, 3, 0, 3)
            };
            lnkNom.LinkClicked += (s, e) => OpenUrl("https://nomfoundation.org/");

            var lnkThiVien = new LinkLabel
            {
                Text = isVi
                    ? "✦ Từ điển Hán Nôm: https://hvdic.thivien.net/\n  (Cơ sở dữ liệu HVDic.wrd tích hợp ngoại tuyến)"
                    : "✦ Han-Nom Dictionary: https://hvdic.thivien.net/\n  (Embedded offline HVDic.wrd database)",
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                LinkColor = Color.FromArgb(217, 119, 6),
                ActiveLinkColor = Color.FromArgb(180, 83, 9),
                VisitedLinkColor = Color.FromArgb(217, 119, 6),
                Cursor = Cursors.Hand,
                Padding = new Padding(22, 3, 0, 3)
            };
            lnkThiVien.LinkClicked += (s, e) => OpenUrl("https://hvdic.thivien.net/");

            pnlLinks.Controls.Add(lnkWik);
            pnlLinks.Controls.Add(lnkUni);
            pnlLinks.Controls.Add(lnkNom);
            pnlLinks.Controls.Add(lnkThiVien);

            var lblDesc = new Label
            {
                Text = isVi
                    ? "Trân trọng tri ân và ghi nhận nguồn dữ liệu học thuật, bộ từ điển cùng phông chữ tiêu chuẩn:"
                    : "Gratefully acknowledges essential academic datasets, dictionaries, and standard typography assets:",
                Dock = DockStyle.Top,
                AutoSize = true,
                UseMnemonic = false,
                Tag = "desc",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105),
                Padding = new Padding(0, 4, 0, 2),
                MaximumSize = new Size(540, 0)
            };

            var lblTitle = new Label
            {
                Text = isVi ? "✦ Nguồn dữ liệu" : "✦ Data Sources",
                Dock = DockStyle.Top,
                AutoSize = true,
                UseMnemonic = false,
                Tag = "title",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                MaximumSize = new Size(560, 0)
            };

            pnl.Controls.Add(pnlLinks);
            pnl.Controls.Add(lblDesc);
            pnl.Controls.Add(lblTitle);
            parent.Controls.Add(pnl);
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                System.Diagnostics.Process.Start(url);
            }
            catch
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }
}
