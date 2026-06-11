using System.IO;

namespace HorizonRadioOverlay.Services;

public sealed class DiagnosticService : IDisposable
{
    private const long MaxLogSizeBytes = 2 * 1024 * 1024;
    private const int MaxBufferedLines = 400;
    private readonly string _logPath;
    private readonly object _lock = new();
    private readonly Queue<string> _recentLines = new();
    private StreamWriter? _writer;
    private bool _enabled;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
        }
    }

    public string LogFilePath => _logPath;

    public DiagnosticService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logPath = Path.Combine(appData, "HorizonRadioOverlay", "diagnostic.log");
        EnsureLogFile();
        OpenWriter();
    }

    public void Event(string message) => Write("INFO", message, requiresVerbose: false);
    public void Info(string message) => Write("INFO", message, requiresVerbose: true);
    public void Warn(string message) => Write("WARN", message, requiresVerbose: false);
    public void Error(string message, Exception? ex = null)
    {
        string msg = ex != null ? $"{message}: {ex.Message}" : message;
        Write("ERROR", msg, requiresVerbose: false);
    }

    public string GetBufferedText()
    {
        lock (_lock)
        {
            return _recentLines.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, _recentLines);
        }
    }

    public string ReadCurrentLogText()
    {
        string buffered = GetBufferedText();
        if (!string.IsNullOrWhiteSpace(buffered))
        {
            return buffered;
        }

        try
        {
            return File.Exists(_logPath) ? File.ReadAllText(_logPath) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _recentLines.Clear();

            try
            {
                try { _writer?.Dispose(); } catch { }
                _writer = null;
                EnsureLogFile();
                File.WriteAllText(_logPath, string.Empty);
                OpenWriter();
            }
            catch
            {
            }
        }
    }

    private void Write(string level, string message, bool requiresVerbose)
    {
        if (requiresVerbose && !_enabled) return;

        lock (_lock)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                _recentLines.Enqueue(line);
                while (_recentLines.Count > MaxBufferedLines)
                {
                    _recentLines.Dequeue();
                }

                if (_writer == null) OpenWriter();
                if (_writer == null) return;

                RotateIfNeeded();
                _writer.WriteLine(line);
                _writer.Flush();
            }
            catch
            {
            }
        }
    }

    private void EnsureLogFile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            if (!File.Exists(_logPath))
            {
                using (File.Create(_logPath))
                {
                }
            }
        }
        catch
        {
        }
    }

    private void OpenWriter()
    {
        try
        {
            EnsureLogFile();
            FileStream stream = new(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, System.Text.Encoding.UTF8)
            {
                AutoFlush = false
            };
        }
        catch
        {
            _writer = null;
        }
    }

    private void CloseWriter()
    {
        lock (_lock)
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logPath)) return;
            var info = new FileInfo(_logPath);
            if (info.Length < MaxLogSizeBytes) return;

            _writer?.Dispose();
            _writer = null;

            string backupPath = _logPath + ".old";
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(_logPath, backupPath);

            OpenWriter();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        CloseWriter();
    }
}
