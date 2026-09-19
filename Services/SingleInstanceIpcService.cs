using System.IO;
using System.IO.Pipes;

namespace HorizonRadioOverlay.Services;

/// <summary>
/// 提供基于命名管道（Named Pipe）的单实例跨进程唤醒与通信服务。
/// </summary>
public sealed class SingleInstanceIpcService : IDisposable
{
    public const string DefaultPipeName = "HorizonRadioOverlay.IpcPipe";

    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private bool _disposed;

    /// <summary>
    /// 当接收到其他进程发来的消息时触发。
    /// </summary>
    public event Action<string>? MessageReceived;

    public SingleInstanceIpcService(string pipeName = DefaultPipeName)
    {
        _pipeName = pipeName;
    }

    /// <summary>
    /// 启动命名管道后台监听。
    /// </summary>
    public void StartServer()
    {
        if (_listenTask != null || _disposed)
        {
            return;
        }

        _listenTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);

                using var reader = new StreamReader(server);
                string? message = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    try
                    {
                        MessageReceived?.Invoke(message);
                    }
                    catch
                    {
                        // 避免事件订阅方异常导致监听循环中断
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    /// <summary>
    /// 向已在运行的实例发送通知消息（异步）。
    /// </summary>
    public static async Task<bool> NotifyExistingInstanceAsync(
        string message,
        string pipeName = DefaultPipeName,
        int timeoutMs = 1500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            using var timeoutCts = new CancellationTokenSource(timeoutMs);

            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(message.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 向已在运行的实例发送通知消息（同步）。
    /// </summary>
    public static bool NotifyExistingInstance(
        string message,
        string pipeName = DefaultPipeName,
        int timeoutMs = 1500)
    {
        try
        {
            return NotifyExistingInstanceAsync(message, pipeName, timeoutMs).GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }
        _cts.Dispose();
    }
}
