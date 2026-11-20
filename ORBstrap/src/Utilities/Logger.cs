using System;
using System.IO;
using System.Text;

namespace Crypt3d.Utilities
{
    public class Logger
    {
        private readonly object _lock = new();
        private readonly string _logPath;

        public Logger()
        {
            var dir = Path.Combine(AppInfo.DataDir, "Logs");

            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, $"{AppInfo.Name}_{DateTime.Now:yyyy-MM-dd}.log");
        }

        public void WriteLine(string ident, string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{ident}] {message}";
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch { /* ignore IO errors */ }
            }
#if DEBUG
            System.Diagnostics.Debug.WriteLine(line);
#endif
        }

        public void WriteException(string ident, Exception ex)
        {
            WriteLine(ident, $"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            if (ex.StackTrace != null)
                WriteLine(ident, ex.StackTrace);
        }
    }
}
