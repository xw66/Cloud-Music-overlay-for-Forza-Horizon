using System.IO;

namespace HorizonRadioOverlay.Services;

public sealed class DiagnosticService : IDisposable
{
    private const long MaxLogSizeBytes = 2 * 1024 * 1024;
    private readonly string _logPath;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private bool _enabled;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (_enabled)
            {
                OpenWriter();
            }
            else
            {
                CloseWriter();
            }
        }
    }

    public DiagnosticService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logPath = Path.Combine(appData, "HorizonRadioOverlay", "diagnostic.log");
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? ex = null)
    {
        string msg = ex != null ? $"{message}: {ex.Message}" : message;
        Write("ERROR", msg);
    }

    private void Write(string level, string message)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            try
            {
                if (_writer == null) OpenWriter();
                if (_writer == null) return;

                RotateIfNeeded();

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                _writer.WriteLine(line);
                _writer.Flush();
            }
            catch
            {
            }
        }
    }

    private void OpenWriter()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            _writer = new StreamWriter(_logPath, append: true, encoding: System.Text.Encoding.UTF8)
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
