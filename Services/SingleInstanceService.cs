using System.Runtime.Versioning;

namespace HorizonRadioOverlay.Services;

[SupportedOSPlatform("windows")]
public sealed class SingleInstanceService : IDisposable
{
    internal const string DefaultMutexName = @"Local\HorizonRadioOverlay.SingleInstance";
    internal const string DefaultActivationEventName = @"Local\HorizonRadioOverlay.ActivateExistingInstance";

    private readonly string _mutexName;
    private readonly string _activationEventName;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;
    private bool _disposed;

    public event EventHandler? ActivationRequested;

    public SingleInstanceService()
        : this(DefaultMutexName, DefaultActivationEventName)
    {
    }

    internal SingleInstanceService(string mutexName, string activationEventName)
    {
        _mutexName = mutexName;
        _activationEventName = activationEventName;
    }

    public bool TryAcquire()
    {
        if (_mutex != null)
        {
            return _ownsMutex;
        }

        _mutex = new Mutex(initiallyOwned: true, _mutexName, out bool createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            return false;
        }

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            _activationEventName);
        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => ActivationRequested?.Invoke(this, EventArgs.Empty),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
        return true;
    }

    public void NotifyExistingInstance()
    {
        try
        {
            using EventWaitHandle activationEvent = EventWaitHandle.OpenExisting(_activationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activationWait?.Unregister(null);
        _activationWait = null;
        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex?.Dispose();
        _mutex = null;
        _ownsMutex = false;
    }
}
