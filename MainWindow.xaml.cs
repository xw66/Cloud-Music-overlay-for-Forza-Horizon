using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay;

[SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    private const int PollFastMs = 200;
    private const int PollSlowMs = 800;
    private const int PollBoostDurationMs = 3000;

    private readonly NeteaseLocalDataService _neteaseLocalDataService;
    private readonly SmtcTrackService _smtcTrackService;
    private readonly OverlaySettingsService _overlaySettingsService;
    private readonly OverlayWindow _overlayWindow;
    private readonly NeteaseShortcutSender _neteaseShortcutSender;
    private readonly GamepadInputService _gamepadInputService;
    private readonly UpdateService _updateService;
    private readonly DiagnosticService _diagnostic;
    private readonly NeteaseOfficialResolver _neteaseOfficialResolver;
    private readonly LyricsService _lyricsService;
    private readonly DispatcherTimer _pollTimer;

    private GlobalHotkeyService? _hotkeyService;
    private readonly ServiceLifecycle _lifecycle = new();
    private bool _isPolling;
    private bool _isInitializingOverlayControls;
    private OverlaySettings _activeSettings;
    private string _lastTrackKey = string.Empty;
    private string _lastDisplayTrackKey = string.Empty;
    private byte[]? _lastPreviewCoverBytes;
    private CancellationTokenSource? _smtcCoverRefreshCts;
    private string _lastStatusText = string.Empty;
    private double _songDetectedTime;
    private long _pollBoostUntil;
    private readonly Dictionary<TextBox, HashSet<GamepadButton>> _gamepadPressed = new();
    private readonly Dictionary<TextBox, DispatcherTimer> _gamepadCommitTimers = new();
    private bool _gamepadEnabledBeforeCapture;
    private int _gamepadCaptureFocusCount;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _isRealExit;
    private readonly bool _startHiddenToTray;
    private bool _startupInitialized;

    private static readonly (GamepadButton Button, string Token)[] GamepadTokenOrder =
    {
        (GamepadButton.LeftTrigger, "LT"),
        (GamepadButton.RightTrigger, "RT"),
        (GamepadButton.LeftShoulder, "LB"),
        (GamepadButton.RightShoulder, "RB"),
        (GamepadButton.DPadUp, "Up"),
        (GamepadButton.DPadDown, "Down"),
        (GamepadButton.DPadLeft, "Left"),
        (GamepadButton.DPadRight, "Right"),
        (GamepadButton.A, "A"),
        (GamepadButton.B, "B"),
        (GamepadButton.X, "X"),
        (GamepadButton.Y, "Y"),
        (GamepadButton.LeftThumb, "LS"),
        (GamepadButton.RightThumb, "RS"),
        (GamepadButton.Back, "Back"),
        (GamepadButton.Start, "Start")
    };

    public MainWindow(bool startHiddenToTray = false)
    {
        _isInitializingOverlayControls = true;
        _startHiddenToTray = startHiddenToTray;

        var coverCache = new CoverCacheService();
        _diagnostic = new DiagnosticService();
        _neteaseOfficialResolver = new NeteaseOfficialResolver(_diagnostic);
        _neteaseLocalDataService = new NeteaseLocalDataService(coverCache, _diagnostic, _neteaseOfficialResolver);
        _smtcTrackService = new SmtcTrackService(_diagnostic);
        _overlaySettingsService = new OverlaySettingsService();
        _overlayWindow = new OverlayWindow();
        _neteaseShortcutSender = new NeteaseShortcutSender();
        _gamepadInputService = new GamepadInputService();
        _updateService = new UpdateService();
        _lyricsService = new LyricsService(_diagnostic);
        OverlaySettings loadedSettings = _overlaySettingsService.Load();
        _activeSettings = loadedSettings;
        _overlayWindow.ApplySettings(loadedSettings);
        ApplyGamepadSettings(loadedSettings);

        InitializeComponent();
        InitializeTrayIcon();
        ApplyAutoWindowSize();
        ApplyRuntimeFeatureAvailability();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PollSlowMs)
        };
        _pollTimer.Tick += PollTimer_Tick;

        _gamepadInputService.PrevTriggered += async (_, _) => await PrevAsync();
        _gamepadInputService.NextTriggered += async (_, _) => await NextAsync();
        _gamepadInputService.ToggleTriggered += (_, _) => TogglePlayPause();

        _lifecycle.Register("GamepadInput",
            onStart: () => _gamepadInputService.Start(),
            onDispose: () => _gamepadInputService.Dispose());

        _lifecycle.Register("PollTimer",
            onStart: () => _pollTimer.Start(),
            onStop: () => _pollTimer.Stop());

        _lifecycle.Register("Hotkey",
            onDispose: () => _hotkeyService?.Dispose());

        _lifecycle.Register("UpdateService",
            onDispose: () => _updateService.Dispose());

        _lifecycle.Register("OverlayWindow",
            onDispose: () => _overlayWindow.Close());

        _lifecycle.Register("TrayIcon",
            onDispose: () => _trayIcon?.Dispose());

        _lifecycle.Register("Settings",
            onDispose: () =>
            {
                try { _overlaySettingsService.Save(_activeSettings); } catch { }
            });

        _lifecycle.Register("Diagnostic",
            onStart: () => _diagnostic.Enabled = _activeSettings.DiagnosticMode,
            onDispose: () => _diagnostic.Dispose());

        InitializeOverlayControls(loadedSettings);
        SetupHotkeyCaptureInputs();

        LoadEmbeddedResources();

        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private void ApplyAutoWindowSize()
    {
        var screen = SystemParameters.WorkArea;
        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double screenWidth = screen.Width / dpiScale;
        double screenHeight = screen.Height / dpiScale;

        double targetWidth = Math.Min(1280, screenWidth * 0.50);
        double targetHeight = Math.Min(910, screenHeight * 0.65);

        Width = Math.Max(920, targetWidth);
        Height = Math.Max(600, targetHeight);
    }

    private void LoadEmbeddedResources()
    {
        try
        {
            var iconUri = new Uri("pack://application:,,,/icon.ico");
            var iconStream = Application.GetResourceStream(iconUri)?.Stream;
            if (iconStream != null)
            {
                var decoder = BitmapDecoder.Create(
                    iconStream,
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
                Icon = decoder.Frames[0];
            }
        }
        catch { }

        try
        {
            var imgUri = new Uri("pack://application:,,,/Assets/Icons/icons8-github-50.png");
            var imgStream = Application.GetResourceStream(imgUri)?.Stream;
            if (imgStream != null && GitHubImage != null)
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = imgStream;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                GitHubImage.Source = img;
            }
        }
        catch { }
    }

    private void InitializeTrayIcon()
    {
        if (!RuntimeFeatureSupport.SupportsTrayIcon())
        {
            _trayIcon = null;
            return;
        }

        System.Drawing.Icon? icon = null;
        try
        {
            var uri = new Uri("pack://application:,,,/icon.ico");
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo?.Stream != null)
            {
                icon = new System.Drawing.Icon(streamInfo.Stream);
            }
        }
        catch
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath))
            {
                try { icon = new System.Drawing.Icon(iconPath); } catch { }
            }
        }

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "网易云悬浮窗",
            Visible = false
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("显示主窗口", null, (_, _) => RestoreFromTray());
        contextMenu.Items.Add("退出", null, (_, _) => RealExit());
        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
        }
    }

    private void RealExit()
    {
        _isRealExit = true;
        Close();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isRealExit || !_activeSettings.MinimizeToTray || _trayIcon == null)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(2000, "网易云悬浮窗", "已最小化到托盘，双击图标恢复。", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (_startupInitialized)
        {
            return;
        }

        _startupInitialized = true;
        bool ok = RebindGlobalHotkeys();
        SetStatus(ok
            ? "状态：快捷键已就绪，按应用快捷键会转发网易云快捷键。"
            : "状态：应用快捷键注册失败（被占用或格式无效）。", false);

        _lifecycle.StartAll();
        _ = RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
        _ = SilentCheckUpdateAsync();

        if (_startHiddenToTray)
        {
            BeginInvokeHideToTray();
        }
    }

    public void PrepareAutoStartToTray()
    {
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
    }

    private void BeginInvokeHideToTray()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            Hide();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = true;
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _lifecycle.Dispose();
    }

    private async void PollTimer_Tick(object? sender, EventArgs e)
    {
        if (_isPolling)
        {
            return;
        }

        _isPolling = true;
        try
        {
            await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: true);

            if (_activeSettings.EnableLyrics && IsSmtcSource())
            {
                var playbackState = await _smtcTrackService.GetPlaybackStateAsync();
                if (playbackState is { } state)
                {
                    _lyricsService.SetPlaybackPosition(state.Position.TotalSeconds);
                }

                string? newLine = _lyricsService.UpdateCurrentLine();
                if (newLine != null)
                {
                    Dispatcher.Invoke(() => _overlayWindow.SetLyrics(newLine));
                }
            }
        }
        finally
        {
            _isPolling = false;
            UpdatePollInterval();
        }
    }

    private void BoostPolling()
    {
        _pollBoostUntil = Environment.TickCount64 + PollBoostDurationMs;
        if (_pollTimer.Interval.TotalMilliseconds > PollFastMs)
        {
            _pollTimer.Interval = TimeSpan.FromMilliseconds(PollFastMs);
        }
    }

    private void UpdatePollInterval()
    {
        int target = Environment.TickCount64 < _pollBoostUntil ? PollFastMs : PollSlowMs;
        if (Math.Abs(_pollTimer.Interval.TotalMilliseconds - target) > 1)
        {
            _pollTimer.Interval = TimeSpan.FromMilliseconds(target);
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

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private async Task PrevAsync()
    {
        if (IsSmtcSource())
        {
            if (!RuntimeFeatureSupport.SupportsSmtc())
            {
                SetStatus("状态：当前系统版本不支持 SMTC 控制。", false);
                return;
            }

            bool ok = await _smtcTrackService.PreviousAsync();
            if (!ok)
            {
                SetStatus("状态：SMTC 发送“上一首”失败。", false);
                return;
            }
        }
        else
        {
            bool ok = _neteaseShortcutSender.Send(_activeSettings.NeteasePrevHotkey);
            if (!ok)
            {
                SetStatus("状态：发送“上一首”快捷键失败，请检查快捷键格式。", false);
                return;
            }
        }

        await RefreshAfterControlAsync();
    }

    private async Task NextAsync()
    {
        if (IsSmtcSource())
        {
            if (!RuntimeFeatureSupport.SupportsSmtc())
            {
                SetStatus("状态：当前系统版本不支持 SMTC 控制。", false);
                return;
            }

            bool ok = await _smtcTrackService.NextAsync();
            if (!ok)
            {
                SetStatus("状态：SMTC 发送“下一首”失败。", false);
                return;
            }
        }
        else
        {
            bool ok = _neteaseShortcutSender.Send(_activeSettings.NeteaseNextHotkey);
            if (!ok)
            {
                SetStatus("状态：发送“下一首”快捷键失败，请检查快捷键格式。", false);
                return;
            }
        }

        await RefreshAfterControlAsync();
    }

    private async void TogglePlayPause()
    {
        if (IsSmtcSource())
        {
            if (!RuntimeFeatureSupport.SupportsSmtc())
            {
                SetStatus("状态：当前系统版本不支持 SMTC 控制。", false);
                return;
            }

            bool ok = await _smtcTrackService.TogglePlayPauseAsync();
            if (!ok)
            {
                SetStatus("状态：SMTC 发送“播放/暂停”失败。", false);
                return;
            }
        }
        else
        {
            bool ok = _neteaseShortcutSender.Send(_activeSettings.NeteaseToggleHotkey);
            if (!ok)
            {
                SetStatus("状态：发送“播放/暂停”快捷键失败，请检查快捷键格式。", false);
                return;
            }
        }

        SetStatus("状态：已发送播放/暂停指令。", false);
        await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
    }

    private bool IsSmtcSource()
    {
        return string.Equals(_activeSettings.TrackSource, "SMTC", StringComparison.OrdinalIgnoreCase);
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

    private async Task RefreshCurrentTrackAsync(bool showOverlay, bool allowOverlayOnTrackChange)
    {
        try
        {
            bool useSmtc = string.Equals(_activeSettings.TrackSource, "SMTC", StringComparison.OrdinalIgnoreCase);
            if (useSmtc && !RuntimeFeatureSupport.SupportsSmtc())
            {
                SetStatus("状态：当前系统版本不支持 SMTC，请切换到网易云窗口标题模式。", false);
                return;
            }

            TrackInfo? track = useSmtc
                ? await _smtcTrackService.GetCurrentTrackAsync()
                : await _neteaseLocalDataService.GetCurrentTrackAsync();

            if (track == null)
            {
                _smtcCoverRefreshCts?.Cancel();
                _smtcCoverRefreshCts = null;
                _lyricsService.Reset();
                Dispatcher.Invoke(() => _overlayWindow.SetLyrics(null));

                if (!string.IsNullOrEmpty(_lastDisplayTrackKey))
                {
                    CurrentTitle.Text = useSmtc ? "未检测到系统媒体会话" : "未检测到网易云歌曲";
                    CurrentArtist.Text = useSmtc ? "请先播放任意媒体内容" : "请打开网易云音乐并播放歌曲";
                    CurrentMeta.Text = useSmtc ? "来源：SMTC" : "来源：CloudMusic(ProcessTitle)";
                    SetCover(null);
                }

                SetStatus(useSmtc ? "状态：未读取到 SMTC 媒体会话。" : "状态：未读取到网易云窗口标题。", false);
                _lastTrackKey = string.Empty;
                _lastDisplayTrackKey = string.Empty;
                return;
            }

            string currentTrackKey = TrackIdentity.BuildTrackKey(track, includeSourceAppId: useSmtc);
            bool changed = !string.Equals(_lastTrackKey, currentTrackKey, StringComparison.Ordinal);
            _lastTrackKey = currentTrackKey;
            bool shouldDelayImmediateSmtcCover = useSmtc
                && changed
                && SmtcCoverRefreshPolicy.ShouldDelayImmediateCoverUpdate(
                    trackChanged: true,
                    previousDisplayedCoverBytes: _lastPreviewCoverBytes,
                    currentCoverBytes: track.CoverBytes);
            bool shouldRetrySmtcCover = useSmtc && changed && (track.CoverBytes == null || shouldDelayImmediateSmtcCover);
            byte[]? immediateCoverBytes = shouldDelayImmediateSmtcCover ? null : track.CoverBytes;

            bool displayChanged = !string.Equals(_lastDisplayTrackKey, currentTrackKey, StringComparison.Ordinal);
            if (displayChanged)
            {
                CurrentTitle.Text = track.Name;
                CurrentArtist.Text = track.Artist;
                CurrentMeta.Text = $"来源：{track.SourceAppId}";
                SetCover(immediateCoverBytes);
                _lastDisplayTrackKey = currentTrackKey;
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

            if (_activeSettings.EnableLyrics && useSmtc)
            {
                double startTime = _songDetectedTime;
                double duration = track.DurationSeconds;
                if (changed)
                {
                    _lyricsService.Reset();
                    Dispatcher.Invoke(() => _overlayWindow.SetLyrics(null));
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                    await _lyricsService.FetchLyricsAsync(
                        track.Name,
                        track.Artist,
                        startTime,
                        duration);
                        string? line = _lyricsService.GetCurrentLine();
                        Dispatcher.Invoke(() => _overlayWindow.SetLyrics(line));
                    }
                    catch
                    {
                        Dispatcher.Invoke(() => _overlayWindow.SetLyrics(null));
                    }
                });
            }
            else if (!useSmtc)
            {
                _lyricsService.Reset();
                Dispatcher.Invoke(() => _overlayWindow.SetLyrics(null));
            }

            SetStatus(useSmtc ? "状态：已从 SMTC 同步。" : "状态：已从网易云窗口标题同步。", false);
        }
        catch (Exception ex)
        {
            _diagnostic.Error("RefreshCurrentTrackAsync failed", ex);
            SetStatus($"状态：读取歌曲数据失败。{ex.Message}", true);
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
                    TrackInfo? refreshed = await _smtcTrackService.GetCurrentTrackAsync();
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

                        SetCover(refreshed.CoverBytes);
                        _overlayWindow.UpdateCover(refreshed.CoverBytes);
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

    private static TrackInfo CreateTrackWithCover(TrackInfo track, byte[]? coverBytes)
    {
        return new TrackInfo
        {
            Name = track.Name,
            Artist = track.Artist,
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
        }
        catch
        {
            CoverPreview.Source = null;
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

        if (_isPolling)
        {
            return;
        }
    }

    private void InitializeOverlayControls(OverlaySettings settings)
    {
        _isInitializingOverlayControls = true;
        try
        {
            SelectTrackSource(settings.TrackSource);
            HorizontalSlider.Value = settings.LeftPercent * 100.0;
            BottomOffsetSlider.Value = settings.TopPercent * 100.0;
            ScaleSlider.Value = settings.Scale * 100.0;
            AppPrevHotkeyBox.Text = settings.AppPrevHotkey;
            AppNextHotkeyBox.Text = settings.AppNextHotkey;
            AppToggleHotkeyBox.Text = settings.AppToggleHotkey;
            NeteasePrevHotkeyBox.Text = settings.NeteasePrevHotkey;
            NeteaseNextHotkeyBox.Text = settings.NeteaseNextHotkey;
            NeteaseToggleHotkeyBox.Text = settings.NeteaseToggleHotkey;
            EnableGamepadCheckBox.IsChecked = settings.EnableGamepadHotkeys;
            GamepadPrevHotkeyBox.Text = settings.GamepadPrevHotkey;
            GamepadNextHotkeyBox.Text = settings.GamepadNextHotkey;
            GamepadToggleHotkeyBox.Text = settings.GamepadToggleHotkey;
            MinimizeToTrayCheckBox.IsChecked = settings.MinimizeToTray;
            AutoStartCheckBox.IsChecked = settings.AutoStartOnBoot;
            AlwaysShowCheckBox.IsChecked = settings.AlwaysShowOverlay;
            DiagnosticCheckBox.IsChecked = settings.DiagnosticMode;
            EnableLyricsCheckBox.IsChecked = settings.EnableLyrics;
            EnableCoverWingEffectCheckBox.IsChecked = settings.EnableCoverWingEffect;
            SelectTitleColor(settings.TitleColor);
            SelectArtistColor(settings.ArtistColor);
            SelectLyricsColor(settings.LyricsColor);
            TitleOpacitySlider.Value = settings.TitleOpacity * 100.0;
            ArtistOpacitySlider.Value = settings.ArtistOpacity * 100.0;
            LyricsOpacitySlider.Value = settings.LyricsOpacity * 100.0;
            ApplyDisplayColors(settings);
            UpdateOverlayControlLabels();
            UpdateRuntimeDependentControls();
        }
        finally
        {
            _isInitializingOverlayControls = false;
        }
    }

    private void HorizontalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls)
        {
            return;
        }

        ApplyOverlaySettingsFromControls();
    }

    private void BottomOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls)
        {
            return;
        }

        ApplyOverlaySettingsFromControls();
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls)
        {
            return;
        }

        ApplyOverlaySettingsFromControls();
    }

    private async void TrackSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingOverlayControls)
        {
            return;
        }

        ApplyOverlaySettingsFromControls();
        UpdateRuntimeDependentControls();
        _smtcCoverRefreshCts?.Cancel();
        _smtcCoverRefreshCts = null;
        _lyricsService.Reset();
        _lastTrackKey = string.Empty;
        _lastDisplayTrackKey = string.Empty;
        _lastPreviewCoverBytes = null;
        SetCover(null);
        _overlayWindow.SetLyrics(null);

        await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
        SetStatus("状态：数据来源已切换（点击“保存”可持久化）。", false);
    }

    private void SaveOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyOverlaySettingsFromControls();

            if (!AreHotkeysValid(_activeSettings))
            {
                SetStatus("状态：快捷键格式无效，示例 Ctrl+Shift+Left。", true);
                return;
            }

            bool rebound = RebindGlobalHotkeys();
            if (!rebound)
            {
                SetStatus("状态：应用快捷键注册失败，请换一组按键后再保存。", true);
                return;
            }

            _overlaySettingsService.Save(_activeSettings);
            SetStatus($"状态：已保存并应用。{_overlaySettingsService.SettingsFilePath}", false);
        }
        catch (Exception ex)
        {
            SetStatus($"状态：保存失败。{ex.Message}", true);
        }
    }

    private void SettingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
        _diagnostic.Enabled = _activeSettings.DiagnosticMode;
        _overlaySettingsService.Save(_activeSettings);
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
        _overlaySettingsService.Save(_activeSettings);
    }

    private void ResetOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
        _activeSettings = new OverlaySettings();
        _overlayWindow.ApplySettings(_activeSettings);
        InitializeOverlayControls(_activeSettings);
        SetStatus("状态：已重置为默认值，点击“保存”后生效并持久化。", false);
    }

    private async void TestOverlay_Click(object sender, RoutedEventArgs e)
    {
        (string Name, string Artist, Color Color)[] previewTracks =
        [
            ("有些", "颜人中", Color.FromRgb(39, 94, 113)),
            ("风景", "测试歌手", Color.FromRgb(112, 58, 72)),
            ("夜航", "测试歌手", Color.FromRgb(58, 86, 131)),
            ("岛屿", "测试歌手", Color.FromRgb(82, 112, 70)),
            ("回声", "测试歌手", Color.FromRgb(130, 88, 48))
        ];

        foreach ((string name, string artist, Color color) in previewTracks)
        {
            TrackInfo previewTrack = new()
            {
                Name = name,
                Artist = artist,
                SourceAppId = "预览",
                CoverBytes = CreatePreviewCoverBytes(name, artist, color)
            };

            await _overlayWindow.ShowTrackAsync(previewTrack);
            await Task.Delay(450);
        }

        SetStatus("状态：已显示悬浮窗预览。", false);
    }

    private static byte[] CreatePreviewCoverBytes(string title, string artist, Color accent)
    {
        const int size = 256;
        DrawingVisual visual = new();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(
                Color.FromRgb(16, 20, 24),
                accent,
                new Point(0, 0),
                new Point(1, 1));
            dc.DrawRectangle(background, null, new Rect(0, 0, size, size));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(58, 255, 255, 255)), null, new Point(56, 46), 86, 54);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(44, 0, 0, 0)), null, new Point(204, 196), 92, 82);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(58, 0, 0, 0)), null, new Rect(0, 168, size, 88));

            var titleText = new FormattedText(
                title,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"),
                46,
                Brushes.White,
                1.0);
            var artistText = new FormattedText(
                artist,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"),
                24,
                new SolidColorBrush(Color.FromRgb(220, 230, 240)),
                1.0);

            dc.DrawText(titleText, new Point(24, 130));
            dc.DrawText(artistText, new Point(24, 190));
        }

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private async Task SilentCheckUpdateAsync()
    {
        try
        {
            UpdateInfo info = await _updateService.CheckForUpdateAsync();
            if (info.HasUpdate && string.IsNullOrEmpty(info.ErrorMessage))
            {
                SetStatus($"状态：发现新版本 {info.LatestVersion}，点击“检查更新”查看。", false);
            }
        }
        catch
        {
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("状态：正在检查更新…", false);
        UpdateInfo info = await _updateService.CheckForUpdateAsync();

        if (!string.IsNullOrEmpty(info.ErrorMessage))
        {
            SetStatus($"状态：{info.ErrorMessage}", true);
            return;
        }

        if (!info.HasUpdate)
        {
            string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            SetStatus($"状态：已是最新版本（v{ver}）。", false);
            return;
        }

        string msg = $"发现新版本 {info.LatestVersion}！\n\n";
        if (!string.IsNullOrEmpty(info.ReleaseNotes))
        {
            msg += $"更新内容：\n{info.ReleaseNotes}\n\n";
        }
        msg += "是否前往 GitHub 下载？";

        var result = System.Windows.MessageBox.Show(msg, "发现更新",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = info.DownloadUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                SetStatus("状态：无法打开下载链接。", true);
            }
        }
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/xw66/Cloud-Music-overlay-for-Forza-Horizon",
                    UseShellExecute = true
                });
        }
        catch
        {
            SetStatus("状态：无法打开浏览器。", true);
        }
    }

    private void BaiduPanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://pan.baidu.com/s/1bWOQYrgYVhHSWJTunLNhBA?pwd=6666",
                    UseShellExecute = true
                });
        }
        catch
        {
            SetStatus("状态：无法打开浏览器。", true);
        }
    }

    private void SetupHotkeyCaptureInputs()
    {
        ConfigureKeyboardHotkeyInput(AppPrevHotkeyBox);
        ConfigureKeyboardHotkeyInput(AppNextHotkeyBox);
        ConfigureKeyboardHotkeyInput(AppToggleHotkeyBox);
        ConfigureKeyboardHotkeyInput(NeteasePrevHotkeyBox);
        ConfigureKeyboardHotkeyInput(NeteaseNextHotkeyBox);
        ConfigureKeyboardHotkeyInput(NeteaseToggleHotkeyBox);

        ConfigureGamepadHotkeyInput(GamepadPrevHotkeyBox);
        ConfigureGamepadHotkeyInput(GamepadNextHotkeyBox);
        ConfigureGamepadHotkeyInput(GamepadToggleHotkeyBox);
    }

    private void ConfigureKeyboardHotkeyInput(TextBox textBox)
    {
        textBox.PreviewKeyDown += KeyboardHotkeyInput_PreviewKeyDown;
    }

    private void KeyboardHotkeyInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab)
        {
            return;
        }

        e.Handled = true;

        if (key == Key.Back || key == Key.Delete)
        {
            textBox.Clear();
            return;
        }

        string? keyToken = KeyToToken(key);
        if (string.IsNullOrWhiteSpace(keyToken))
        {
            return;
        }

        bool isModifierKey = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
        if (isModifierKey)
        {
            return;
        }

        string captured = FormatKeyboardCombination(Keyboard.Modifiers, keyToken);
        if (!string.IsNullOrWhiteSpace(captured))
        {
            textBox.Text = captured;
            textBox.CaretIndex = textBox.Text.Length;
        }
    }

    private void ConfigureGamepadHotkeyInput(TextBox textBox)
    {
        textBox.IsReadOnly = true;
        textBox.GotKeyboardFocus += GamepadHotkeyInput_GotKeyboardFocus;
        textBox.LostKeyboardFocus += GamepadHotkeyInput_LostKeyboardFocus;
    }

    private void GamepadHotkeyInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (!_gamepadCommitTimers.TryGetValue(textBox, out DispatcherTimer? timer))
        {
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(45)
            };
            timer.Tick += (_, _) => CaptureGamepadInput(textBox);
            _gamepadCommitTimers[textBox] = timer;
        }

        if (_gamepadCaptureFocusCount == 0)
        {
            _gamepadEnabledBeforeCapture = _gamepadInputService.Enabled;
        }

        _gamepadCaptureFocusCount++;
        _gamepadInputService.Enabled = false;
        timer.Start();
    }

    private void GamepadHotkeyInput_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (_gamepadCommitTimers.TryGetValue(textBox, out DispatcherTimer? timer))
        {
            timer.Stop();
        }

        _gamepadPressed.Remove(textBox);

        _gamepadCaptureFocusCount = Math.Max(0, _gamepadCaptureFocusCount - 1);
        if (_gamepadCaptureFocusCount == 0)
        {
            _gamepadInputService.Enabled = _gamepadEnabledBeforeCapture;
        }
    }

    private void CaptureGamepadInput(TextBox textBox)
    {
        GamepadButton currentButtons = GamepadInputService.GetCurrentButtonsSnapshot();
        if (currentButtons == GamepadButton.None)
        {
            return;
        }

        if (!_gamepadPressed.TryGetValue(textBox, out HashSet<GamepadButton>? pressed))
        {
            pressed = new HashSet<GamepadButton>();
            _gamepadPressed[textBox] = pressed;
        }

        pressed.Clear();
        foreach (var (button, _) in GamepadTokenOrder)
        {
            if ((currentButtons & button) == button)
            {
                pressed.Add(button);
            }
        }

        string captured = FormatGamepadCombination(pressed);
        if (!string.IsNullOrWhiteSpace(captured))
        {
            textBox.Text = captured;
            textBox.CaretIndex = textBox.Text.Length;
        }
    }

    private static string FormatKeyboardCombination(ModifierKeys modifiers, string mainKeyToken)
    {
        List<string> tokens = new();

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            tokens.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            tokens.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            tokens.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            tokens.Add("Win");
        }

        if (!string.IsNullOrWhiteSpace(mainKeyToken))
        {
            tokens.Add(mainKeyToken);
        }

        return string.Join("+", tokens);
    }

    private static string FormatGamepadCombination(HashSet<GamepadButton> pressed)
    {
        List<string> tokens = new();
        foreach (var (button, token) in GamepadTokenOrder)
        {
            if (pressed.Contains(button))
            {
                tokens.Add(token);
            }
        }

        return string.Join("+", tokens);
    }

    private static string? KeyToToken(Key key)
    {
        return key switch
        {
            Key.LeftCtrl or Key.RightCtrl => "Ctrl",
            Key.LeftAlt or Key.RightAlt => "Alt",
            Key.LeftShift or Key.RightShift => "Shift",
            Key.LWin or Key.RWin => "Win",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Space => "Space",
            Key.Oem1 => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.Oem2 => "/",
            Key.Oem5 => "\\",
            Key.OemOpenBrackets => "[",
            Key.Oem6 => "]",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.Oem3 => "`",
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
            >= Key.F1 and <= Key.F12 => key.ToString(),
            _ => null
        };
    }

    private void SelectTrackSource(string trackSource)
    {
        if (TrackSourceComboBox == null)
        {
            return;
        }

        foreach (object item in TrackSourceComboBox.Items)
        {
            if (item is ComboBoxItem combo &&
                combo.Tag is string tag &&
                string.Equals(tag, trackSource, StringComparison.OrdinalIgnoreCase))
            {
                TrackSourceComboBox.SelectedItem = combo;
                return;
            }
        }

        TrackSourceComboBox.SelectedIndex = 0;
    }

    private void ApplyRuntimeFeatureAvailability()
    {
        if (!RuntimeFeatureSupport.SupportsSmtc() &&
            string.Equals(_activeSettings.TrackSource, "SMTC", StringComparison.OrdinalIgnoreCase))
        {
            _activeSettings.TrackSource = "NeteaseProcess";
        }

        UpdateRuntimeDependentControls();
    }

    private void UpdateRuntimeDependentControls()
    {
        if (TrackSourceComboBox == null || EnableLyricsCheckBox == null)
        {
            return;
        }

        bool smtcSupported = RuntimeFeatureSupport.SupportsSmtc();
        ComboBoxItem? smtcItem = null;
        foreach (object item in TrackSourceComboBox.Items)
        {
            if (item is ComboBoxItem combo &&
                string.Equals(combo.Tag as string, "SMTC", StringComparison.OrdinalIgnoreCase))
            {
                smtcItem = combo;
                break;
            }
        }

        if (smtcItem != null)
        {
            smtcItem.IsEnabled = smtcSupported;
            smtcItem.ToolTip = smtcSupported
                ? "使用系统媒体会话读取歌曲信息"
                : "当前系统版本不支持 SMTC";
        }

        if (!smtcSupported && string.Equals(GetSelectedTrackSource(), "SMTC", StringComparison.OrdinalIgnoreCase))
        {
            SelectTrackSource("NeteaseProcess");
        }

        bool useSmtc = smtcSupported &&
            string.Equals(GetSelectedTrackSource(), "SMTC", StringComparison.OrdinalIgnoreCase);

        EnableLyricsCheckBox.IsEnabled = useSmtc;
        EnableLyricsCheckBox.ToolTip = TrackSourcePolicy.GetLyricsTooltip(useSmtc);
    }

    private string GetSelectedTrackSource()
    {
        if (TrackSourceComboBox?.SelectedItem is ComboBoxItem combo && combo.Tag is string tag)
        {
            return tag;
        }

        return "NeteaseProcess";
    }

    private void ApplyOverlaySettingsFromControls()
    {
        if (HorizontalSlider == null || BottomOffsetSlider == null || ScaleSlider == null)
        {
            return;
        }

        OverlaySettings settings = new()
        {
            TrackSource = GetSelectedTrackSource(),
            LeftPercent = HorizontalSlider.Value / 100.0,
            TopPercent = BottomOffsetSlider.Value / 100.0,
            Scale = ScaleSlider.Value / 100.0,
            AppPrevHotkey = AppPrevHotkeyBox.Text.Trim(),
            AppNextHotkey = AppNextHotkeyBox.Text.Trim(),
            AppToggleHotkey = AppToggleHotkeyBox.Text.Trim(),
            NeteasePrevHotkey = NeteasePrevHotkeyBox.Text.Trim(),
            NeteaseNextHotkey = NeteaseNextHotkeyBox.Text.Trim(),
            NeteaseToggleHotkey = NeteaseToggleHotkeyBox.Text.Trim(),
            EnableGamepadHotkeys = EnableGamepadCheckBox.IsChecked == true,
            GamepadPrevHotkey = GamepadPrevHotkeyBox.Text.Trim(),
            GamepadNextHotkey = GamepadNextHotkeyBox.Text.Trim(),
            GamepadToggleHotkey = GamepadToggleHotkeyBox.Text.Trim(),
            MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true,
            AutoStartOnBoot = AutoStartCheckBox.IsChecked == true,
            AlwaysShowOverlay = AlwaysShowCheckBox.IsChecked == true,
            DiagnosticMode = DiagnosticCheckBox.IsChecked == true,
            EnableLyrics = EnableLyricsCheckBox.IsChecked == true,
            EnableCoverWingEffect = EnableCoverWingEffectCheckBox.IsChecked == true,
            TitleColor = GetSelectedTitleColor(),
            ArtistColor = GetSelectedArtistColor(),
            LyricsColor = GetSelectedLyricsColor(),
            TitleOpacity = TitleOpacitySlider.Value / 100.0,
            ArtistOpacity = ArtistOpacitySlider.Value / 100.0,
            LyricsOpacity = LyricsOpacitySlider.Value / 100.0
        };

        _activeSettings = settings;
        _overlayWindow.ApplySettings(settings);
        ApplyGamepadSettings(settings);
        ApplyAutoStart(settings.AutoStartOnBoot);
        ApplyDisplayColors(settings);
        UpdateOverlayControlLabels();
    }

    private void UpdateOverlayControlLabels()
    {
        if (HorizontalValueText == null || BottomOffsetValueText == null || ScaleValueText == null)
        {
            return;
        }

        HorizontalValueText.Text = $"{HorizontalSlider.Value:0}%";
        BottomOffsetValueText.Text = $"{BottomOffsetSlider.Value:0}%";
        ScaleValueText.Text = $"{ScaleSlider.Value:0}%";
        TitleOpacityValueText.Text = $"{TitleOpacitySlider.Value:0}%";
        ArtistOpacityValueText.Text = $"{ArtistOpacitySlider.Value:0}%";
        LyricsOpacityValueText.Text = $"{LyricsOpacitySlider.Value:0}%";
    }

    private void SelectTitleColor(string color)
    {
        color = color.ToUpperInvariant();
        TitleColor_White.IsChecked = color == "#FFFFFF";
        TitleColor_Light.IsChecked = color == "#D0E0F0";
        TitleColor_Yellow.IsChecked = color == "#F0E080";
        TitleColor_Green.IsChecked = color == "#90EE90";
        TitleColor_Orange.IsChecked = color == "#FFB366";
    }

    private void SelectArtistColor(string color)
    {
        color = color.ToUpperInvariant();
        ArtistColor_Light.IsChecked = color == "#C0D0E0";
        ArtistColor_White.IsChecked = color == "#FFFFFF";
        ArtistColor_Yellow.IsChecked = color == "#F0E080";
        ArtistColor_Green.IsChecked = color == "#90EE90";
        ArtistColor_Orange.IsChecked = color == "#FFB366";
    }

    private string GetSelectedTitleColor()
    {
        if (TitleColor_White.IsChecked == true) return "#FFFFFF";
        if (TitleColor_Light.IsChecked == true) return "#D0E0F0";
        if (TitleColor_Yellow.IsChecked == true) return "#F0E080";
        if (TitleColor_Green.IsChecked == true) return "#90EE90";
        if (TitleColor_Orange.IsChecked == true) return "#FFB366";
        return "#FFFFFF";
    }

    private string GetSelectedArtistColor()
    {
        if (ArtistColor_Light.IsChecked == true) return "#C0D0E0";
        if (ArtistColor_White.IsChecked == true) return "#FFFFFF";
        if (ArtistColor_Yellow.IsChecked == true) return "#F0E080";
        if (ArtistColor_Green.IsChecked == true) return "#90EE90";
        if (ArtistColor_Orange.IsChecked == true) return "#FFB366";
        return "#C0D0E0";
    }

    private void SelectLyricsColor(string color)
    {
        color = color.ToUpperInvariant();
        LyricsColor_Light.IsChecked = color == "#A0B8D0";
        LyricsColor_White.IsChecked = color == "#FFFFFF";
        LyricsColor_Yellow.IsChecked = color == "#F0E080";
        LyricsColor_Green.IsChecked = color == "#90EE90";
        LyricsColor_Orange.IsChecked = color == "#FFB366";
    }

    private string GetSelectedLyricsColor()
    {
        if (LyricsColor_Light.IsChecked == true) return "#A0B8D0";
        if (LyricsColor_White.IsChecked == true) return "#FFFFFF";
        if (LyricsColor_Yellow.IsChecked == true) return "#F0E080";
        if (LyricsColor_Green.IsChecked == true) return "#90EE90";
        if (LyricsColor_Orange.IsChecked == true) return "#FFB366";
        return "#A0B8D0";
    }

    private void ApplyDisplayColors(OverlaySettings settings)
    {
        try
        {
            var titleColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.TitleColor);
            CurrentTitle.Foreground = new System.Windows.Media.SolidColorBrush(titleColor);
            CurrentTitle.Opacity = settings.TitleOpacity;
        }
        catch { }

        try
        {
            var artistColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.ArtistColor);
            CurrentArtist.Foreground = new System.Windows.Media.SolidColorBrush(artistColor);
            CurrentArtist.Opacity = settings.ArtistOpacity;
        }
        catch { }
    }

    private void TitleColor_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private void ArtistColor_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private void TitleOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private void ArtistOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private void LyricsColor_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private void LyricsOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private bool RebindGlobalHotkeys()
    {
        _hotkeyService?.Dispose();

        _hotkeyService = new GlobalHotkeyService(this);
        _hotkeyService.NextRequested += async (_, _) => await NextAsync();
        _hotkeyService.PrevRequested += async (_, _) => await PrevAsync();
        _hotkeyService.TogglePlayPauseRequested += (_, _) => TogglePlayPause();

        return _hotkeyService.Register(_activeSettings.AppPrevHotkey, _activeSettings.AppNextHotkey, _activeSettings.AppToggleHotkey);
    }

    private static bool AreHotkeysValid(OverlaySettings settings)
    {
        bool keyboardOk = HotkeyParser.TryParse(settings.AppPrevHotkey, out _) &&
                          HotkeyParser.TryParse(settings.AppNextHotkey, out _) &&
                          HotkeyParser.TryParse(settings.AppToggleHotkey, out _) &&
                          HotkeyParser.TryParse(settings.NeteasePrevHotkey, out _) &&
                          HotkeyParser.TryParse(settings.NeteaseNextHotkey, out _) &&
                          HotkeyParser.TryParse(settings.NeteaseToggleHotkey, out _);

        if (!keyboardOk)
        {
            return false;
        }

        if (!settings.EnableGamepadHotkeys)
        {
            return true;
        }

        return GamepadHotkeyParser.TryParse(settings.GamepadPrevHotkey, out _) &&
               GamepadHotkeyParser.TryParse(settings.GamepadNextHotkey, out _) &&
               GamepadHotkeyParser.TryParse(settings.GamepadToggleHotkey, out _);
    }

    private void ApplyGamepadSettings(OverlaySettings settings)
    {
        _gamepadInputService.Enabled = settings.EnableGamepadHotkeys;

        if (GamepadHotkeyParser.TryParse(settings.GamepadPrevHotkey, out var prev))
        {
            _gamepadInputService.PrevHotkey = prev;
        }

        if (GamepadHotkeyParser.TryParse(settings.GamepadNextHotkey, out var next))
        {
            _gamepadInputService.NextHotkey = next;
        }

        if (GamepadHotkeyParser.TryParse(settings.GamepadToggleHotkey, out var toggle))
        {
            _gamepadInputService.ToggleHotkey = toggle;
        }
    }

    private static void ApplyAutoStart(bool enable)
    {
        if (!RuntimeFeatureSupport.SupportsRegistryAutoStart())
        {
            return;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null)
            {
                return;
            }

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue("HorizonRadioOverlay", AppLaunchPolicy.BuildAutoStartCommand(exePath));
                }
            }
            else
            {
                key.DeleteValue("HorizonRadioOverlay", false);
            }
        }
        catch { }
    }
}
