namespace HorizonRadioOverlay.Services;

public readonly record struct SmtcLyricTimingSample(
    double RawPositionSeconds,
    double DisplayPositionSeconds,
    double CompensationSeconds,
    bool IsDiscontinuity);

public sealed class SmtcLyricTimingController
{
    private const double MaxAdaptiveAdjustmentSeconds = 0.25;
    private const double MinAdaptiveAdjustmentSeconds = -0.10;
    private const double SampleJitterIgnoreSeconds = 0.12;
    private const double AdaptiveSmoothingFactor = 0.08;
    private const int MinimumStableLagSamples = 4;

    private readonly PlaybackSyncState _playbackSyncState = new();
    private double _adaptiveAdjustmentSeconds;
    private double? _delayOverrideSeconds;
    private int _stableLagDirection;
    private int _stableLagSampleCount;

    public double CurrentCompensationSeconds => _delayOverrideSeconds ?? (SmtcLyricsSyncPolicy.DisplayDelayCompensationSeconds + _adaptiveAdjustmentSeconds);

    public double? GetCurrentRawPositionSeconds()
    {
        return _playbackSyncState.GetCurrentPositionSeconds();
    }

    public double? GetCurrentDisplayPositionSeconds()
    {
        double? rawPositionSeconds = GetCurrentRawPositionSeconds();
        return rawPositionSeconds.HasValue
            ? Math.Max(0, rawPositionSeconds.Value - CurrentCompensationSeconds)
            : null;
    }

    public void Reset()
    {
        _playbackSyncState.Reset();
        _adaptiveAdjustmentSeconds = 0;
        _stableLagDirection = 0;
        _stableLagSampleCount = 0;
    }

    public void SetDelayOverrideMilliseconds(double? delayOverrideMs)
    {
        _delayOverrideSeconds = delayOverrideMs.HasValue ? delayOverrideMs.Value / 1000.0 : null;
    }

    public SmtcLyricTimingSample Update(double smtcPositionSeconds, bool isPlaying)
    {
        return Update(smtcPositionSeconds, isPlaying, DateTime.UtcNow);
    }

    internal SmtcLyricTimingSample Update(double smtcPositionSeconds, bool isPlaying, DateTime nowUtc)
    {
        double predictedBeforeUpdate = _playbackSyncState.GetCurrentPositionSeconds(nowUtc) ?? smtcPositionSeconds;
        bool wasTracking = _playbackSyncState.HasState;
        bool isDiscontinuity = wasTracking &&
                               SmtcLyricsSyncPolicy.ShouldResyncFromSmtc(smtcPositionSeconds, predictedBeforeUpdate);

        _playbackSyncState.UpdateFromSmtc(smtcPositionSeconds, isPlaying, nowUtc);

        double rawPositionSeconds = _playbackSyncState.GetCurrentPositionSeconds(nowUtc) ?? smtcPositionSeconds;

        if (_delayOverrideSeconds == null)
        {
            UpdateAdaptiveAdjustment(predictedBeforeUpdate, smtcPositionSeconds, wasTracking, isDiscontinuity, isPlaying);
        }

        double displayPositionSeconds = Math.Max(0, rawPositionSeconds - CurrentCompensationSeconds);
        return new SmtcLyricTimingSample(rawPositionSeconds, displayPositionSeconds, CurrentCompensationSeconds, isDiscontinuity);
    }

    private void UpdateAdaptiveAdjustment(
        double predictedBeforeUpdate,
        double smtcPositionSeconds,
        bool wasTracking,
        bool isDiscontinuity,
        bool isPlaying)
    {
        if (!wasTracking || isDiscontinuity || !isPlaying)
        {
            _adaptiveAdjustmentSeconds *= 0.5;
            _stableLagDirection = 0;
            _stableLagSampleCount = 0;
            return;
        }

        double lagSeconds = predictedBeforeUpdate - smtcPositionSeconds;
        if (Math.Abs(lagSeconds) <= SampleJitterIgnoreSeconds)
        {
            _adaptiveAdjustmentSeconds *= 0.92;
            _stableLagDirection = 0;
            _stableLagSampleCount = 0;
            return;
        }

        int direction = Math.Sign(lagSeconds);
        if (direction == _stableLagDirection)
        {
            _stableLagSampleCount++;
        }
        else
        {
            _stableLagDirection = direction;
            _stableLagSampleCount = 1;
        }

        if (_stableLagSampleCount < MinimumStableLagSamples)
        {
            return;
        }

        double targetAdjustmentSeconds = Math.Clamp(lagSeconds, MinAdaptiveAdjustmentSeconds, MaxAdaptiveAdjustmentSeconds);
        _adaptiveAdjustmentSeconds += (targetAdjustmentSeconds - _adaptiveAdjustmentSeconds) * AdaptiveSmoothingFactor;
    }
}
