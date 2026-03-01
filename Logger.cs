using System;
using System.IO;
using Speakly.Config;

namespace Speakly.Services
{
    public static class Logger
    {
        private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "speakly_debug.log");
        private static readonly object _lock = new object();

        public static void Log(string message)
        {
            if (!ConfigManager.Config.EnableDebugLogs) return;

            try
            {
                lock (_lock)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    File.AppendAllText(LogFile, $"[{timestamp}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Ignore logging errors to prevent crashes
            }
        }

        public static void LogException(string context, Exception ex)
        {
            Log($"ERROR in {context}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
        }
    }
}
