namespace HorizonRadioOverlay.Services;

public sealed class NeteaseLyricTimingController
{
    private const double BackwardJumpThresholdSeconds = 1.5;
    private const double BackwardTrajectoryToleranceSeconds = 1.5;
    private const double BackwardConfirmationMinimumSeconds = 0.75;
    private const double BackwardConfirmationMaximumSeconds = 3.0;
    private const int BackwardConfirmationMinimumSamples = 3;

    private DateTime _anchorTimeUtc;
    private double _anchorPositionSeconds;
    private bool _isPlaying;
    private double? _pendingBackwardPositionSeconds;
    private DateTime _pendingBackwardStartedUtc;
    private DateTime _pendingBackwardLastSampleUtc;
    private bool _pendingBackwardIsPlaying;
    private int _pendingBackwardSampleCount;

    public bool HasState { get; private set; }

    public void Start()
    {
        Start(DateTime.UtcNow);
    }

    internal void Start(DateTime nowUtc)
    {
        ClearPendingBackwardSeek();
        SetAnchor(0, isPlaying: true, nowUtc);
        HasState = true;
    }

    public void Reset()
    {
        HasState = false;
        _anchorTimeUtc = default;
        _anchorPositionSeconds = 0;
        _isPlaying = false;
        ClearPendingBackwardSeek();
    }

    public void UpdateFromPlayer(double positionSeconds, bool isPlaying)
    {
        UpdateFromPlayer(positionSeconds, isPlaying, DateTime.UtcNow);
    }

    internal void UpdateFromPlayer(double positionSeconds, bool isPlaying, DateTime nowUtc)
    {
        if (!double.IsFinite(positionSeconds) || positionSeconds < 0)
        {
            return;
        }

        if (HasState)
        {
            double currentPositionSeconds = GetCurrentPositionSeconds(nowUtc);
            if (positionSeconds <
                currentPositionSeconds - BackwardJumpThresholdSeconds)
            {
                if (!ConfirmBackwardSeek(positionSeconds, isPlaying, nowUtc))
                {
                    return;
                }
            }
            else
            {
                ClearPendingBackwardSeek();
            }
        }

        SetAnchor(positionSeconds, isPlaying, nowUtc);
        HasState = true;
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

    private bool ConfirmBackwardSeek(
        double positionSeconds,
        bool isPlaying,
        DateTime nowUtc)
    {
        if (!_pendingBackwardPositionSeconds.HasValue ||
            nowUtc <= _pendingBackwardLastSampleUtc ||
            (nowUtc - _pendingBackwardStartedUtc).TotalSeconds >
            BackwardConfirmationMaximumSeconds)
        {
            StartPendingBackwardSeek(positionSeconds, isPlaying, nowUtc);
            return false;
        }

        double elapsedSeconds =
            (nowUtc - _pendingBackwardStartedUtc).TotalSeconds;
        double expectedPositionSeconds = _pendingBackwardPositionSeconds.Value +
            (_pendingBackwardIsPlaying ? elapsedSeconds : 0);
        if (Math.Abs(positionSeconds - expectedPositionSeconds) >
            BackwardTrajectoryToleranceSeconds)
        {
            StartPendingBackwardSeek(positionSeconds, isPlaying, nowUtc);
            return false;
        }

        _pendingBackwardLastSampleUtc = nowUtc;
        _pendingBackwardSampleCount++;
        if (_pendingBackwardSampleCount < BackwardConfirmationMinimumSamples ||
            elapsedSeconds < BackwardConfirmationMinimumSeconds)
        {
            return false;
        }

        ClearPendingBackwardSeek();
        return true;
    }

    private void StartPendingBackwardSeek(
        double positionSeconds,
        bool isPlaying,
        DateTime nowUtc)
    {
        _pendingBackwardPositionSeconds = positionSeconds;
        _pendingBackwardStartedUtc = nowUtc;
        _pendingBackwardLastSampleUtc = nowUtc;
        _pendingBackwardIsPlaying = isPlaying;
        _pendingBackwardSampleCount = 1;
    }

    private void ClearPendingBackwardSeek()
    {
        _pendingBackwardPositionSeconds = null;
        _pendingBackwardStartedUtc = default;
        _pendingBackwardLastSampleUtc = default;
        _pendingBackwardIsPlaying = false;
        _pendingBackwardSampleCount = 0;
    }
}
