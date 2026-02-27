using System;
using System.IO;
using System.Text;

namespace FlowRunner
{
    internal static class AppLog
    {
        public static readonly string RootDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FlowRunner");

        public static readonly string LogsDir = Path.Combine(RootDir, "logs");

        private static readonly object _lock = new();

        public static string CurrentLogPath =>
            Path.Combine(LogsDir, $"app_{DateTime.Now:yyyyMMdd}.log");

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        public static void Exception(string context, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine(context);
            sb.AppendLine(ex.ToString());
            Write("EX", sb.ToString());
        }

        private static void Write(string level, string message)
        {
            try
            {
                Directory.CreateDirectory(LogsDir);

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

                lock (_lock)
                {
                    File.AppendAllText(CurrentLogPath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // never crash because logging failed
            }
        }
    }
}