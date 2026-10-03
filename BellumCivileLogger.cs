using System;
using System.IO;
using System.Text;
using TaleWorlds.Library;

namespace BellumCivile
{
    public static class BellumCivileLogger
    {
        private static readonly object Sync = new object();
        private const long MaxLogBytes = 5L * 1024L * 1024L;
        private const int DaysToKeepLogs = 5;
        private const int LogBufferBytes = 64 * 1024;
        private const int FlushLineInterval = 32;
        private static readonly Encoding LogEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static bool _prunedOldLogsThisSession;
        private static StreamWriter _logWriter;
        private static string _activeLogDate = string.Empty;
        private static long _activeLogBytes;
        private static int _activeLogIndex = -1;
        private static int _linesSinceFlush;
        private static DateTime _lastFlushUtc = DateTime.UtcNow;

        static BellumCivileLogger()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, __) => CloseLogWriter();
        }

        private static string LogDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Mount and Blade II Bannerlord",
                "Configs",
                "ModLogs");

        private static string YearlyReportsPath =>
            Path.Combine(LogDirectory, "bellumcivile_yearlyreports.txt");

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            bool fileLogged = false;

            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(LogDirectory);
                    PruneOldLogsOnce();
                    WriteBufferedLine(line);
                    fileLogged = true;
                }
                catch
                {
                    CloseLogWriter();
                    // If file logging fails, at least keep the message visible to attached debuggers.
                }
            }

            if (!fileLogged)
                Debug.Print(line);
        }

        public static void LogYearlyReport(string category, string report)
        {
            if (string.IsNullOrWhiteSpace(report))
                return;

            string categoryText = string.IsNullOrWhiteSpace(category) ? "general" : category;
            string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{categoryText}]\r\n{report}\r\n";

            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(LogDirectory);
                    PruneOldLogsOnce();
                    File.AppendAllText(YearlyReportsPath, text + Environment.NewLine);
                }
                catch
                {
                    // Best-effort report logging only.
                }
            }
        }

        public static void Flush()
        {
            lock (Sync)
            {
                try
                {
                    FlushLogWriter();
                }
                catch
                {
                    CloseLogWriter();
                }
            }
        }

        private static void WriteBufferedLine(string line)
        {
            string date = DateTime.Now.ToString("yyyyMMdd");
            int lineBytes = LogEncoding.GetByteCount(line + Environment.NewLine);
            if (_logWriter == null || _activeLogDate != date)
                OpenWritableLog(date, 0);
            else if (_activeLogBytes > 0 && _activeLogBytes + lineBytes > MaxLogBytes)
                OpenWritableLog(date, _activeLogIndex + 1);

            _logWriter.WriteLine(line);
            _activeLogBytes += lineBytes;
            _linesSinceFlush++;

            DateTime now = DateTime.UtcNow;
            if (_linesSinceFlush >= FlushLineInterval || (now - _lastFlushUtc).TotalMilliseconds >= 500d)
                FlushLogWriter(now);
        }

        private static void OpenWritableLog(string date, int startingIndex)
        {
            CloseLogWriter();
            string fileName = $"BellumCivile{date}";
            int index = Math.Max(0, startingIndex);

            while (true)
            {
                string suffix = index == 0 ? string.Empty : $"_{index}";
                string path = Path.Combine(LogDirectory, fileName + suffix + ".log");
                long length = File.Exists(path) ? new FileInfo(path).Length : 0L;
                if (length < MaxLogBytes)
                {
                    FileStream stream = new FileStream(
                        path,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite,
                        LogBufferBytes,
                        FileOptions.SequentialScan);
                    _logWriter = new StreamWriter(stream, LogEncoding, LogBufferBytes);
                    _activeLogDate = date;
                    _activeLogBytes = length;
                    _activeLogIndex = index;
                    _linesSinceFlush = 0;
                    _lastFlushUtc = DateTime.UtcNow;
                    return;
                }

                index++;
            }
        }

        private static void FlushLogWriter()
        {
            FlushLogWriter(DateTime.UtcNow);
        }

        private static void FlushLogWriter(DateTime now)
        {
            if (_logWriter == null)
                return;

            _logWriter.Flush();
            _linesSinceFlush = 0;
            _lastFlushUtc = now;
        }

        private static void CloseLogWriter()
        {
            try
            {
                _logWriter?.Flush();
                _logWriter?.Dispose();
            }
            catch
            {
                // Best-effort shutdown only.
            }

            _logWriter = null;
            _activeLogDate = string.Empty;
            _activeLogBytes = 0L;
            _activeLogIndex = -1;
            _linesSinceFlush = 0;
        }

        private static void PruneOldLogsOnce()
        {
            if (_prunedOldLogsThisSession)
                return;

            _prunedOldLogsThisSession = true;
            DateTime cutoff = DateTime.Today.AddDays(-DaysToKeepLogs);

            foreach (string path in Directory.GetFiles(LogDirectory, "BellumCivile*.log"))
            {
                try
                {
                    if (TryGetLogDate(path, out DateTime logDate) && logDate < cutoff)
                        File.Delete(path);
                }
                catch
                {
                    // Best-effort cleanup only; never let log pruning break gameplay.
                }
            }
        }

        private static bool TryGetLogDate(string path, out DateTime date)
        {
            date = default(DateTime);

            string name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name) || !name.StartsWith("BellumCivile"))
                return false;

            string dateText = name.Substring("BellumCivile".Length);
            int separatorIndex = dateText.IndexOf('_');
            if (separatorIndex >= 0)
                dateText = dateText.Substring(0, separatorIndex);

            if (dateText.Length != 8)
                return false;

            return DateTime.TryParseExact(
                dateText,
                "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out date);
        }
    }
}
