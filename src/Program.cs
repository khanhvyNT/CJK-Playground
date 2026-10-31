using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CJKPlayground.Forms;
using CJKPlayground.Services;
using CJKPlayground.Tests;

namespace CJKPlayground
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0].ToLowerInvariant() == "--benchmark")
            {
                var report = IdsQualityHarness.RunBenchmark(FontFamily.GenericSansSerif, true, "contact_sheet.png");
                string summary = report.FormatSummaryTable();
                Console.WriteLine(summary);
                File.WriteAllText("benchmark_report.txt", summary, System.Text.Encoding.UTF8);
                return;
            }

            if (args != null && args.Length > 0 && args[0].ToLowerInvariant() == "--test")
            {
                string rep1;
                bool ok1 = LinguisticTests.RunAllTests(out rep1);
                string rep2;
                bool ok2 = OpenTypeTests.RunAllTests(out rep2);
                string rep3;
                bool ok3 = HvDicTests.RunAllTests(out rep3);
                string rep4;
                bool ok4 = AutoBeautifyTests.RunAllTests(out rep4);
                string rep5;
                bool ok5 = FontInjectionTests.RunAllTests(out rep5);
                string rep6;
                bool ok6 = IllustratorToolsTests.RunAllTests(out rep6);

                string fullReport = rep1 + "\n\n" + rep2 + "\n\n" + rep3 + "\n\n" + rep4 + "\n\n" + rep5 + "\n\n" + rep6;
                File.WriteAllText("test_report.txt", fullReport, System.Text.Encoding.UTF8);
                Console.WriteLine(fullReport);
                Environment.Exit((ok1 && ok2 && ok3 && ok4 && ok5 && ok6) ? 0 : 1);
                return;
            }

            SplashScreenForm splash = null;
            bool isEn = false;
            try
            {
                var s = SettingsManager.Load();
                isEn = s != null && "en".Equals(s.Language, StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    splash = new SplashScreenForm();
                    splash.Show();
                    splash.UpdateProgress(5, isEn ? "Starting CJK Playground..." : "Đang khởi động CJK Playground...");
                }
                catch { }

                MainForm mainForm = null;
                try
                {
                    mainForm = new MainForm((percent, msg) =>
                    {
                        if (splash != null && !splash.IsDisposed)
                        {
                            splash.UpdateProgress(percent, msg);
                        }
                    });
                }
                catch
                {
                    if (splash != null && !splash.IsDisposed)
                    {
                        try { splash.Close(); splash.Dispose(); } catch { }
                    }
                    throw;
                }

                if (splash != null && !splash.IsDisposed)
                {
                    splash.UpdateProgress(100, isEn ? "Startup complete!" : "Hoàn tất khởi động!");
                    System.Threading.Thread.Sleep(80);
                    Application.DoEvents();
                }

                if (mainForm != null)
                {
                    mainForm.Shown += (s, e) =>
                    {
                        if (splash != null && !splash.IsDisposed)
                        {
                            try
                            {
                                splash.Close();
                                splash.Dispose();
                                splash = null;
                            }
                            catch { }
                        }
                        try
                        {
                            mainForm.Activate();
                            mainForm.BringToFront();
                        }
                        catch { }
                    };

                    Application.Run(mainForm);
                }
            }
            catch (Exception ex)
            {
                if (splash != null && !splash.IsDisposed)
                {
                    try { splash.Close(); splash.Dispose(); } catch { }
                }

                MessageBox.Show(
                    isEn ? ("An error occurred while starting CJK Playground:\n\n" + ex.ToString())
                         : ("Đã xảy ra lỗi khi khởi động CJK Playground:\n\n" + ex.ToString()),
                    isEn ? "CJK Playground Startup Error" : "Lỗi Khởi Động CJK Playground",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}
