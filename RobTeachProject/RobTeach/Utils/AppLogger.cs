using System;
using System.IO;
using System.Text;

namespace RobTeach.Utils
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Debug
    }

    public static class AppLogger
    {
        private static readonly object _lock = new object();
        private static string _logDirectory;

        static AppLogger()
        {
            try
            {
                _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
            }
            catch (Exception ex)
            {
                // Fallback or error handling if log directory creation fails.
                // For now, we'll let it try to write to base directory if this fails,
                // or it will throw when trying to write the file.
                // A more robust solution might try a secondary location or disable logging.
                System.Diagnostics.Debug.WriteLine($"[AppLogger] Error creating log directory: {ex.Message}");
                _logDirectory = AppDomain.CurrentDomain.BaseDirectory; // Fallback
            }
        }

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            try
            {
                lock (_lock)
                {
                    string logFileName = $"AppLog_{DateTime.Now:yyyy-MM-dd}.txt";
                    string filePath = Path.Combine(_logDirectory, logFileName);

                    StringBuilder logEntry = new StringBuilder();
                    logEntry.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    logEntry.Append($" [{level.ToString().ToUpper()}] ");
                    logEntry.Append(message);

                    File.AppendAllText(filePath, logEntry.ToString() + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // Prevent logger from crashing the application.
                System.Diagnostics.Debug.WriteLine($"[AppLogger] Failed to write log: {ex.Message}");
            }
        }

        // Overload for logging exceptions
        public static void Log(string message, Exception ex, LogLevel level = LogLevel.Error)
        {
            try
            {
                lock (_lock)
                {
                    string logFileName = $"AppLog_{DateTime.Now:yyyy-MM-dd}.txt";
                    string filePath = Path.Combine(_logDirectory, logFileName);

                    StringBuilder logEntry = new StringBuilder();
                    logEntry.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    logEntry.Append($" [{level.ToString().ToUpper()}] ");
                    logEntry.Append(message);
                    logEntry.Append(Environment.NewLine);
                    logEntry.Append("Exception: ").Append(ex.ToString()); // Includes stack trace

                    File.AppendAllText(filePath, logEntry.ToString() + Environment.NewLine);
                }
            }
            catch (Exception loggingEx)
            {
                System.Diagnostics.Debug.WriteLine($"[AppLogger] Failed to write exception log: {loggingEx.Message}");
            }
        }
    }
}
