using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Playnite.SDK;

namespace PlayniteDisplayManager.Logging
{
    /// <summary>
    /// Dual logger: Playnite ILogger + session-oriented support file under plugin user data.
    /// Always on for Info/Warn/Error; Debug/Trace and success detail dumps only when verbose.
    /// </summary>
    public sealed class PluginFileLogger : ILogger
    {
        public const string FileName = "display-manager.log";
        private const long RotateBytes = 8L * 1024 * 1024;
        private const int MaxDetailChars = 48000;

        private readonly ILogger inner;
        private readonly object sync = new object();
        private readonly Func<bool> isVerboseEnabled;
        private readonly string logDirectory;
        private readonly string logPath;

        private Guid? sessionGameId;
        private string sessionGameName;
        private Stopwatch sessionWatch;

        public PluginFileLogger(string pluginUserDataPath, ILogger playniteLogger, Func<bool> verboseEnabled)
        {
            if (string.IsNullOrWhiteSpace(pluginUserDataPath))
            {
                throw new ArgumentException("Plugin user data path is required.", nameof(pluginUserDataPath));
            }

            inner = playniteLogger ?? LogManager.GetLogger();
            isVerboseEnabled = verboseEnabled ?? (() => false);
            logDirectory = Path.Combine(pluginUserDataPath, "logs");
            logPath = Path.Combine(logDirectory, FileName);
            Directory.CreateDirectory(logDirectory);
            Current = this;
            WriteHeader();
        }

        /// <summary>Optional sink for code without an injected ILogger (HDR/topology helpers).</summary>
        public static PluginFileLogger Current { get; private set; }

        public string LogDirectory => logDirectory;

        public string LogFilePath => logPath;

        public bool IsVerboseEnabled => isVerboseEnabled();

        public bool HasOpenSession => sessionGameId.HasValue;

        public static string ShortId(Guid id)
        {
            return id.ToString("N").Substring(0, 8);
        }

        /// <summary>Rewrite the log file with only the header (Vaciar registro).</summary>
        public void Clear()
        {
            lock (sync)
            {
                Directory.CreateDirectory(logDirectory);
                File.WriteAllText(logPath, BuildHeader(), Encoding.UTF8);
            }
        }

        /// <summary>Create the log file with header if it does not exist yet.</summary>
        public void EnsureFileExists()
        {
            lock (sync)
            {
                Directory.CreateDirectory(logDirectory);
                if (!File.Exists(logPath))
                {
                    File.WriteAllText(logPath, BuildHeader(), Encoding.UTF8);
                }
            }
        }

        public void SessionBegin(Guid gameId, string gameName)
        {
            sessionGameId = gameId;
            sessionGameName = gameName ?? string.Empty;
            sessionWatch = Stopwatch.StartNew();
            WriteBanner(
                "SESSION begin | " + sessionGameName + " | id=" + ShortId(gameId),
                null);
        }

        public void SessionEnd(bool ok, string reason = null)
        {
            var title = sessionGameName ?? string.Empty;
            var id = sessionGameId.HasValue ? ShortId(sessionGameId.Value) : "--------";
            var elapsed = sessionWatch == null
                ? string.Empty
                : FormatElapsed(sessionWatch.Elapsed);
            var status = ok ? "ok" : "failed";
            var line = "SESSION end | " + title + " | id=" + id +
                       " | " + status +
                       (string.IsNullOrWhiteSpace(reason) ? string.Empty : " | reason=" + reason.Trim()) +
                       (string.IsNullOrEmpty(elapsed) ? string.Empty : " | " + elapsed);
            WriteBanner(line, null);
            sessionGameId = null;
            sessionGameName = null;
            sessionWatch = null;
        }

        public void Event(string level, string topic, string details, string detailDump = null)
        {
            WriteEvent(level ?? "INFO", topic, details, detailDump);
        }

        public void InfoTopic(string topic, string details, string detailDump = null)
        {
            // Detail dumps on INFO only when verbose (success path stays scannable).
            var dump = IsVerboseEnabled ? detailDump : null;
            WriteEvent("INFO", topic, details, dump);
            inner.Info(FormatPlayniteMessage(topic, details));
        }

        public void WarnTopic(string topic, string details, string detailDump = null)
        {
            WriteEvent("WARN", topic, details, detailDump);
            inner.Warn(FormatPlayniteMessage(topic, details));
        }

        public void ErrorTopic(string topic, string details, Exception exception = null, string detailDump = null)
        {
            var dump = CombineDump(detailDump, exception == null ? null : exception.ToString());
            WriteEvent("ERROR", topic, details, dump);
            if (exception != null)
            {
                inner.Error(exception, FormatPlayniteMessage(topic, details));
            }
            else
            {
                inner.Error(FormatPlayniteMessage(topic, details));
            }
        }

        public void Info(string message)
        {
            WriteEvent("INFO", "plugin", message, null);
            inner.Info(message);
        }

        public void Info(Exception exception, string message)
        {
            WriteEvent("INFO", "plugin", message, exception == null ? null : exception.ToString());
            inner.Info(exception, message);
        }

        public void Warn(string message)
        {
            WriteEvent("WARN", "plugin", message, null);
            inner.Warn(message);
        }

