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

[SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    private const int PollFastMs = BackgroundPollingPolicy.FastPollMs;
    private const int PollSlowMs = BackgroundPollingPolicy.WarmPollMs;
    private const int PollBoostDurationMs = 3000;

    private readonly PlaybackCoordinator _playbackCoordinator;
    private readonly PlaybackSessionService _playbackSession;
    private readonly OverlaySettingsService _overlaySettingsService;
    private readonly OverlayWindow _overlayWindow;
    private readonly GamepadInputService _gamepadInputService;
    private readonly UpdateService _updateService;
    private readonly DiagnosticService _diagnostic;
    private readonly LyricsService _lyricsService;
    private readonly RemoteControlService _remoteControlService;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private readonly SemaphoreSlim _pollWakeSignal = new(0, 1);
    private readonly object _lyricGate = new();
    private volatile bool _isMainWindowVisible = true;
    private volatile bool _isOverlayVisible;
    private PlaybackDataHealth? _lastReportedHealth;
    private string? _lastReportedHealthError;
    private string? _lastReportedHealthSourceId;
    private readonly MainShellViewModel _shellViewModel;
    private readonly NowPlayingViewModel _nowPlayingViewModel;
    private readonly FloatingSettingsViewModel _floatingSettingsViewModel;
    private readonly ThemeSettingsViewModel _themeSettingsViewModel;
    private readonly HotkeySettingsViewModel _hotkeySettingsViewModel;
    private readonly RemoteControlViewModel _remoteControlViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly AboutViewModel _aboutViewModel;

    private GlobalHotkeyService? _hotkeyService;
    private readonly ServiceLifecycle _lifecycle = new();
    private bool _isInitializingOverlayControls;
    private OverlaySettings _activeSettings;
    private string _lastTrackKey = string.Empty;
    private string _lastDisplayTrackKey = string.Empty;
    private string _lastLyricsPreviewLine = string.Empty;
    private bool _pageMetaUpdateDirty;
    private readonly DispatcherTimer _pageMetaUpdateTimer;
    private string _lastStatusText = string.Empty;
    private bool _lastStatusIsError;
    private byte[]? _lastPreviewCoverBytes;
    private CancellationTokenSource? _smtcCoverRefreshCts;
    private double _songDetectedTime;
    private long _pollBoostUntil;
    private long _lastConsumedTrackVersion;
    private long _lastConsumedTimelineVersion;
    private readonly Dictionary<TextBox, HashSet<GamepadButton>> _gamepadPressed = new();
    private readonly Dictionary<TextBox, DispatcherTimer> _gamepadCommitTimers = new();
    private readonly Dictionary<TextBox, GamepadButton> _gamepadCaptureBaseline = new();
    private readonly Dictionary<TextBox, GamepadButton> _gamepadCaptureCandidates = new();
    private readonly Dictionary<TextBox, int> _gamepadCaptureCandidateCounts = new();
    private bool _gamepadEnabledBeforeCapture;
    private int _gamepadCaptureFocusCount;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _isRealExit;
    private readonly bool _startHiddenToTray;
    private bool _startupInitialized;
    private int _previewEffectLevel = 1;
    private bool _suppressPageAnimation;
    private FileSystemWatcher? _logWatcher;
    private double? _lastSmtcPlaybackPositionSeconds;
    private bool _overlayHiddenByPause;
    private readonly SmtcLyricTimingController _smtcLyricTimingController;
    private readonly NeteaseLyricTimingController _neteaseLyricTimingController;
    private NowPlayingPage? _nowPlayingPageView;
    private FloatingSettingsPage? _floatingSettingsPageView;
    private HotkeySettingsPage? _hotkeySettingsPageView;
    private RemoteControlPage? _remoteControlPageView;
    private ThemeSettingsPage? _themeSettingsPageView;
    private LogsPage? _logsPageView;
    private AboutPage? _aboutPageView;
    private bool _nowPlayingPageWired;
    private bool _floatingSettingsPageWired;
    private bool _hotkeySettingsPageWired;
    private bool _remoteControlPageWired;
    private bool _themeSettingsPageWired;
    private bool _logsPageWired;
    private bool _aboutPageWired;

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
        (GamepadButton.Start, "Start"),
        (GamepadButton.Button1, "Button1"),
        (GamepadButton.Button2, "Button2"),
        (GamepadButton.Button3, "Button3"),
        (GamepadButton.Button4, "Button4"),
        (GamepadButton.Button5, "Button5"),
        (GamepadButton.Button6, "Button6"),
        (GamepadButton.Button7, "Button7"),
        (GamepadButton.Button8, "Button8"),
        (GamepadButton.Button9, "Button9"),
        (GamepadButton.Button10, "Button10"),
        (GamepadButton.Button11, "Button11"),
        (GamepadButton.Button12, "Button12"),
        (GamepadButton.Button13, "Button13"),
        (GamepadButton.Button14, "Button14"),
        (GamepadButton.Button15, "Button15"),
        (GamepadButton.Button16, "Button16")
    };

    private NowPlayingPage NowPlayingPageView => _nowPlayingPageView ??= new NowPlayingPage();
    private FloatingSettingsPage FloatingSettingsPageView => _floatingSettingsPageView ??= new FloatingSettingsPage();
    private HotkeySettingsPage HotkeySettingsPageView => _hotkeySettingsPageView ??= new HotkeySettingsPage();
    private RemoteControlPage RemoteControlPageView => _remoteControlPageView ??= new RemoteControlPage();
    private ThemeSettingsPage ThemeSettingsPageView => _themeSettingsPageView ??= new ThemeSettingsPage();
    private LogsPage LogsPageView => _logsPageView ??= new LogsPage();
    private AboutPage AboutPageView => _aboutPageView ??= new AboutPage();

    private Image CoverPreview => (_nowPlayingPageView ??= new NowPlayingPage()).CoverPreview;
    private TextBlock CurrentTitle => (_nowPlayingPageView ??= new NowPlayingPage()).CurrentTitle;
    private TextBlock CurrentArtist => (_nowPlayingPageView ??= new NowPlayingPage()).CurrentArtist;
    private TextBlock CurrentMeta => (_nowPlayingPageView ??= new NowPlayingPage()).CurrentMeta;
    private TextBlock LyricsPreviewText => (_nowPlayingPageView ??= new NowPlayingPage()).LyricsPreviewText;
    private TextBlock ConnectionStatusText => (_nowPlayingPageView ??= new NowPlayingPage()).ConnectionStatusText;
    private TextBlock ConnectionStatusSubText => (_nowPlayingPageView ??= new NowPlayingPage()).ConnectionStatusSubText;


    public MainWindow(bool startHiddenToTray = false)
        : this(
            AppServices.GetRequiredService<PlaybackCoordinator>(),
            AppServices.GetRequiredService<PlaybackSessionService>(),
            AppServices.GetRequiredService<OverlaySettingsService>(),
            AppServices.GetRequiredService<OverlayWindow>(),
            AppServices.GetRequiredService<GamepadInputService>(),
            AppServices.GetRequiredService<UpdateService>(),
            AppServices.GetRequiredService<DiagnosticService>(),
            AppServices.GetRequiredService<LyricsService>(),
            AppServices.GetRequiredService<RemoteControlService>(),
            AppServices.GetRequiredService<MainShellViewModel>(),
            AppServices.GetRequiredService<NowPlayingViewModel>(),
            AppServices.GetRequiredService<FloatingSettingsViewModel>(),
            AppServices.GetRequiredService<ThemeSettingsViewModel>(),
            AppServices.GetRequiredService<HotkeySettingsViewModel>(),
            AppServices.GetRequiredService<RemoteControlViewModel>(),
            AppServices.GetRequiredService<LogsViewModel>(),
            AppServices.GetRequiredService<AboutViewModel>(),
            AppServices.GetRequiredService<SmtcLyricTimingController>(),
            AppServices.GetRequiredService<NeteaseLyricTimingController>(),
            startHiddenToTray)
    {
    }

    public MainWindow(
        PlaybackCoordinator playbackCoordinator,
        PlaybackSessionService playbackSession,
        OverlaySettingsService overlaySettingsService,
        OverlayWindow overlayWindow,
        GamepadInputService gamepadInputService,
        UpdateService updateService,
        DiagnosticService diagnostic,
        LyricsService lyricsService,
        RemoteControlService remoteControlService,
        MainShellViewModel shellViewModel,
        NowPlayingViewModel nowPlayingViewModel,
        FloatingSettingsViewModel floatingSettingsViewModel,
        ThemeSettingsViewModel themeSettingsViewModel,
        HotkeySettingsViewModel hotkeySettingsViewModel,
        RemoteControlViewModel remoteControlViewModel,
        LogsViewModel logsViewModel,
        AboutViewModel aboutViewModel,
        SmtcLyricTimingController smtcLyricTimingController,
        NeteaseLyricTimingController neteaseLyricTimingController)
        : this(
            playbackCoordinator,
            playbackSession,
            overlaySettingsService,
            overlayWindow,
            gamepadInputService,
            updateService,
            diagnostic,
            lyricsService,
            remoteControlService,
            shellViewModel,
            nowPlayingViewModel,
            floatingSettingsViewModel,
            themeSettingsViewModel,
            hotkeySettingsViewModel,
            remoteControlViewModel,
            logsViewModel,
            aboutViewModel,
            smtcLyricTimingController,
            neteaseLyricTimingController,
            startHiddenToTray: false)
    {
    }

    public MainWindow(
        PlaybackCoordinator playbackCoordinator,
        PlaybackSessionService playbackSession,
        OverlaySettingsService overlaySettingsService,
        OverlayWindow overlayWindow,
        GamepadInputService gamepadInputService,
        UpdateService updateService,
        DiagnosticService diagnostic,
        LyricsService lyricsService,
        RemoteControlService remoteControlService,
        MainShellViewModel shellViewModel,
        NowPlayingViewModel nowPlayingViewModel,
        FloatingSettingsViewModel floatingSettingsViewModel,
        ThemeSettingsViewModel themeSettingsViewModel,
        HotkeySettingsViewModel hotkeySettingsViewModel,
        RemoteControlViewModel remoteControlViewModel,
        LogsViewModel logsViewModel,
        AboutViewModel aboutViewModel,
        SmtcLyricTimingController smtcLyricTimingController,
        NeteaseLyricTimingController neteaseLyricTimingController,
        bool startHiddenToTray)
    {
        _isInitializingOverlayControls = true;
        _startHiddenToTray = startHiddenToTray;

        _playbackCoordinator = playbackCoordinator;
        _playbackSession = playbackSession;
        _overlaySettingsService = overlaySettingsService;
        _overlayWindow = overlayWindow;
        _gamepadInputService = gamepadInputService;
        _updateService = updateService;
        _diagnostic = diagnostic;
        _lyricsService = lyricsService;
        _remoteControlService = remoteControlService;
        _shellViewModel = shellViewModel;
        _nowPlayingViewModel = nowPlayingViewModel;
        _floatingSettingsViewModel = floatingSettingsViewModel;
        _themeSettingsViewModel = themeSettingsViewModel;
        _hotkeySettingsViewModel = hotkeySettingsViewModel;
        _remoteControlViewModel = remoteControlViewModel;
        _logsViewModel = logsViewModel;
        _aboutViewModel = aboutViewModel;
        _smtcLyricTimingController = smtcLyricTimingController;
        _neteaseLyricTimingController = neteaseLyricTimingController;

        _remoteControlService.SetHandlers(GetRemoteControlStatusAsync, HandleRemoteControlActionAsync);

        OverlaySettings loadedSettings = _overlaySettingsService.Load();
        _activeSettings = loadedSettings;
        UpdatePlaybackSessionConfiguration();
        _smtcLyricTimingController.SetDelayOverrideMilliseconds(loadedSettings.SmtcLyricDelayOverrideMs);
        _overlayWindow.ApplySettings(loadedSettings);
        ApplyGamepadSettings(loadedSettings);

        InitializeComponent();
        DataContext = _shellViewModel;
        WireShellControls();
        InitializeTrayIcon();
        ApplyAutoWindowSize();
        ApplyRuntimeFeatureAvailability();

        IsVisibleChanged += (_, _) => UpdateUiVisibilityCache();
        StateChanged += (_, _) => UpdateUiVisibilityCache();
        _overlayWindow.IsVisibleChanged += (_, _) => UpdateUiVisibilityCache();
        UpdateUiVisibilityCache();

        _pageMetaUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
            _pageMetaUpdateTimer.Tick += (_, _) =>
        {
            _pageMetaUpdateTimer.Stop();
            if (_pageMetaUpdateDirty)
            {
                _pageMetaUpdateDirty = false;
                UpdatePageMetaTexts();
            }
        };

        _gamepadInputService.PrevTriggered += async (_, _) => await PrevAsync();
        _gamepadInputService.NextTriggered += async (_, _) => await NextAsync();
        _gamepadInputService.ToggleTriggered += async (_, _) => await TogglePlayPauseAsync();
        _gamepadInputService.ToggleOverlayTriggered += async (_, _) => await ToggleOverlayVisibilityAsync();

        _lifecycle.Register("GamepadInput",
            onStart: () => _gamepadInputService.Start(),
            onDispose: () => _gamepadInputService.Dispose());

        _lifecycle.Register("GamepadHotkeyCapture",
            onDispose: DisposeGamepadHotkeyCaptureTimers);

        _lifecycle.Register("PlaybackSession",
            onStart: _playbackSession.Start,
            onStop: _playbackSession.Stop,
            onDispose: _playbackSession.Dispose);

        _lifecycle.Register("PlaybackPoller",
            onStart: StartPolling,
            onStop: StopPolling,
            onDispose: DisposePolling);

        _lifecycle.Register("Hotkey",
            onDispose: () => _hotkeyService?.Dispose());

        _lifecycle.Register("UpdateService",
            onDispose: () => _updateService.Dispose());

        _remoteControlService.StateChanged += (_, _) => Dispatcher.BeginInvoke(UpdateRemoteControlPage);
        _lifecycle.Register("RemoteControl",
            onStart: () => _remoteControlService.ApplySettings(_activeSettings),
            onStop: () => _remoteControlService.Stop(),
            onDispose: () => _remoteControlService.Dispose());

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

        _lifecycle.Register("LogWatcher",
            onDispose: () =>
            {
                if (_logWatcher == null) return;
                try { _logWatcher.EnableRaisingEvents = false; } catch { }
                try { _logWatcher.Dispose(); } catch { }
                _logWatcher = null;
            });

        UpdatePageMetaTexts();
        _isInitializingOverlayControls = false;
        _diagnostic.Event("应用启动，主窗口已初始化。");
        _diagnostic.Event($"当前来源：{_activeSettings.TrackSource}");
        _diagnostic.Event($"歌词显示：{_activeSettings.EnableLyrics}");
        _diagnostic.Event($"手柄热键：{_activeSettings.EnableGamepadHotkeys}");
        Dispatcher.BeginInvoke(() => StartupDiagnosticsService.LogSnapshot(_diagnostic, _activeSettings), DispatcherPriority.ContextIdle);

        LoadEmbeddedResources();

        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }
}
