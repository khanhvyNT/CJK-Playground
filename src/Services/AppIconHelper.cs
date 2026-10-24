using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CJKPlayground.Services
{
    public static class AppIconHelper
    {
        private static Icon _cachedIcon;

        public static Icon GetAppIcon()
        {
            if (_cachedIcon != null) return _cachedIcon;

            // 1. Try extracting from executing assembly PE Win32 icon resources
            try
            {
                _cachedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (_cachedIcon != null) return _cachedIcon;
            }
            catch { }

            // 2. Try loading from embedded resource
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("app.ico"))
                {
                    if (stream != null)
                    {
                        _cachedIcon = new Icon(stream);
                        return _cachedIcon;
                    }
                }
            }
            catch { }

            // 3. Try loading from local file app.ico
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(path))
                {
                    _cachedIcon = new Icon(path);
                    return _cachedIcon;
                }
            }
            catch { }

            return null;
        }
    }
}
