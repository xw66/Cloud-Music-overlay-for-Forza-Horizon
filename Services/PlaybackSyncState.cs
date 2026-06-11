namespace HorizonRadioOverlay.Services;

public sealed class PlaybackSyncState
{
    private const double SeekThresholdSeconds = 2.0;
    private const double QuantizedSampleToleranceSeconds = 0.95;

    private DateTime _anchorReadTimeUtc;
    private double _anchorPositionSeconds;
    private bool _isPlaying;

    public double LastSmtcPositionSeconds { get; private set; }

    public DateTime LastSmtcReadTimeUtc { get; private set; }

    public bool HasState { get; private set; }

    public void Reset()
    {
        HasState = false;
        LastSmtcPositionSeconds = 0;
        LastSmtcReadTimeUtc = default;
        _anchorReadTimeUtc = default;
        _anchorPositionSeconds = 0;
        _isPlaying = false;
    }

    public void UpdateFromSmtc(double smtcPositionSeconds, bool isPlaying)
    {
        UpdateFromSmtc(smtcPositionSeconds, isPlaying, DateTime.UtcNow);
    }

    internal void UpdateFromSmtc(double smtcPositionSeconds, bool isPlaying, DateTime nowUtc)
    {
        double wallClockPosition = GetCurrentPositionSeconds(nowUtc) ?? smtcPositionSeconds;

        LastSmtcPositionSeconds = smtcPositionSeconds;
        LastSmtcReadTimeUtc = nowUtc;

        if (!HasState)
        {
            SetAnchor(smtcPositionSeconds, isPlaying, nowUtc);
            HasState = true;
            return;
        }

        if (_isPlaying != isPlaying)
        {
            SetAnchor(smtcPositionSeconds, isPlaying, nowUtc);
            return;
        }

        if (Math.Abs(smtcPositionSeconds - wallClockPosition) > SeekThresholdSeconds)
        {
            SetAnchor(smtcPositionSeconds, isPlaying, nowUtc);
            return;
        }

        if (isPlaying && Math.Abs(smtcPositionSeconds - wallClockPosition) <= QuantizedSampleToleranceSeconds)
        {
            return;
        }

        SetAnchor(smtcPositionSeconds, isPlaying, nowUtc);
    }

    public double? GetCurrentPositionSeconds()
    {
        return GetCurrentPositionSeconds(DateTime.UtcNow);
    }

    internal double? GetCurrentPositionSeconds(DateTime nowUtc)
    {
        if (!HasState)
        {
            return null;
        }

        if (!_isPlaying)
        {
            return _anchorPositionSeconds;
        }

        double elapsedSeconds = Math.Max(0, (nowUtc - _anchorReadTimeUtc).TotalSeconds);
        return _anchorPositionSeconds + elapsedSeconds;
    }

    private void SetAnchor(double positionSeconds, bool isPlaying, DateTime nowUtc)
    {
        _anchorPositionSeconds = positionSeconds;
        _anchorReadTimeUtc = nowUtc;
        _isPlaying = isPlaying;
    }
}
