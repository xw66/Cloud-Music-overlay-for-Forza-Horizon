using System.Threading.Channels;
using System.Windows.Threading;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class OverlayAnimationQueue : IDisposable
{
    private readonly Channel<TrackInfo> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processTask;
    private long _sequenceId;

    public OverlayAnimationQueue(Dispatcher dispatcher, Func<TrackInfo, long, CancellationToken, Task> handler)
    {
        _channel = Channel.CreateBounded<TrackInfo>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _processTask = Task.Run(async () =>
        {
            await foreach (var track in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                long id = Interlocked.Increment(ref _sequenceId);
                try
                {
                    await dispatcher.InvokeAsync(() => handler(track, id, _cts.Token));
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                }
            }
        });
    }

    public void Enqueue(TrackInfo track)
    {
        _channel.Writer.TryWrite(track);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try { _processTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _cts.Dispose();
    }
}
