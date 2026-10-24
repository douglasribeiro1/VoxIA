using System;
using System.IO;

namespace VoxIA.Services
{
    public static class Logger
    {
        /// <summary>Logs mais antigos que isso são apagados.</summary>
        private const int LogRetentionDays = 7;

        private static readonly object _lock = new object();
        private static DateTime _lastPurgeDate = DateTime.MinValue;

        public static void LogInfo(string message)
        {
            WriteLog("INFO", message);
        }

        public static void LogWarning(string message)
        {
            WriteLog("WARN", message);
        }

        public static void LogError(string message, Exception ex)
        {
            string msg = message;
            if (ex != null)
            {
                msg += string.Format(" | Exception: {0} - {1}", ex.GetType().Name, ex.Message);
            }
            WriteLog("ERROR", msg);
        }

        public static void LogError(string message)
        {
            LogError(message, null);
        }

        private static void WriteLog(string level, string message)
        {
            try
            {
                string logFile = Path.Combine(AppPathService.LogsDirectory, string.Format("app_{0:yyyy-MM-dd}.log", DateTime.Now));
                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}", DateTime.Now, level, Sanitize(message));

                lock (_lock)
                {
                    if (_lastPurgeDate != DateTime.Today)
                    {
                        _lastPurgeDate = DateTime.Today;
                        DeleteLogs(DateTime.Today.AddDays(-LogRetentionDays));
                    }
                    File.AppendAllText(logFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // Silent fail for logging to avoid cascading crashes
            }
        }

        /// <summary>Apaga todos os arquivos de log (usado na migração de versões que gravavam o texto ditado).</summary>
        public static void DeleteAllLogs()
        {
            lock (_lock)
            {
                DeleteLogs(DateTime.MaxValue);
            }
        }

        private static void DeleteLogs(DateTime olderThan)
        {
            try
            {
                foreach (string file in Directory.GetFiles(AppPathService.LogsDirectory, "app_*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < olderThan) File.Delete(file);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return System.Text.RegularExpressions.Regex.Replace(text, @"gsk_[a-zA-Z0-9_-]+", "gsk_REDACTED");
        }
    }
}
