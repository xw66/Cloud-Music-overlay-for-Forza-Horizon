namespace HorizonRadioOverlay.Services;

public sealed class ServiceLifecycle : IDisposable
{
    private readonly List<ServiceEntry> _entries = new();
    private bool _started;
    private bool _disposed;

    public void Register(string name, Action? onStart = null, Action? onStop = null, Action? onDispose = null)
    {
        _entries.Add(new ServiceEntry(name, onStart, onStop, onDispose));
    }

    public void StartAll()
    {
        if (_started) return;
        _started = true;

        foreach (var entry in _entries)
        {
            try
            {
                entry.OnStart?.Invoke();
            }
            catch
            {
            }
        }
    }

    public void StopAll()
    {
        if (!_started) return;
        _started = false;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            try
            {
                _entries[i].OnStop?.Invoke();
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAll();

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            try
            {
                _entries[i].OnDispose?.Invoke();
            }
            catch
            {
            }
        }

        _entries.Clear();
    }

    private sealed record ServiceEntry(string Name, Action? OnStart, Action? OnStop, Action? OnDispose);
}