        public void Warn(Exception exception, string message)
        {
            WriteEvent("WARN", "plugin", message, exception == null ? null : exception.ToString());
            inner.Warn(exception, message);
        }

        public void Error(string message)
        {
            WriteEvent("ERROR", "plugin", message, null);
            inner.Error(message);
        }

        public void Error(Exception exception, string message)
        {
            WriteEvent("ERROR", "plugin", message, exception == null ? null : exception.ToString());
            inner.Error(exception, message);
        }

        public void Debug(string message)
        {
            if (IsVerboseEnabled)
            {
                WriteEvent("DEBUG", "plugin", message, null);
            }

            inner.Debug(message);
        }

        public void Debug(Exception exception, string message)
        {
            if (IsVerboseEnabled)
            {
                WriteEvent("DEBUG", "plugin", message, exception == null ? null : exception.ToString());
            }

            inner.Debug(exception, message);
        }

        public void Trace(string message)
        {
            if (IsVerboseEnabled)
            {
                WriteEvent("TRACE", "plugin", message, null);
            }

            inner.Trace(message);
        }

        public void Trace(Exception exception, string message)
        {
            if (IsVerboseEnabled)
            {
                WriteEvent("TRACE", "plugin", message, exception == null ? null : exception.ToString());
            }

            inner.Trace(exception, message);
        }

        public static string Truncate(string text, int maxChars = MaxDetailChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, maxChars) +
                   Environment.NewLine + "…(truncated, " +
                   text.Length.ToString(CultureInfo.InvariantCulture) + " chars total)";
        }

        private string BuildHeader()
        {
            return "==== Display Manager support log ====" + Environment.NewLine +
                   "File: " + logPath + Environment.NewLine +
                   "Levels: INFO WARN ERROR  |  Summary on success; detail dumps on warn/error (or verbose)." + Environment.NewLine +
                   "Delete this file anytime; it is recreated on the next plugin load / session." + Environment.NewLine +
                   Environment.NewLine;
        }

        private void WriteHeader()
        {
            try
            {
                AppendUnlocked(BuildHeader());
            }
            catch
            {
            }
        }

        private void WriteBanner(string title, string details)
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var block = new StringBuilder();
            block.Append("===== ").Append(stamp).Append(" | ").Append(title ?? "log").Append(" =====");
            block.Append(Environment.NewLine);
            if (!string.IsNullOrEmpty(details))
            {
                block.Append(details.TrimEnd());
                block.Append(Environment.NewLine);
            }

            block.Append(Environment.NewLine);
            AppendUnlocked(block.ToString());
        }

        private void WriteEvent(string level, string topic, string details, string detailDump)
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var block = new StringBuilder();
            block.Append(PadLevel(level))
                .Append("  ")
                .Append(stamp)
                .Append(" | ")
                .Append(string.IsNullOrWhiteSpace(topic) ? "log" : topic.Trim());

            if (sessionGameId.HasValue)
            {
                block.Append(" | ")
                    .Append(sessionGameName ?? string.Empty)
                    .Append(" | id=")
                    .Append(ShortId(sessionGameId.Value));
            }

            block.Append(Environment.NewLine);
            if (!string.IsNullOrEmpty(details))
            {
                block.Append(details.TrimEnd());
                block.Append(Environment.NewLine);
            }

            if (!string.IsNullOrEmpty(detailDump))
            {
                block.Append("--- detail ---");
                block.Append(Environment.NewLine);
                block.Append(Truncate(detailDump).TrimEnd());
                block.Append(Environment.NewLine);
                block.Append("--- end detail ---");
                block.Append(Environment.NewLine);
            }

            block.Append(Environment.NewLine);
            AppendUnlocked(block.ToString());
        }

        private void AppendUnlocked(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(logDirectory);
                    RotateIfNeededUnlocked();
                    File.AppendAllText(logPath, text, Encoding.UTF8);
                }
            }
            catch
            {
                // Support logging is best-effort.
            }
        }

        private void RotateIfNeededUnlocked()
        {
            if (!File.Exists(logPath))
            {
                return;
            }

            var info = new FileInfo(logPath);
            if (info.Length < RotateBytes)
            {
                return;
            }

            var backup = logPath + ".old";
            try
            {
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }

                File.Move(logPath, backup);
            }
            catch
            {
                // Keep appending if rotation fails.
            }
        }

        private static string PadLevel(string level)
        {
            var value = (level ?? "INFO").Trim().ToUpperInvariant();
            if (value.Length >= 5)
            {
                return value.Substring(0, 5);
            }

            return value.PadRight(5);
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
            {
                return elapsed.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture);
            }

            return elapsed.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
        }

        private static string FormatPlayniteMessage(string topic, string details)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return topic ?? string.Empty;
            }

            return (topic ?? "log") + ": " + details;
        }

        private static string CombineDump(string detailDump, string exceptionText)
        {
            if (string.IsNullOrEmpty(detailDump))
            {
                return exceptionText;
            }

            if (string.IsNullOrEmpty(exceptionText))
            {
                return detailDump;
            }

            return detailDump.TrimEnd() + Environment.NewLine + Environment.NewLine +
                   "exception:" + Environment.NewLine + exceptionText;
        }
    }
}
