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
    private readonly DispatcherTimer _pollTimer;
    private readonly MainShellViewModel _shellViewModel = new();

    private GlobalHotkeyService? _hotkeyService;
    private readonly ServiceLifecycle _lifecycle = new();
    private bool _isPolling;
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
    private readonly SmtcLyricTimingController _smtcLyricTimingController = new();
    private readonly NeteaseLyricTimingController _neteaseLyricTimingController = new();
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
    private TextBlock ThemePreviewTitle => (_themeSettingsPageView ??= new ThemeSettingsPage()).ThemePreviewTitle;
    private TextBlock ThemePreviewArtist => (_themeSettingsPageView ??= new ThemeSettingsPage()).ThemePreviewArtist;
    private TextBlock ThemePreviewLyrics => (_themeSettingsPageView ??= new ThemeSettingsPage()).ThemePreviewLyrics;

    private ComboBox TrackSourceComboBox => (_floatingSettingsPageView ??= new FloatingSettingsPage()).TrackSourceComboBox;
    private Slider HorizontalSlider => (_floatingSettingsPageView ??= new FloatingSettingsPage()).HorizontalSlider;
    private Slider BottomOffsetSlider => (_floatingSettingsPageView ??= new FloatingSettingsPage()).BottomOffsetSlider;
    private Slider ScaleSlider => (_floatingSettingsPageView ??= new FloatingSettingsPage()).ScaleSlider;
    private TextBlock HorizontalValueText => (_floatingSettingsPageView ??= new FloatingSettingsPage()).HorizontalValueText;
    private TextBlock BottomOffsetValueText => (_floatingSettingsPageView ??= new FloatingSettingsPage()).BottomOffsetValueText;
    private TextBlock ScaleValueText => (_floatingSettingsPageView ??= new FloatingSettingsPage()).ScaleValueText;
    private CheckBox MinimizeToTrayCheckBox => (_floatingSettingsPageView ??= new FloatingSettingsPage()).MinimizeToTrayCheckBox;
    private CheckBox AutoStartCheckBox => (_floatingSettingsPageView ??= new FloatingSettingsPage()).AutoStartCheckBox;
    private CheckBox AlwaysShowCheckBox => (_floatingSettingsPageView ??= new FloatingSettingsPage()).AlwaysShowCheckBox;
    private CheckBox HideOverlayWhenPausedCheckBox => (_floatingSettingsPageView ??= new FloatingSettingsPage()).HideOverlayWhenPausedCheckBox;
    private CheckBox DiagnosticCheckBox => (_floatingSettingsPageView ??= new FloatingSettingsPage()).DiagnosticCheckBox;
    private CheckBox EnableLyricsCheckBox => (_floatingSettingsPageView ??= new FloatingSettingsPage()).EnableLyricsCheckBox;
    private CheckBox EnableNeteaseMemoryTimelineCheckBox => (_floatingSettingsPageView ??= new FloatingSettingsPage()).EnableNeteaseMemoryTimelineCheckBox;
    private CheckBox EnableCoverWingEffectCheckBox => (_floatingSettingsPageView ??= new FloatingSettingsPage()).EnableCoverWingEffectCheckBox;

    private TextBox AppPrevHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).AppPrevHotkeyBox;
    private TextBox AppNextHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).AppNextHotkeyBox;
    private TextBox AppToggleHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).AppToggleHotkeyBox;
    private TextBox AppToggleOverlayHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).AppToggleOverlayHotkeyBox;
    private TextBox NeteasePrevHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).NeteasePrevHotkeyBox;
    private TextBox NeteaseNextHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).NeteaseNextHotkeyBox;
    private TextBox NeteaseToggleHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).NeteaseToggleHotkeyBox;
    private CheckBox EnableGamepadCheckBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).EnableGamepadCheckBox;
    private TextBox GamepadPrevHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).GamepadPrevHotkeyBox;
    private TextBox GamepadNextHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).GamepadNextHotkeyBox;
    private TextBox GamepadToggleHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).GamepadToggleHotkeyBox;
    private TextBox GamepadToggleOverlayHotkeyBox => (_hotkeySettingsPageView ??= new HotkeySettingsPage()).GamepadToggleOverlayHotkeyBox;

    private RadioButton TitleColor_White => (_themeSettingsPageView ??= new ThemeSettingsPage()).TitleColor_White;
    private RadioButton TitleColor_Light => (_themeSettingsPageView ??= new ThemeSettingsPage()).TitleColor_Light;
    private RadioButton TitleColor_Yellow => (_themeSettingsPageView ??= new ThemeSettingsPage()).TitleColor_Yellow;
    private RadioButton TitleColor_Green => (_themeSettingsPageView ??= new ThemeSettingsPage()).TitleColor_Green;
    private RadioButton TitleColor_Orange => (_themeSettingsPageView ??= new ThemeSettingsPage()).TitleColor_Orange;
    private RadioButton ArtistColor_Light => (_themeSettingsPageView ??= new ThemeSettingsPage()).ArtistColor_Light;
    private RadioButton ArtistColor_White => (_themeSettingsPageView ??= new ThemeSettingsPage()).ArtistColor_White;
    private RadioButton ArtistColor_Yellow => (_themeSettingsPageView ??= new ThemeSettingsPage()).ArtistColor_Yellow;
    private RadioButton ArtistColor_Green => (_themeSettingsPageView ??= new ThemeSettingsPage()).ArtistColor_Green;
    private RadioButton ArtistColor_Orange => (_themeSettingsPageView ??= new ThemeSettingsPage()).ArtistColor_Orange;
    private RadioButton LyricsColor_Light => (_themeSettingsPageView ??= new ThemeSettingsPage()).LyricsColor_Light;
    private RadioButton LyricsColor_White => (_themeSettingsPageView ??= new ThemeSettingsPage()).LyricsColor_White;
    private RadioButton LyricsColor_Yellow => (_themeSettingsPageView ??= new ThemeSettingsPage()).LyricsColor_Yellow;
    private RadioButton LyricsColor_Green => (_themeSettingsPageView ??= new ThemeSettingsPage()).LyricsColor_Green;
    private RadioButton LyricsColor_Orange => (_themeSettingsPageView ??= new ThemeSettingsPage()).LyricsColor_Orange;
    private Slider TitleOpacitySlider => (_themeSettingsPageView ??= new ThemeSettingsPage()).TitleOpacitySlider;
    private Slider ArtistOpacitySlider => (_themeSettingsPageView ??= new ThemeSettingsPage()).ArtistOpacitySlider;
    private Slider LyricsOpacitySlider => (_themeSettingsPageView ??= new ThemeSettingsPage()).LyricsOpacitySlider;
    private TextBlock TitleOpacityValueText => (_themeSettingsPageView ??= new ThemeSettingsPage()).TitleOpacityValueText;
    private TextBlock ArtistOpacityValueText => (_themeSettingsPageView ??= new ThemeSettingsPage()).ArtistOpacityValueText;
    private TextBlock LyricsOpacityValueText => (_themeSettingsPageView ??= new ThemeSettingsPage()).LyricsOpacityValueText;

    private TextBlock LogTextBlock => (_logsPageView ??= new LogsPage()).LogTextBlock;
    private ScrollViewer LogScrollViewer => (_logsPageView ??= new LogsPage()).LogScrollViewer;

    public MainWindow(bool startHiddenToTray = false)
    {
        _isInitializingOverlayControls = true;
        _startHiddenToTray = startHiddenToTray;

        _diagnostic = new DiagnosticService();
        var coverCache = new CoverCacheService(_diagnostic);
        var neteaseOfficialResolver = new NeteaseOfficialResolver(_diagnostic);
        var neteaseLocalDataService = new NeteaseLocalDataService(coverCache, _diagnostic, neteaseOfficialResolver);
        var neteaseMemoryPlaybackProbe = new NeteaseMemoryPlaybackProbe(_diagnostic);
        var smtcTrackService = new SmtcTrackService(_diagnostic);
        _playbackCoordinator = new PlaybackCoordinator([
            new NeteasePlaybackSource(neteaseLocalDataService, new NeteaseShortcutSender(), neteaseMemoryPlaybackProbe),
            new SmtcPlaybackSource(smtcTrackService)
        ]);
        _playbackSession = new PlaybackSessionService(_playbackCoordinator, _diagnostic);
        _overlaySettingsService = new OverlaySettingsService();
        _overlayWindow = new OverlayWindow();
        _gamepadInputService = new GamepadInputService();
        _updateService = new UpdateService();
        _lyricsService = new LyricsService(_diagnostic);
        _remoteControlService = new RemoteControlService(GetRemoteControlStatusAsync, HandleRemoteControlActionAsync);
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

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PollSlowMs)
        };
        _pollTimer.Tick += PollTimer_Tick;

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

        _lifecycle.Register("PollTimer",
            onStart: () => _pollTimer.Start(),
            onStop: () => _pollTimer.Stop());

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
