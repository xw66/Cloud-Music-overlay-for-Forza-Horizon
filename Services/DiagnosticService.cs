using System.IO;
using System.Threading.Channels;

namespace HorizonRadioOverlay.Services;

/// <summary>
/// 提供基于异步通道（Channel）的高性能、非阻塞诊断日志服务。
/// </summary>
public sealed class DiagnosticService : IDisposable
{
    private const long MaxLogSizeBytes = 2 * 1024 * 1024;
    private const int MaxBufferedLines = 400;

    private readonly string _logPath;
    private readonly object _bufferLock = new();
    private readonly object _writerLock = new();
    private readonly Queue<string> _recentLines = new();

    private readonly Channel<string> _channel;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _cts = new();

    private StreamWriter? _writer;
    private bool _enabled;
    private bool _disposed;
    private int _pendingWrites;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public string LogFilePath => _logPath;

    public DiagnosticService(string? customLogPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customLogPath))
        {
            _logPath = Path.GetFullPath(customLogPath);
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logPath = Path.Combine(appData, "HorizonRadioOverlay", "diagnostic.log");
        }

        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

        _writerTask = Task.Run(ProcessQueueAsync);
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
        lock (_bufferLock)
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

        return ReadFileText();
    }

    public string ReadFileText()
    {
        try
        {
            if (!File.Exists(_logPath)) return string.Empty;
            using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Clear()
    {
        lock (_bufferLock)
        {
            _recentLines.Clear();
        }

        Flush();

        lock (_writerLock)
        {
            CloseWriter();
            try
            {
                EnsureLogFile();
                using (var stream = new FileStream(_logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                }
            }
            catch
            {
            }
        }
    }

    public void Flush()
    {
        if (_disposed) return;

        var spin = new SpinWait();
        int attempts = 0;
        while (Volatile.Read(ref _pendingWrites) > 0 && attempts++ < 200)
        {
            spin.SpinOnce();
            Thread.Sleep(5);
        }

        lock (_writerLock)
        {
            try
            {
                _writer?.Flush();
            }
            catch
            {
            }
        }
    }

    private void Write(string level, string message, bool requiresVerbose)
    {
        if (requiresVerbose && !_enabled) return;

        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";

        // 1. 更新内存即时缓冲区 (供 UI 0 延迟刷新)
        lock (_bufferLock)
        {
            _recentLines.Enqueue(line);
            while (_recentLines.Count > MaxBufferedLines)
            {
                _recentLines.Dequeue();
            }
        }

        // 2. 异步入队写盘通道（0ms 阻塞）
        if (!_disposed)
        {
            Interlocked.Increment(ref _pendingWrites);
            if (!_channel.Writer.TryWrite(line))
            {
                Interlocked.Decrement(ref _pendingWrites);
            }
        }
    }

    private async Task ProcessQueueAsync()
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                bool writtenAny = false;

                // 批量拉取所有已到达的日志（最多批处理 100 条）
                int batchCount = 0;
                while (batchCount < 100 && reader.TryRead(out string? line))
                {
                    try
                    {
                        if (line != null)
                        {
                            lock (_writerLock)
                            {
                                EnsureWriter();
                                if (_writer != null)
                                {
                                    RotateIfNeeded();
                                    _writer.WriteLine(line);
                                    writtenAny = true;
                                }
                            }
                            batchCount++;
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _pendingWrites);
                    }
                }

                if (writtenAny)
                {
                    lock (_writerLock)
                    {
                        try
                        {
                            _writer?.Flush();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            // 排空通道中所有遗留项
            try
            {
                lock (_writerLock)
                {
                    EnsureWriter();
                    while (reader.TryRead(out string? line))
                    {
                        try
                        {
                            if (_writer != null && line != null)
                            {
                                RotateIfNeeded();
                                _writer.WriteLine(line);
                            }
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _pendingWrites);
                        }
                    }
                    _writer?.Flush();
                }
            }
            catch
            {
            }
            finally
            {
                lock (_writerLock)
                {
                    CloseWriter();
                }
            }
        }
    }

    private void EnsureLogFile()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
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

    private void EnsureWriter()
    {
        if (_writer != null) return;

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
        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }
        _writer = null;
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logPath)) return;
            var info = new FileInfo(_logPath);
            if (info.Length < MaxLogSizeBytes) return;

            CloseWriter();

            string backupPath = _logPath + ".old";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
            File.Move(_logPath, backupPath);

            EnsureWriter();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _channel.Writer.TryComplete();
        try
        {
            _writerTask.GetAwaiter().GetResult();
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
