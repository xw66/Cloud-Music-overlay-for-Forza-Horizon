using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public enum PlaybackDataHealth
{
    Starting,
    Healthy,
    Degraded,
    Unavailable
}

public sealed record PlaybackSnapshot(
    string SourceId,
    string SourceDisplayName,
    PlaybackSourceCapabilities Capabilities,
    TrackInfo? Track,
    TimeSpan? Position,
    bool? IsPlaying,
    DateTimeOffset CapturedAt,
    DateTimeOffset? TrackCapturedAt,
    DateTimeOffset? TimelineCapturedAt,
    long TrackVersion,
    long TimelineVersion,
    PlaybackDataHealth Health,
    int ConsecutiveFailures,
    string? LastError)
{
    public static PlaybackSnapshot StartingFor(IPlaybackSource source, DateTimeOffset now)
    {
        return new PlaybackSnapshot(
            source.Id,
            source.DisplayName,
            source.Capabilities,
            null,
            null,
            null,
            now,
            null,
            null,
            0,
            0,
            source.IsAvailable ? PlaybackDataHealth.Starting : PlaybackDataHealth.Unavailable,
            0,
            null);
    }
}

public sealed class PlaybackSessionService : IDisposable
{
    internal const int FailureThreshold = 3;

    private static readonly TimeSpan DefaultTrackInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NeteaseTrackInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SmtcTrackInterval = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan DefaultTimelineInterval = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan DefaultFailureCooldown = TimeSpan.FromSeconds(2);

    private readonly PlaybackCoordinator _coordinator;
    private readonly DiagnosticService? _diagnostic;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _trackInterval;
    private readonly TimeSpan _timelineInterval;
    private readonly TimeSpan _failureCooldown;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    private string _sourceId;
    private bool _timelineEnabled;
    private long _configurationVersion;
    private DateTimeOffset _nextTrackRefreshAt;
    private DateTimeOffset _nextTimelineRefreshAt;
    private DateTimeOffset _circuitOpenUntil;
    private int _consecutiveTrackFailures;
    private PlaybackSnapshot _latestSnapshot;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private bool _disposed;

    public PlaybackSessionService(
        PlaybackCoordinator coordinator,
        DiagnosticService? diagnostic = null,
        TimeProvider? timeProvider = null,
        TimeSpan? trackInterval = null,
        TimeSpan? timelineInterval = null,
        TimeSpan? failureCooldown = null)
    {
        _coordinator = coordinator;
        _diagnostic = diagnostic;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _trackInterval = trackInterval ?? DefaultTrackInterval;
        _timelineInterval = timelineInterval ?? DefaultTimelineInterval;
        _failureCooldown = failureCooldown ?? DefaultFailureCooldown;
        _sourceId = PlaybackSourceIds.Netease;
        IPlaybackSource source = _coordinator.GetSource(_sourceId);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        _latestSnapshot = PlaybackSnapshot.StartingFor(source, now);
        _nextTrackRefreshAt = DateTimeOffset.MinValue;
        _nextTimelineRefreshAt = DateTimeOffset.MinValue;
    }

    public PlaybackSnapshot LatestSnapshot
    {
        get
        {
            lock (_stateGate)
            {
                return _latestSnapshot;
            }
        }
    }

    public void Configure(string? sourceId, bool timelineEnabled)
    {
        ThrowIfDisposed();
        IPlaybackSource source = _coordinator.GetSource(sourceId);
        lock (_stateGate)
        {
            bool sourceChanged = !string.Equals(
                _sourceId,
                source.Id,
                StringComparison.OrdinalIgnoreCase);
            bool timelineChanged = _timelineEnabled != timelineEnabled;
            if (!sourceChanged && !timelineChanged)
            {
                return;
            }

            _sourceId = source.Id;
            _timelineEnabled = timelineEnabled;
            _configurationVersion++;
            _nextTrackRefreshAt = DateTimeOffset.MinValue;
            _nextTimelineRefreshAt = DateTimeOffset.MinValue;
            _circuitOpenUntil = DateTimeOffset.MinValue;
            _consecutiveTrackFailures = 0;
            if (sourceChanged)
            {
                _latestSnapshot = PlaybackSnapshot.StartingFor(
                    source,
                    _timeProvider.GetUtcNow());
            }
            else if (!timelineEnabled)
            {
                _latestSnapshot = _latestSnapshot with
                {
                    Position = null,
                    IsPlaying = null,
                    TimelineCapturedAt = null,
                    TimelineVersion = 0,
                    ConsecutiveFailures = 0,
                    LastError = null,
                    Health = source.IsAvailable
                        ? PlaybackDataHealth.Starting
                        : PlaybackDataHealth.Unavailable
                };
            }
        }

        Wake();
    }

