namespace HorizonRadioOverlay.Services;

public sealed class NeteaseLyricTimingController
{
    private DateTime _anchorTimeUtc;
    private double _anchorPositionSeconds;
    private bool _isPlaying;

    public bool HasState { get; private set; }

    public void Start()
    {
        Start(DateTime.UtcNow);
    }

    internal void Start(DateTime nowUtc)
    {
        SetAnchor(0, isPlaying: true, nowUtc);
        HasState = true;
    }

    public void Reset()
    {
        HasState = false;
        _anchorTimeUtc = default;
        _anchorPositionSeconds = 0;
        _isPlaying = false;
    }

    public void UpdateFromPlayer(double positionSeconds, bool isPlaying)
    {
        UpdateFromPlayer(positionSeconds, isPlaying, DateTime.UtcNow);
    }

    internal void UpdateFromPlayer(double positionSeconds, bool isPlaying, DateTime nowUtc)
    {
        _ = positionSeconds;
        _ = isPlaying;
        _ = nowUtc;
    }

    public double GetCurrentPositionSeconds()
    {
        return GetCurrentPositionSeconds(DateTime.UtcNow);
    }

    internal double GetCurrentPositionSeconds(DateTime nowUtc)
    {
        if (!HasState)
        {
            return 0;
        }

        if (!_isPlaying)
        {
            return _anchorPositionSeconds;
        }

        return _anchorPositionSeconds + Math.Max(0, (nowUtc - _anchorTimeUtc).TotalSeconds);
    }

    private void SetAnchor(double positionSeconds, bool isPlaying, DateTime nowUtc)
    {
        _anchorPositionSeconds = positionSeconds;
        _anchorTimeUtc = nowUtc;
        _isPlaying = isPlaying;
    }
}
