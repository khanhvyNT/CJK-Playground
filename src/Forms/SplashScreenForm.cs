using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CJKPlayground.Services;

namespace CJKPlayground.Forms
{
    public class SplashScreenForm : Form
    {
        private Label _lblStatus;
        private Label _lblPercent;
        private Panel _progressTrackPanel;
        private Panel _progressFillPanel;
        private Timer _failsafeTimer;
        private int _currentPercent = 0;
        private string _currentMessage = "Đang khởi động...";

        public SplashScreenForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(520, 260);
            BackColor = Color.White;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;

            bool isEn = false;
            try
            {
                var s = SettingsManager.Load();
                isEn = s != null && "en".Equals(s.Language, StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            _currentMessage = isEn ? "Starting up..." : "Đang khởi động...";

            // 1. App Icon Box (Visual Badge)
            var pnlIconBadge = new Panel
            {
                Location = new Point(24, 28),
                Size = new Size(68, 68),
                BackColor = Color.FromArgb(238, 242, 255)
            };
            pnlIconBadge.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(199, 210, 254), 1.5F))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, pnlIconBadge.Width - 1, pnlIconBadge.Height - 1);
                }
            };

            // Safe CJK font fallback for badge character
            Font badgeFont = null;
            foreach (string fname in new[] { "Microsoft YaHei", "SimSun", "MS Gothic", "Malgun Gothic", "Segoe UI Symbol" })
            {
                try
                {
                    using (var test = new Font(fname, 12F))
                    {
                        if (test.Name.Equals(fname, StringComparison.OrdinalIgnoreCase))
                        {
                            badgeFont = new Font(fname, 30F, FontStyle.Regular);
                            break;
                        }
                    }
                }
                catch { }
            }
            if (badgeFont == null) badgeFont = new Font(FontFamily.GenericSansSerif, 30F, FontStyle.Bold);

            var lblBadgeChar = new Label
            {
                Text = "字",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = badgeFont,
                ForeColor = Color.FromArgb(37, 99, 235)
            };
            pnlIconBadge.Controls.Add(lblBadgeChar);

            // 2. Titles & Welcome Greeting
            var lblTitle = new Label
            {
                Text = "CJK Playground",
                Location = new Point(106, 26),
                AutoSize = true,
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };

            var lblWelcome = new Label
            {
                Text = isEn ? "Welcome, Valued Guest!" : "Kính chào quý khách!",
                Location = new Point(108, 68),
                AutoSize = true,
                Font = new Font("Segoe UI", 11F, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105)
            };

            // 3. Separator Line
            var pnlDivider = new Panel
            {
                Location = new Point(24, 118),
                Size = new Size(472, 1),
                BackColor = Color.FromArgb(241, 245, 249)
            };

            // 4. Status and Percentage Labels
            _lblStatus = new Label
            {
                Location = new Point(24, 140),
                Size = new Size(385, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(51, 65, 85),
                Text = _currentMessage
            };

            _lblPercent = new Label
            {
                Location = new Point(415, 140),
                Size = new Size(81, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                TextAlign = ContentAlignment.MiddleRight,
                Text = "0%"
            };

            // 5. Custom Smooth Progress Bar Track & Fill
            _progressTrackPanel = new Panel
            {
                Location = new Point(24, 168),
                Size = new Size(472, 8),
                BackColor = Color.FromArgb(241, 245, 249)
            };
            _progressTrackPanel.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _progressTrackPanel.Width - 1, _progressTrackPanel.Height - 1);
                }
            };

            _progressFillPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(0, 8),
                BackColor = Color.FromArgb(37, 99, 235) // Modern Royal Blue
            };
            _progressTrackPanel.Controls.Add(_progressFillPanel);

            // 6. Bottom Information Labels
            var lblFooterLeft = new Label
            {
                Text = isEn ? "Initializing system components..." : "Đang khởi tạo các thành phần hệ thống...",
                Location = new Point(24, 222),
                AutoSize = true,
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(148, 163, 184)
            };

            var lblFooterRight = new Label
            {
                Text = isEn ? "Version 1.0 (Release)" : "Phiên bản 1.0 (Release)",
                Location = new Point(350, 222),
                Size = new Size(146, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(148, 163, 184)
            };

            Controls.AddRange(new Control[]
            {
                pnlIconBadge, lblTitle, lblWelcome,
                pnlDivider, _lblStatus, _lblPercent,
                _progressTrackPanel, lblFooterLeft, lblFooterRight
            });

            // 7. Failsafe auto-close timer (15 seconds max)
            _failsafeTimer = new Timer { Interval = 15000 };
            _failsafeTimer.Tick += (s, e) =>
            {
                _failsafeTimer.Stop();
                try { Close(); Dispose(); } catch { }
            };
            _failsafeTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Outer border
            using (var borderPen = new Pen(Color.FromArgb(203, 213, 225), 1))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }

            // Top blue decorative accent bar
            using (var topBrush = new SolidBrush(Color.FromArgb(37, 99, 235)))
            {
                e.Graphics.FillRectangle(topBrush, 0, 0, Width, 4);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Capture = false;
                Message msg = Message.Create(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
                DefWndProc(ref msg);
            }
        }

        public void UpdateProgress(int percent, string message)
        {
            if (IsDisposed) return;

            try
            {
                if (!IsHandleCreated)
                {
                    CreateControl();
                }

                if (InvokeRequired)
                {
                    Invoke(new Action(() => UpdateProgress(percent, message)));
                    return;
                }

                _currentPercent = Math.Max(0, Math.Min(100, percent));
                if (!string.IsNullOrEmpty(message))
                {
                    _currentMessage = message;
                }

                _lblStatus.Text = _currentMessage;
                _lblPercent.Text = _currentPercent + "%";

                int targetWidth = (int)((_progressTrackPanel.Width * _currentPercent) / 100.0);
                _progressFillPanel.Width = Math.Max(0, Math.Min(_progressTrackPanel.Width, targetWidth));

                _lblStatus.Refresh();
                _lblPercent.Refresh();
                _progressFillPanel.Refresh();
                _progressTrackPanel.Refresh();

                Application.DoEvents();
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_failsafeTimer != null)
                {
                    _failsafeTimer.Stop();
                    _failsafeTimer.Dispose();
                    _failsafeTimer = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