    public void Start()
    {
        ThrowIfDisposed();
        lock (_stateGate)
        {
            if (_runTask is { IsCompleted: false })
            {
                return;
            }

            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunAsync(_runCts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_stateGate)
        {
            cts = _runCts;
            _runCts = null;
        }

        if (cts == null)
        {
            return;
        }

        cts.Cancel();
        Wake();
        cts.Dispose();
    }

    public void RequestImmediateRefresh()
    {
        ThrowIfDisposed();
        lock (_stateGate)
        {
            _nextTrackRefreshAt = DateTimeOffset.MinValue;
            _nextTimelineRefreshAt = DateTimeOffset.MinValue;
            _circuitOpenUntil = DateTimeOffset.MinValue;
        }

        Wake();
    }

    public Task<PlaybackSnapshot> RefreshTrackAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(includeTrack: true, includeTimeline: false, cancellationToken);
    }

    public Task<PlaybackSnapshot> RefreshTimelineAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(includeTrack: false, includeTimeline: true, cancellationToken);
    }

    public async Task<PlaybackSnapshot> RefreshAsync(
        bool includeTrack,
        bool includeTimeline,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string sourceId;
            long configurationVersion;
            lock (_stateGate)
            {
                sourceId = _sourceId;
                configurationVersion = _configurationVersion;
                includeTimeline &= _timelineEnabled;
            }

            IPlaybackSource source = _coordinator.GetSource(sourceId);
            DateTimeOffset capturedAt = _timeProvider.GetUtcNow();
            if (!source.IsAvailable)
            {
                return CommitUnavailable(
                    source,
                    capturedAt,
                    configurationVersion,
                    includeTrack,
                    includeTimeline);
            }

            TrackInfo? track = null;
            (TimeSpan Position, bool IsPlaying)? timeline = null;
            Exception? failure = null;

            if (includeTrack)
            {
                try
                {
                    track = await _coordinator
                        .GetCurrentTrackAsync(sourceId)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }

            bool supportsTimeline = source.Capabilities
                .HasFlag(PlaybackSourceCapabilities.PlaybackState);
            if (includeTimeline && supportsTimeline)
            {
                try
                {
                    timeline = await _coordinator
                        .GetPlaybackStateAsync(sourceId)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failure ??= ex;
                }
            }

            bool trackFailed = includeTrack && track == null;
            bool timelineFailed = includeTimeline && supportsTimeline && timeline == null;
            bool operationFailed = failure != null || trackFailed || timelineFailed;
            return CommitSample(
                source,
                capturedAt,
                configurationVersion,
                includeTrack,
                track,
                includeTimeline && supportsTimeline,
                timeline,
                operationFailed,
                failure);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                bool includeTrack;
                bool includeTimeline;
                TimeSpan delay;
                lock (_stateGate)
                {
                    if (_circuitOpenUntil > now)
                    {
                        includeTrack = false;
                        includeTimeline = _timelineEnabled &&
                            now >= _nextTimelineRefreshAt;
                        DateTimeOffset nextRefresh = _timelineEnabled
                            ? Min(_circuitOpenUntil, _nextTimelineRefreshAt)
                            : _circuitOpenUntil;
                        delay = nextRefresh > now
                            ? nextRefresh - now
                            : TimeSpan.Zero;
                    }
                    else
                    {
                        includeTrack = now >= _nextTrackRefreshAt;
                        includeTimeline = _timelineEnabled && now >= _nextTimelineRefreshAt;
                        DateTimeOffset nextRefresh = _timelineEnabled
                            ? Min(_nextTrackRefreshAt, _nextTimelineRefreshAt)
                            : _nextTrackRefreshAt;
                        delay = nextRefresh > now
                            ? nextRefresh - now
                            : TimeSpan.Zero;
                    }
                }

                if (includeTrack || includeTimeline)
                {
                    await RefreshAsync(
                        includeTrack,
                        includeTimeline,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await WaitForWakeAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _diagnostic?.Error("后台播放会话轮询失败", ex);
                await WaitForWakeAsync(_failureCooldown, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private PlaybackSnapshot CommitUnavailable(
        IPlaybackSource source,
        DateTimeOffset capturedAt,
        long configurationVersion,
        bool includeTrack,
        bool includeTimeline)
    {
        lock (_stateGate)
        {
            if (configurationVersion != _configurationVersion)
            {
                return _latestSnapshot;
            }

            PlaybackDataHealth previousHealth = _latestSnapshot.Health;
            _latestSnapshot = _latestSnapshot with
            {
                SourceId = source.Id,
                SourceDisplayName = source.DisplayName,
                Capabilities = source.Capabilities,
                CapturedAt = capturedAt,
                Health = PlaybackDataHealth.Unavailable,
                ConsecutiveFailures = _latestSnapshot.ConsecutiveFailures + 1,
                LastError = "播放器数据源当前不可用"
            };
            ScheduleNextLocked(
                capturedAt,
                includeTrack,
                trackFailed: includeTrack,
                includeTimeline);
            LogHealthChange(previousHealth, _latestSnapshot);
            return _latestSnapshot;
        }
    }

    private PlaybackSnapshot CommitSample(
        IPlaybackSource source,
        DateTimeOffset capturedAt,
        long configurationVersion,
        bool includeTrack,
        TrackInfo? track,
        bool includeTimeline,
        (TimeSpan Position, bool IsPlaying)? timeline,
        bool failed,
        Exception? failure)
    {
        lock (_stateGate)
        {
            if (configurationVersion != _configurationVersion)
            {
                return _latestSnapshot;
            }

            PlaybackDataHealth previousHealth = _latestSnapshot.Health;
            int consecutiveFailures = failed
                ? _latestSnapshot.ConsecutiveFailures + 1
                : 0;
            PlaybackDataHealth health = failed
                ? consecutiveFailures >= FailureThreshold
                    ? PlaybackDataHealth.Degraded
                    : previousHealth == PlaybackDataHealth.Healthy
                        ? PlaybackDataHealth.Healthy
                        : PlaybackDataHealth.Starting
                : PlaybackDataHealth.Healthy;
            string? lastError = failed
                ? failure?.Message ?? BuildMissingDataMessage(includeTrack, track, includeTimeline, timeline)
                : null;
            TrackInfo? nextTrack = includeTrack ? track : _latestSnapshot.Track;
            TimeSpan? nextPosition = includeTimeline
                ? timeline?.Position
                : _latestSnapshot.Position;
            bool? nextIsPlaying = includeTimeline
                ? timeline?.IsPlaying
                : _latestSnapshot.IsPlaying;
            bool trackChanged = includeTrack &&
                !AreTrackSamplesEqual(_latestSnapshot.Track, nextTrack);
            bool timelineChanged = includeTimeline &&
                (_latestSnapshot.Position != nextPosition ||
                 _latestSnapshot.IsPlaying != nextIsPlaying);

            _latestSnapshot = _latestSnapshot with
            {
                SourceId = source.Id,
                SourceDisplayName = source.DisplayName,
                Capabilities = source.Capabilities,
                Track = nextTrack,
                Position = nextPosition,
                IsPlaying = nextIsPlaying,
                CapturedAt = capturedAt,
                TrackCapturedAt = includeTrack ? capturedAt : _latestSnapshot.TrackCapturedAt,
                TimelineCapturedAt = includeTimeline ? capturedAt : _latestSnapshot.TimelineCapturedAt,
                TrackVersion = trackChanged
                    ? _latestSnapshot.TrackVersion + 1
                    : _latestSnapshot.TrackVersion,
                TimelineVersion = timelineChanged
                    ? _latestSnapshot.TimelineVersion + 1
                    : _latestSnapshot.TimelineVersion,
                Health = health,
                ConsecutiveFailures = consecutiveFailures,
                LastError = lastError
            };
            bool trackFailed = includeTrack && track == null;
            ScheduleNextLocked(
                capturedAt,
                includeTrack,
                trackFailed,
                includeTimeline);
            LogHealthChange(previousHealth, _latestSnapshot);
            return _latestSnapshot;
        }
    }

    private void ScheduleNextLocked(
        DateTimeOffset capturedAt,
        bool includeTrack,
        bool trackFailed,
        bool includeTimeline)
    {
        if (includeTrack)
        {
            _nextTrackRefreshAt = capturedAt + GetTrackRefreshInterval(
                _latestSnapshot.SourceId,
                _trackInterval);
            _consecutiveTrackFailures = trackFailed
                ? _consecutiveTrackFailures + 1
                : 0;
            if (trackFailed &&
                _consecutiveTrackFailures >= FailureThreshold)
            {
                _circuitOpenUntil = capturedAt + GetFailureCooldown(
                    _failureCooldown,
                    _consecutiveTrackFailures);
            }
            else if (!trackFailed)
            {
                _circuitOpenUntil = DateTimeOffset.MinValue;
            }
        }

        if (includeTimeline)
        {
            _nextTimelineRefreshAt = capturedAt + _timelineInterval;
        }
    }

    private void LogHealthChange(
        PlaybackDataHealth previousHealth,
        PlaybackSnapshot snapshot)
    {
        if (previousHealth == snapshot.Health)
        {
            return;
        }

        _diagnostic?.Event(
            $"播放数据源状态：source={snapshot.SourceId}, " +
            $"health={snapshot.Health}, failures={snapshot.ConsecutiveFailures}, " +
            $"error={snapshot.LastError ?? "<none>"}");
    }

    private async Task WaitForWakeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            delay = TimeSpan.FromMilliseconds(25);
        }

        await _wakeSignal
            .WaitAsync(delay, cancellationToken)
            .ConfigureAwait(false);
    }

    private void Wake()
    {
        try
        {
            if (_wakeSignal.CurrentCount == 0)
            {
                _wakeSignal.Release();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string BuildMissingDataMessage(
        bool includeTrack,
        TrackInfo? track,
        bool includeTimeline,
        (TimeSpan Position, bool IsPlaying)? timeline)
    {
        if (includeTrack && track == null && includeTimeline && timeline == null)
        {
            return "未读取到歌曲信息和播放时间轴";
        }

        if (includeTrack && track == null)
        {
            return "未读取到歌曲信息";
        }

        return "未读取到播放时间轴";
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left <= right ? left : right;
    }

    private static bool AreTrackSamplesEqual(TrackInfo? left, TrackInfo? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        return string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
               string.Equals(left.Artist, right.Artist, StringComparison.Ordinal) &&
               string.Equals(left.Subtitle, right.Subtitle, StringComparison.Ordinal) &&
               string.Equals(left.AlbumTitle, right.AlbumTitle, StringComparison.Ordinal) &&
               string.Equals(left.SourceAppId, right.SourceAppId, StringComparison.Ordinal) &&
               string.Equals(left.SongId, right.SongId, StringComparison.Ordinal) &&
               left.DurationSeconds.Equals(right.DurationSeconds) &&
               string.Equals(left.CoverSource, right.CoverSource, StringComparison.Ordinal) &&
               AreCoverBytesEqual(left.CoverBytes, right.CoverBytes);
    }

    private static bool AreCoverBytesEqual(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left != null &&
               right != null &&
               left.AsSpan().SequenceEqual(right);
    }

    internal static TimeSpan GetFailureCooldown(
        TimeSpan baseCooldown,
        int consecutiveFailures)
    {
        int exponent = Math.Clamp(
            consecutiveFailures - FailureThreshold,
            0,
            4);
        return TimeSpan.FromTicks(
            checked(baseCooldown.Ticks * (1L << exponent)));
    }

    internal static TimeSpan GetTrackRefreshInterval(
        string sourceId,
        TimeSpan configuredInterval)
    {
        if (string.Equals(
                sourceId,
                PlaybackSourceIds.Netease,
                StringComparison.OrdinalIgnoreCase) &&
            configuredInterval > NeteaseTrackInterval)
        {
            return NeteaseTrackInterval;
        }

        return PlaybackCoordinator.IsSmtcSource(sourceId) &&
               configuredInterval > SmtcTrackInterval
            ? SmtcTrackInterval
            : configuredInterval;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
