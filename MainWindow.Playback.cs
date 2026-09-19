using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;
using HorizonRadioOverlay.ViewModels;
using HorizonRadioOverlay.Views.Pages;

namespace HorizonRadioOverlay;

public partial class MainWindow
{
    private void StartPolling()
    {
        lock (_pollWakeSignal)
        {
            if (_pollTask is { IsCompleted: false })
            {
                return;
            }

            _pollCts?.Dispose();
            _pollCts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollingLoopAsync(_pollCts.Token));
        }
    }

    private void StopPolling()
    {
        CancellationTokenSource? cts;
        lock (_pollWakeSignal)
        {
            cts = _pollCts;
            _pollCts = null;
        }

        if (cts == null)
        {
            return;
        }

        cts.Cancel();
        WakePolling();
        cts.Dispose();
    }

    private void DisposePolling()
    {
        StopPolling();
        _pollWakeSignal.Dispose();
    }

    private void BoostPolling()
    {
        _pollBoostUntil = Environment.TickCount64 + PollBoostDurationMs;
        _playbackSession.RequestImmediateRefresh();
        WakePolling();
    }

    private void WakePolling()
    {
        try
        {
            if (_pollWakeSignal.CurrentCount == 0)
            {
                _pollWakeSignal.Release();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void UpdateUiVisibilityCache()
    {
        _isMainWindowVisible = IsVisible && WindowState != WindowState.Minimized;
        _isOverlayVisible = _overlayWindow.IsVisible;
        WakePolling();
    }

    private int CalculatePollInterval()
    {
        bool keepFast = _activeSettings.EnableLyrics &&
                        _playbackCoordinator.GetCapabilities(_activeSettings.TrackSource)
                            .HasFlag(PlaybackSourceCapabilities.Lyrics);
        bool boosted = Environment.TickCount64 < _pollBoostUntil;
        return BackgroundPollingPolicy.GetPollIntervalMs(
            keepFast,
            _isMainWindowVisible,
            _isOverlayVisible,
            boosted);
    }

    private async Task PollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ExecutePollTickAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _diagnostic.Error("播放状态后台轮询失败", ex);
            }

            int delayMs = CalculatePollInterval();
            try
            {
                await _pollWakeSignal.WaitAsync(delayMs, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ExecutePollTickAsync(CancellationToken ct)
    {
        bool isSmtcSource = IsSmtcSource();
        bool shouldSyncLyrics = SmtcLyricsSyncPolicy.ShouldPrioritizeTimelineSync(_activeSettings.EnableLyrics, isSmtcSource);
        bool shouldSyncNeteaseLyrics = _activeSettings.EnableLyrics && !isSmtcSource;
        bool supportsPlaybackState = _playbackCoordinator
            .GetCapabilities(_activeSettings.TrackSource)
            .HasFlag(PlaybackSourceCapabilities.PlaybackState);
        bool shouldSyncNeteaseTimeline = shouldSyncNeteaseLyrics && supportsPlaybackState;
        bool shouldCheckPauseVisibility = _activeSettings.HideOverlayWhenPaused && isSmtcSource;

        PlaybackSnapshot snapshot = _playbackSession.LatestSnapshot;
        CrashReportService.UpdatePlaybackSnapshot(snapshot);

        if (snapshot.Health != _lastReportedHealth ||
            !string.Equals(snapshot.LastError, _lastReportedHealthError, StringComparison.Ordinal) ||
            !string.Equals(snapshot.SourceId, _lastReportedHealthSourceId, StringComparison.OrdinalIgnoreCase))
        {
            _lastReportedHealth = snapshot.Health;
            _lastReportedHealthError = snapshot.LastError;
            _lastReportedHealthSourceId = snapshot.SourceId;
            _ = Dispatcher.BeginInvoke(() => UpdatePlaybackHealthText(snapshot), DispatcherPriority.Background);
        }

        if (snapshot.TimelineVersion > _lastConsumedTimelineVersion)
        {
            _lastConsumedTimelineVersion = snapshot.TimelineVersion;
            if (snapshot.Position is { } position)
            {
                bool isPlaying = snapshot.IsPlaying ?? true;
                if (shouldCheckPauseVisibility)
                {
                    _ = Dispatcher.BeginInvoke(async () =>
                    {
                        await ApplyPauseOverlayVisibilityRuleAsync(isSmtcSource, isPlaying);
                    }, DispatcherPriority.Background);
                }

                lock (_lyricGate)
                {
                    if (shouldSyncLyrics)
                    {
                        _lastSmtcPlaybackPositionSeconds = position.TotalSeconds;
                        SmtcLyricTimingSample timingSample = _smtcLyricTimingController.Update(position.TotalSeconds, isPlaying);
                        if (timingSample.IsDiscontinuity)
                        {
                            _diagnostic.Info(
                                $"SMTC lyric timing: raw={timingSample.RawPositionSeconds:F3}s, display={timingSample.DisplayPositionSeconds:F3}s, compensation={timingSample.CompensationSeconds:F3}s, discontinuity={timingSample.IsDiscontinuity}");
                        }
                    }
                    else if (shouldSyncNeteaseTimeline)
                    {
                        _neteaseLyricTimingController.UpdateFromPlayer(position.TotalSeconds, isPlaying);
                    }
                }
            }
        }

        if (shouldSyncLyrics || shouldSyncNeteaseLyrics)
        {
            string? earlyLine = null;
            lock (_lyricGate)
            {
                if (shouldSyncLyrics && _smtcLyricTimingController.GetCurrentDisplayPositionSeconds() is { } displayPosition)
                {
                    _lyricsService.SetPlaybackPosition(displayPosition);
                }
                else if (shouldSyncNeteaseLyrics)
                {
                    _lyricsService.SetPlaybackPosition(_neteaseLyricTimingController.GetCurrentPositionSeconds());
                }

                earlyLine = _lyricsService.UpdateCurrentLine();
            }

            if (earlyLine != null && !string.Equals(_lastLyricsPreviewLine, earlyLine, StringComparison.Ordinal))
            {
                _lastLyricsPreviewLine = earlyLine;
                _ = Dispatcher.BeginInvoke(() =>
                {
                    _overlayWindow.SetLyrics(earlyLine);
                    SetTextIfChanged(LyricsPreviewText, earlyLine);
                }, DispatcherPriority.Background);
            }
        }

        if (snapshot.TrackVersion > _lastConsumedTrackVersion)
        {
            _lastConsumedTrackVersion = snapshot.TrackVersion;
            _ = Dispatcher.BeginInvoke(async () =>
            {
                await RefreshCurrentTrackAsync(
                    showOverlay: false,
                    allowOverlayOnTrackChange: true,
                    snapshot.Track,
                    useSnapshot: true);
            }, DispatcherPriority.Background);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCurrentTrackAsync(showOverlay: true, allowOverlayOnTrackChange: false);
    }

    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        await PrevAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        await NextAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        await TogglePlayPauseAsync();
    }

    private async Task PrevAsync()
    {
        if (!await ExecutePlaybackCommandAsync(PlaybackCommand.Previous, "上一首"))
        {
            return;
        }

        await RefreshAfterControlAsync();
    }

    private async Task NextAsync()
    {
        if (!await ExecutePlaybackCommandAsync(PlaybackCommand.Next, "下一首"))
        {
            return;
        }

        await RefreshAfterControlAsync();
    }

    private async Task TogglePlayPauseAsync()
    {
        if (!await ExecutePlaybackCommandAsync(PlaybackCommand.TogglePlayPause, "播放/暂停"))
        {
            return;
        }

        SetStatus("状态：已发送播放/暂停指令。", false);
        await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
    }

    private void UpdatePlaybackHealthText(PlaybackSnapshot snapshot)
    {
        bool useSmtc = PlaybackCoordinator.IsSmtcSource(snapshot.SourceId);
        string status = snapshot.Health switch
        {
            PlaybackDataHealth.Healthy => useSmtc
                ? "已连接 SMTC"
                : "已连接网易云",
            PlaybackDataHealth.Degraded => "播放器数据已降级",
            PlaybackDataHealth.Unavailable => "播放器数据源不可用",
            _ => "正在连接播放器"
        };
        string detail = snapshot.Health switch
        {
            PlaybackDataHealth.Healthy when useSmtc =>
                "正在通过系统媒体会话同步。",
            PlaybackDataHealth.Healthy when _activeSettings.EnableNeteaseMemoryTimeline =>
                "快进快退后，歌词需要一段时间才能正常显示。",
            PlaybackDataHealth.Healthy =>
                "网易云歌词当前使用本地计时。",
            PlaybackDataHealth.Degraded when !useSmtc && _activeSettings.EnableNeteaseMemoryTimeline =>
                "内存时间轴暂不可用，歌词已自动使用本地计时。",
            PlaybackDataHealth.Degraded => snapshot.LastError ?? "播放器数据暂时不完整。",
            PlaybackDataHealth.Unavailable => snapshot.LastError ?? "请确认播放器正在运行。",
            _ => "等待首个播放器数据快照。"
        };

        SetTextIfChanged(ConnectionStatusText, status);
        SetTextIfChanged(ConnectionStatusSubText, detail);
        _nowPlayingViewModel.ConnectionStatus = status;
        _nowPlayingViewModel.ConnectionStatusDetail = detail;
    }

    private async Task<bool> ExecutePlaybackCommandAsync(PlaybackCommand command, string displayName)
    {
        PlaybackCommandResult result = await _playbackCoordinator.ExecuteAsync(command, _activeSettings);
        if (result.Succeeded)
        {
            return true;
        }

        if (result.Status == PlaybackCommandStatus.Unsupported)
        {
            SetStatus("状态：当前系统版本不支持 SMTC 控制。", false);
            return false;
        }

        SetStatus(PlaybackCoordinator.IsSmtcSource(result.SourceId)
            ? $"状态：SMTC 发送“{displayName}”失败。"
            : $"状态：发送“{displayName}”快捷键失败，请检查快捷键格式。", false);
        return false;
    }

    private async Task ToggleOverlayVisibilityAsync()
    {
        if (_overlayWindow.IsContentVisible)
        {
            await _overlayWindow.ConcealAsync();
            _overlayHiddenByPause = false;
            SetStatus("状态：已隐藏悬浮窗。", false);
            return;
        }

        _overlayHiddenByPause = false;
        await RefreshCurrentTrackAsync(showOverlay: true, allowOverlayOnTrackChange: false);
        if (_overlayWindow.IsContentVisible)
        {
            SetStatus("状态：已显示悬浮窗。", false);
        }
    }

    private async Task ApplyPauseOverlayVisibilityRuleAsync()
    {
        if (!_activeSettings.HideOverlayWhenPaused ||
            !IsSmtcSource() ||
            !_playbackCoordinator.SupportsSource(_activeSettings.TrackSource))
        {
            return;
        }

        PlaybackSnapshot snapshot = await _playbackSession.RefreshTimelineAsync();
        _lastConsumedTimelineVersion = Math.Max(
            _lastConsumedTimelineVersion,
            snapshot.TimelineVersion);
        if (snapshot.IsPlaying is { } isPlaying)
        {
            await ApplyPauseOverlayVisibilityRuleAsync(isSmtcSource: true, isPlaying);
        }
    }

    private async Task ApplyPauseOverlayVisibilityRuleAsync(bool isSmtcSource, bool? isPlaying)
    {
        if (OverlayVisibilityPolicy.ShouldRestoreWhenPlaybackResumed(_activeSettings.HideOverlayWhenPaused, isSmtcSource, isPlaying, _overlayHiddenByPause))
        {
            _overlayHiddenByPause = false;
            await RefreshCurrentTrackAsync(showOverlay: true, allowOverlayOnTrackChange: false);
            return;
        }

        if (!OverlayVisibilityPolicy.ShouldHideWhenPlaybackPaused(_activeSettings.HideOverlayWhenPaused, isSmtcSource, isPlaying))
        {
            if (isPlaying == true)
            {
                _overlayHiddenByPause = false;
            }

            return;
        }

        if (_overlayWindow.IsContentVisible)
        {
            await _overlayWindow.ConcealAsync();
            _overlayHiddenByPause = true;
            SetStatus("状态：音乐已暂停，悬浮窗已隐藏。", false);
        }
    }

    private bool IsSmtcSource()
    {
        return PlaybackCoordinator.IsSmtcSource(_activeSettings.TrackSource);
    }

    private async Task RefreshAfterControlAsync()
    {
        if (!IsSmtcSource())
        {
            await Task.Delay(650);
            await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: true);
            return;
        }

        string previousTrackKey = _lastTrackKey;

        for (int attempt = 1; attempt <= SmtcControlRefreshPolicy.MaxRefreshAttempts; attempt++)
        {
            await Task.Delay(SmtcControlRefreshPolicy.GetDelayMilliseconds(attempt));
            await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: true);

            if (SmtcControlRefreshPolicy.ShouldStopWaiting(previousTrackKey, _lastTrackKey, attempt))
            {
                return;
            }
        }
    }

    private async Task RefreshCurrentTrackAsync(
        bool showOverlay,
        bool allowOverlayOnTrackChange,
        TrackInfo? snapshotTrack = null,
        bool useSnapshot = false)
    {
        try
        {
            bool useSmtc = IsSmtcSource();
            if (useSmtc && !_playbackCoordinator.SupportsSource(_activeSettings.TrackSource))
            {
                SetStatus("状态：当前系统版本不支持 SMTC，请切换到网易云窗口标题模式。", false);
                return;
            }

            TrackInfo? track = snapshotTrack;
            if (!useSnapshot)
            {
                PlaybackSnapshot snapshot = await _playbackSession.RefreshTrackAsync();
                _lastConsumedTrackVersion = Math.Max(
                    _lastConsumedTrackVersion,
                    snapshot.TrackVersion);
                track = snapshot.Track;
            }

            if (track == null)
            {
                _smtcCoverRefreshCts?.Cancel();
                _smtcCoverRefreshCts = null;
                lock (_lyricGate)
                {
                    _lyricsService.Reset();
                    _lastSmtcPlaybackPositionSeconds = null;
                    _smtcLyricTimingController.Reset();
                    _neteaseLyricTimingController.Reset();
                }
                if (!string.IsNullOrEmpty(_lastLyricsPreviewLine))
                {
                    _lastLyricsPreviewLine = string.Empty;
                    _ = Dispatcher.BeginInvoke(() => _overlayWindow.SetLyrics(null), DispatcherPriority.Background);
                }

                if (!string.IsNullOrEmpty(_lastDisplayTrackKey))
                {
                    string fallbackTitle = useSmtc ? "未检测到系统媒体会话" : "未检测到网易云歌曲";
                    string fallbackArtist = useSmtc ? "请先播放任意媒体内容" : "请打开网易云音乐并播放歌曲";
                    string fallbackMeta = useSmtc
                        ? "来源：SMTC"
                        : $"来源：{NeteaseCoverDiagnosticPolicy.FormatSourceAppId("CloudMusic(ProcessTitle)", NeteaseCoverDiagnosticPolicy.WindowTitleMissing)}";

                    SetTextIfChanged(CurrentTitle, fallbackTitle);
                    SetTextIfChanged(CurrentArtist, fallbackArtist);
                    SetTextIfChanged(CurrentMeta, fallbackMeta);
                    SetTextIfChanged(FooterSourceText, CurrentMeta.Text);
                    _nowPlayingViewModel.Title = fallbackTitle;
                    _nowPlayingViewModel.Artist = fallbackArtist;
                    _nowPlayingViewModel.SourceText = fallbackMeta;
                    _nowPlayingViewModel.LyricsPreview = UiText.LyricsPreviewPlaceholder;
                    _lastLyricsPreviewLine = string.Empty;
                    SetTextIfChanged(LyricsPreviewText, UiText.LyricsPreviewPlaceholder);
                    if (!SameBytes(_lastPreviewCoverBytes, null))
                    {
                        SetCover(null);
                    }
                }

                SetStatusIfChanged(useSmtc ? "状态：未读取到 SMTC 媒体会话。" : "状态：未读取到网易云窗口标题。", false);
                _lastTrackKey = string.Empty;
                _lastDisplayTrackKey = string.Empty;
                return;
            }

            string currentTrackKey = useSmtc
                ? TrackIdentity.BuildTrackKey(track, includeSourceAppId: true)
                : TrackIdentity.BuildNeteaseTrackKey(track);
            bool changed = !string.Equals(_lastTrackKey, currentTrackKey, StringComparison.Ordinal);
            _lastTrackKey = currentTrackKey;
            bool shouldDelayImmediateSmtcCover = useSmtc
                && changed
                && SmtcCoverRefreshPolicy.ShouldDelayImmediateCoverUpdate(
                    trackChanged: true,
                    previousDisplayedCoverBytes: _lastPreviewCoverBytes,
                    currentCoverBytes: track.CoverBytes);
            bool shouldRetrySmtcCover = useSmtc && changed && (track.CoverBytes == null || shouldDelayImmediateSmtcCover);
            byte[]? immediateCoverBytes = useSmtc
                ? SmtcCoverRefreshPolicy.SelectImmediateCover(
                    changed,
                    _lastPreviewCoverBytes,
                    track.CoverBytes)
                : track.CoverBytes;

            bool displayChanged = !string.Equals(_lastDisplayTrackKey, currentTrackKey, StringComparison.Ordinal);
            if (displayChanged)
            {
                SetTextIfChanged(CurrentTitle, track.Name);
                SetTextIfChanged(CurrentArtist, track.Artist);
                SetTextIfChanged(CurrentMeta, $"来源：{track.SourceAppId}");
                SetTextIfChanged(FooterSourceText, CurrentMeta.Text);
                _nowPlayingViewModel.Title = track.Name;
                _nowPlayingViewModel.Artist = track.Artist;
                _nowPlayingViewModel.SourceText = $"来源：{track.SourceAppId}";
                if (!SameBytes(_lastPreviewCoverBytes, immediateCoverBytes))
                {
                    SetCover(immediateCoverBytes);
                }
                _lastDisplayTrackKey = currentTrackKey;
            }
            else if (!useSmtc && TrackDisplayPolicy.ShouldRefreshCoverForSameTrack(useSmtc, displayChanged, _lastPreviewCoverBytes, immediateCoverBytes))
            {
                SetTextIfChanged(CurrentMeta, $"来源：{track.SourceAppId}");
                SetTextIfChanged(FooterSourceText, CurrentMeta.Text);
                _nowPlayingViewModel.SourceText = $"来源：{track.SourceAppId}";
                if (!SameBytes(_lastPreviewCoverBytes, immediateCoverBytes))
                {
                    SetCover(immediateCoverBytes);
                    _overlayWindow.UpdateCoverIfChanged(immediateCoverBytes);
                }
            }

            if (shouldRetrySmtcCover)
            {
                _diagnostic.Info($"SMTC cover pending refresh: {currentTrackKey} (reason={(track.CoverBytes == null ? "missing" : "stale-suspected")})");
                StartSmtcCoverRefresh(currentTrackKey, track.CoverBytes);
            }
            else if (useSmtc && changed)
            {
                _smtcCoverRefreshCts?.Cancel();
                _smtcCoverRefreshCts = null;
            }

            if (showOverlay || (allowOverlayOnTrackChange && changed))
            {
                _diagnostic.Info($"Track changed: {track.Name} - {track.Artist} (source={track.SourceAppId})");
                _songDetectedTime = Environment.TickCount / 1000.0;
                BoostPolling();
                await _overlayWindow.ShowTrackAsync(CreateTrackWithCover(track, immediateCoverBytes));
            }

            if (SmtcLyricsSyncPolicy.ShouldUseExternalLyrics(_activeSettings.EnableLyrics, useSmtc))
            {
                double startTime = _smtcLyricTimingController.GetCurrentDisplayPositionSeconds()
                    ?? SmtcLyricsSyncPolicy.GetDisplayPlaybackPositionSeconds(
                        SmtcLyricsSyncPolicy.GetInitialPlaybackPositionSeconds(_lastSmtcPlaybackPositionSeconds));
                double duration = track.DurationSeconds;
                if (changed)
                {
                    lock (_lyricGate)
                    {
                        _lyricsService.Reset();
                    }
                    _ = Dispatcher.BeginInvoke(() => _overlayWindow.SetLyrics(null), DispatcherPriority.Background);
                    _lastLyricsPreviewLine = string.Empty;
                    SetTextIfChanged(LyricsPreviewText, UiText.LyricsPreviewPlaceholder);
                }

                if (changed || !_lyricsService.HasLyrics)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _lyricsService.FetchLyricsAsync(
                                track.Name,
                                track.Artist,
                                track.AlbumTitle,
                                startTime,
                                duration);
                            string? line = _lyricsService.GetCurrentLine();
                            _ = Dispatcher.BeginInvoke(() => UpdateLyricsPreviewAfterFetch(line), DispatcherPriority.Background);
                        }
                        catch
                        {
                            Dispatcher.Invoke(() =>
                            {
                                _overlayWindow.SetLyrics(null);
                                _lastLyricsPreviewLine = string.Empty;
                                SetTextIfChanged(LyricsPreviewText, "本次未获取到歌词内容。");
                            });
                        }
                    });
                }
            }
            else if (_activeSettings.EnableLyrics && !useSmtc)
            {
                if (changed)
                {
                    lock (_lyricGate)
                    {
                        _lyricsService.Reset();
                        _neteaseLyricTimingController.Reset();
                        _neteaseLyricTimingController.Start();
                    }
                    _ = Dispatcher.BeginInvoke(() => _overlayWindow.SetLyrics(null), DispatcherPriority.Background);
                    _lastLyricsPreviewLine = string.Empty;
                    SetTextIfChanged(LyricsPreviewText, UiText.LyricsPreviewPlaceholder);
                }
                else if (!_neteaseLyricTimingController.HasState)
                {
                    lock (_lyricGate)
                    {
                        _neteaseLyricTimingController.Start();
                    }
                }

                double startTime;
                lock (_lyricGate)
                {
                    startTime = _neteaseLyricTimingController.GetCurrentPositionSeconds();
                    _lyricsService.SetPlaybackPosition(startTime);
                }
                if (changed || !_lyricsService.HasLyrics)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _lyricsService.FetchLyricsAsync(
                                track.Name,
                                track.Artist,
                                track.AlbumTitle,
                                startTime,
                                track.DurationSeconds,
                                track.SongId);
                            string? line = _lyricsService.GetCurrentLine();
                            _ = Dispatcher.BeginInvoke(() => UpdateLyricsPreviewAfterFetch(line), DispatcherPriority.Background);
                        }
                        catch
                        {
                            Dispatcher.Invoke(() =>
                            {
                                _overlayWindow.SetLyrics(null);
                                _lastLyricsPreviewLine = string.Empty;
                                SetTextIfChanged(LyricsPreviewText, "本次未获取到歌词内容。");
                            });
                        }
                    });
                }
            }
            else
            {
                lock (_lyricGate)
                {
                    _lyricsService.Reset();
                    _lastSmtcPlaybackPositionSeconds = null;
                    _smtcLyricTimingController.Reset();
                    _neteaseLyricTimingController.Reset();
                }
                if (!string.IsNullOrEmpty(_lastLyricsPreviewLine))
                {
                    _lastLyricsPreviewLine = string.Empty;
                    _ = Dispatcher.BeginInvoke(() => _overlayWindow.SetLyrics(null), DispatcherPriority.Background);
                }
                SetTextIfChanged(LyricsPreviewText, UiText.LyricsPreviewPlaceholder);
            }

            SetStatusIfChanged(useSmtc ? "状态：已从 SMTC 同步。" : "状态：已从网易云窗口标题同步。", false);
            if (UiRefreshPolicy.ShouldRefreshTrackMetadata(changed, displayChanged, showOverlay))
            {
            SchedulePageMetaTextsUpdate();
        }
        }
        catch (Exception ex)
        {
            _diagnostic.Error("RefreshCurrentTrackAsync failed", ex);
            SetStatusIfChanged($"状态：读取歌曲数据失败。{ex.Message}", true);
        }
    }

    private void StartSmtcCoverRefresh(string expectedTrackKey, byte[]? pendingCoverBytes)
    {
        _smtcCoverRefreshCts?.Cancel();
        CancellationTokenSource cts = new();
        _smtcCoverRefreshCts = cts;

        _ = Task.Run(async () =>
        {
            int matchingCoverObservationCount = 0;

            for (int i = 0; i < 6; i++)
            {
                try
                {
                    await Task.Delay(220 * (i + 1), cts.Token);
                    PlaybackSnapshot refreshedSnapshot =
                        await _playbackSession.RefreshTrackAsync(cts.Token);
                    TrackInfo? refreshed = refreshedSnapshot.Track;
                    if (cts.IsCancellationRequested || refreshed == null)
                    {
                        return;
                    }

                    string candidateTrackKey = TrackIdentity.BuildTrackKey(refreshed, includeSourceAppId: true);
                    if (!string.Equals(candidateTrackKey, expectedTrackKey, StringComparison.Ordinal))
                    {
                        _diagnostic.Info($"SMTC cover refresh skipped because track changed again: {candidateTrackKey}");
                        return;
                    }

                    if (pendingCoverBytes is { Length: > 0 }
                        && refreshed.CoverBytes is { Length: > 0 }
                        && pendingCoverBytes.AsSpan().SequenceEqual(refreshed.CoverBytes))
                    {
                        matchingCoverObservationCount++;
                    }
                    else
                    {
                        matchingCoverObservationCount = 0;
                    }

                    if (!SmtcCoverRefreshPolicy.ShouldApplyRetriedCover(
                            expectedTrackKey,
                            candidateTrackKey,
                            pendingCoverBytes,
                            refreshed.CoverBytes,
                            matchingCoverObservationCount))
                    {
                        continue;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        if (!string.Equals(_lastDisplayTrackKey, expectedTrackKey, StringComparison.Ordinal))
                        {
                            return;
                        }

                        if (!SmtcCoverRefreshPolicy.ShouldUpdateDisplayedCover(
                                _lastPreviewCoverBytes,
                                refreshed.CoverBytes))
                        {
                            return;
                        }

                        _overlayWindow.UpdateCover(refreshed.CoverBytes);
                        SetCover(refreshed.CoverBytes);
                    });

                    _diagnostic.Info($"SMTC cover refresh applied: {expectedTrackKey}");
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _diagnostic.Warn($"SMTC cover refresh attempt failed: {ex.Message}");
                }
            }

            _diagnostic.Warn($"SMTC cover refresh timed out: {expectedTrackKey}");
        });
    }

    private void UpdateLyricsPreviewAfterFetch(string? line)
    {
        if (!_lyricsService.HasLyrics)
        {
            _overlayWindow.SetLyrics(null);
            _lastLyricsPreviewLine = string.Empty;
            SetTextIfChanged(LyricsPreviewText, "未获取到歌词。");
            _nowPlayingViewModel.LyricsPreview = "未获取到歌词。";
            return;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            _overlayWindow.SetLyrics(null);
            _lastLyricsPreviewLine = string.Empty;
            SetTextIfChanged(LyricsPreviewText, "歌词已获取，等待同步到当前时间点。");
            _nowPlayingViewModel.LyricsPreview = "歌词已获取，等待同步到当前时间点。";
            return;
        }

        _overlayWindow.SetLyrics(line);
        _lastLyricsPreviewLine = line;
        SetTextIfChanged(LyricsPreviewText, line);
        _nowPlayingViewModel.LyricsPreview = line;
    }

    private void SchedulePageMetaTextsUpdate()
    {
        _pageMetaUpdateDirty = true;
        _pageMetaUpdateTimer.Stop();
        _pageMetaUpdateTimer.Start();
    }

    private static void SetTextIfChanged(TextBlock textBlock, string value)
    {
        if (!string.Equals(textBlock.Text, value, StringComparison.Ordinal))
        {
            textBlock.Text = value;
        }
    }

    private void SetStatusIfChanged(string message, bool isError)
    {
        if (string.Equals(_lastStatusText, message, StringComparison.Ordinal) && _lastStatusIsError == isError)
        {
            return;
        }

        _lastStatusText = message;
        _lastStatusIsError = isError;
        SetStatus(message, isError);
    }

    private static bool SameBytes(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        return left.AsSpan().SequenceEqual(right);
    }

    private static TrackInfo CreateTrackWithCover(TrackInfo track, byte[]? coverBytes)
    {
        return new TrackInfo
        {
            Name = track.Name,
            Artist = track.Artist,
            Subtitle = track.Subtitle,
            AlbumTitle = track.AlbumTitle,
            SourceAppId = track.SourceAppId,
            SongId = track.SongId,
            DurationSeconds = track.DurationSeconds,
            CoverSource = track.CoverSource,
            CoverBytes = coverBytes
        };
    }

    private void SetCover(byte[]? coverBytes)
    {
        if (ReferenceEquals(_lastPreviewCoverBytes, coverBytes))
        {
            return;
        }

        _lastPreviewCoverBytes = coverBytes;

        if (coverBytes == null || coverBytes.Length == 0)
        {
            CoverPreview.Source = null;
            _nowPlayingViewModel.CoverImage = null;
            return;
        }

        try
        {
            BitmapImage image = new();

            using MemoryStream stream = new(coverBytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 300;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            CoverPreview.Source = image;
            _nowPlayingViewModel.CoverImage = image;
        }
        catch
        {
            CoverPreview.Source = null;
            _nowPlayingViewModel.CoverImage = null;
        }
    }

    private void SetStatus(string text, bool isError = false)
    {
        if (string.Equals(_lastStatusText, text, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatusText = text;
        StatusText.Text = text;
        if (isError)
        {
            return;
        }
    }
}
