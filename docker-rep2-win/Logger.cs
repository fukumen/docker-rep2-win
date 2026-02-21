using System;
using System.IO;
using System.Text;
using System.Windows;

namespace docker_rep2_win
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private const long MaxLogSize = 100 * 1024; // 100KB

        public static void Log(Exception ex, string context = "")
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {context}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                sb.AppendLine($"InnerException: {ex.InnerException.Message}");
                sb.AppendLine($"InnerStackTrace: {ex.InnerException.StackTrace}");
            }
            sb.AppendLine(new string('-', 60));

            WriteToFile(sb.ToString());
        }

        public static void Log(string message)
        {
            WriteToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INFO: {message}{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}");
        }

        public static void Debug(string message)
        {
            var app = (App)Application.Current;
            if (app.Settings.User.DebugLogEnabled)
            {
                WriteToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DEBUG: {message}{Environment.NewLine}");
            }
        }

        private static void WriteToFile(string text)
        {
            try
            {
                var app = (App)Application.Current;
                string appDataPath = app.Settings.WindowsDataPath;
                if (string.IsNullOrEmpty(appDataPath)) return;

                if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);

                string logFile = Path.Combine(appDataPath, "win-error.txt");
                string backupFile = logFile + ".1";

                lock (_lock)
                {
                    // ローテーション処理
                    if (File.Exists(logFile) && new FileInfo(logFile).Length > MaxLogSize)
                    {
                        try {
                            if (File.Exists(backupFile)) File.Delete(backupFile);
                            File.Move(logFile, backupFile);
                        } catch { /* リネーム失敗時は諦めて追記 */ }
                    }

                    File.AppendAllText(logFile, text, Encoding.UTF8);
                }
            }
            catch
            {
                // ログ出力失敗時はデバッグ出力のみ
                System.Diagnostics.Debug.WriteLine("Failed to write to log file.");
            }
        }
    }
}
