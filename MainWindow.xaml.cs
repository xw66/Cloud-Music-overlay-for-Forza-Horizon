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
    private long _lastNeteaseTrackRefreshAt;
    private long _lastSmtcTimelineSampleAt;
    private long _lastSmtcTrackRefreshAt;
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
        _neteaseOfficialResolver = new NeteaseOfficialResolver(_diagnostic);
        _neteaseLocalDataService = new NeteaseLocalDataService(coverCache, _diagnostic, _neteaseOfficialResolver);
        _smtcTrackService = new SmtcTrackService(_diagnostic);
        _overlaySettingsService = new OverlaySettingsService();
        _overlayWindow = new OverlayWindow();
        _neteaseShortcutSender = new NeteaseShortcutSender();
        _gamepadInputService = new GamepadInputService();
        _updateService = new UpdateService();
        _lyricsService = new LyricsService(_diagnostic);
        _remoteControlService = new RemoteControlService(GetRemoteControlStatusAsync, HandleRemoteControlActionAsync);
        OverlaySettings loadedSettings = _overlaySettingsService.Load();
        _activeSettings = loadedSettings;
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

    private void WireShellControls()
    {
        NavigationListBox.SelectedIndex = 0;
        ShowPage(_shellViewModel.SelectedItem?.Key ?? "NowPlaying");
    }

    private void WirePageControls()
    {
        if (_nowPlayingPageView != null && !_nowPlayingPageWired)
        {
            _nowPlayingPageView.PrevButton.Click += Prev_Click;
            _nowPlayingPageView.PlayPauseButton.Click += PlayPause_Click;
            _nowPlayingPageView.NextButton.Click += Next_Click;
            _nowPlayingPageView.RefreshButton.Click += Refresh_Click;
            _nowPlayingPageView.OpenFloatingSettingsButton.Click += (_, _) => NavigateTo("FloatingSettings");
            _nowPlayingPageView.OpenHotkeySettingsButton.Click += (_, _) => NavigateTo("Hotkeys");
            _nowPlayingPageWired = true;
        }

        if (_floatingSettingsPageView != null && !_floatingSettingsPageWired)
        {
            _floatingSettingsPageView.TrackSourceComboBox.SelectionChanged += TrackSourceComboBox_SelectionChanged;
            _floatingSettingsPageView.HorizontalSlider.ValueChanged += HorizontalSlider_ValueChanged;
            _floatingSettingsPageView.BottomOffsetSlider.ValueChanged += BottomOffsetSlider_ValueChanged;
            _floatingSettingsPageView.ScaleSlider.ValueChanged += ScaleSlider_ValueChanged;
            _floatingSettingsPageView.MinimizeToTrayCheckBox.Checked += SettingCheckBox_Changed;
            _floatingSettingsPageView.MinimizeToTrayCheckBox.Unchecked += SettingCheckBox_Changed;
            _floatingSettingsPageView.AutoStartCheckBox.Checked += SettingCheckBox_Changed;
            _floatingSettingsPageView.AutoStartCheckBox.Unchecked += SettingCheckBox_Changed;
            _floatingSettingsPageView.AlwaysShowCheckBox.Checked += SettingCheckBox_Changed;
            _floatingSettingsPageView.AlwaysShowCheckBox.Unchecked += SettingCheckBox_Changed;
            _floatingSettingsPageView.HideOverlayWhenPausedCheckBox.Checked += SettingCheckBox_Changed;
            _floatingSettingsPageView.HideOverlayWhenPausedCheckBox.Unchecked += SettingCheckBox_Changed;
            _floatingSettingsPageView.DiagnosticCheckBox.Checked += SettingCheckBox_Changed;
            _floatingSettingsPageView.DiagnosticCheckBox.Unchecked += SettingCheckBox_Changed;
            _floatingSettingsPageView.EnableLyricsCheckBox.Checked += SettingCheckBox_Changed;
            _floatingSettingsPageView.EnableLyricsCheckBox.Unchecked += SettingCheckBox_Changed;
            _floatingSettingsPageView.EnableCoverWingEffectCheckBox.Checked += SettingCheckBox_Changed;
            _floatingSettingsPageView.EnableCoverWingEffectCheckBox.Unchecked += SettingCheckBox_Changed;
            _floatingSettingsPageWired = true;
            InitializeOverlayControls(_activeSettings);
        }

        if (_hotkeySettingsPageView != null && !_hotkeySettingsPageWired)
        {
            _hotkeySettingsPageView.EnableGamepadCheckBox.Checked += SettingCheckBox_Changed;
            _hotkeySettingsPageView.EnableGamepadCheckBox.Unchecked += SettingCheckBox_Changed;
            _hotkeySettingsPageView.AppPrevHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageView.AppNextHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageView.AppToggleHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageView.AppToggleOverlayHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageView.NeteasePrevHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageView.NeteaseNextHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageView.NeteaseToggleHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageView.GamepadPrevHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageView.GamepadNextHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageView.GamepadToggleHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageView.GamepadToggleOverlayHotkeyBox.LostFocus += HotkeyBox_LostFocus;
            _hotkeySettingsPageWired = true;
            InitializeOverlayControls(_activeSettings);
            SetupHotkeyCaptureInputs();
        }

        if (_remoteControlPageView != null && !_remoteControlPageWired)
        {
            _remoteControlPageView.EnableRemoteControlCheckBox.Checked += RemoteControlSetting_Changed;
            _remoteControlPageView.EnableRemoteControlCheckBox.Unchecked += RemoteControlSetting_Changed;
            _remoteControlPageView.RemoteControlAllowLanCheckBox.Checked += RemoteControlSetting_Changed;
            _remoteControlPageView.RemoteControlAllowLanCheckBox.Unchecked += RemoteControlSetting_Changed;
            _remoteControlPageView.RemoteControlPortBox.LostFocus += RemoteControlSetting_Changed;
            _remoteControlPageView.CopyRemoteAddressButton.Click += CopyRemoteAddress_Click;
            _remoteControlPageView.ResetRemoteTokenButton.Click += ResetRemoteToken_Click;
            _remoteControlPageWired = true;
            InitializeOverlayControls(_activeSettings);
        }

        if (_themeSettingsPageView != null && !_themeSettingsPageWired)
        {
            _themeSettingsPageView.TitleColor_White.Click += TitleColor_Click;
            _themeSettingsPageView.TitleColor_Light.Click += TitleColor_Click;
            _themeSettingsPageView.TitleColor_Yellow.Click += TitleColor_Click;
            _themeSettingsPageView.TitleColor_Green.Click += TitleColor_Click;
            _themeSettingsPageView.TitleColor_Orange.Click += TitleColor_Click;
            _themeSettingsPageView.ArtistColor_Light.Click += ArtistColor_Click;
            _themeSettingsPageView.ArtistColor_White.Click += ArtistColor_Click;
            _themeSettingsPageView.ArtistColor_Yellow.Click += ArtistColor_Click;
            _themeSettingsPageView.ArtistColor_Green.Click += ArtistColor_Click;
            _themeSettingsPageView.ArtistColor_Orange.Click += ArtistColor_Click;
            _themeSettingsPageView.LyricsColor_Light.Click += LyricsColor_Click;
            _themeSettingsPageView.LyricsColor_White.Click += LyricsColor_Click;
            _themeSettingsPageView.LyricsColor_Yellow.Click += LyricsColor_Click;
            _themeSettingsPageView.LyricsColor_Green.Click += LyricsColor_Click;
            _themeSettingsPageView.LyricsColor_Orange.Click += LyricsColor_Click;
            _themeSettingsPageView.TitleOpacitySlider.ValueChanged += TitleOpacitySlider_ValueChanged;
            _themeSettingsPageView.ArtistOpacitySlider.ValueChanged += ArtistOpacitySlider_ValueChanged;
            _themeSettingsPageView.LyricsOpacitySlider.ValueChanged += LyricsOpacitySlider_ValueChanged;
            _themeSettingsPageView.ThemeAccentIndigoButton.Click += (_, _) => ApplyThemeAccentColor("#5B5CEB");
            _themeSettingsPageView.ThemeAccentBlueButton.Click += (_, _) => ApplyThemeAccentColor("#3B82F6");
            _themeSettingsPageView.ThemeAccentGreenButton.Click += (_, _) => ApplyThemeAccentColor("#22C55E");
            _themeSettingsPageView.ThemeAccentAmberButton.Click += (_, _) => ApplyThemeAccentColor("#F59E0B");
            _themeSettingsPageView.ThemeAccentRoseButton.Click += (_, _) => ApplyThemeAccentColor("#F43F5E");
            _themeSettingsPageView.PreviewEffectSoftButton.Click += (_, _) => SetPreviewEffect(0);
            _themeSettingsPageView.PreviewEffectMediumButton.Click += (_, _) => SetPreviewEffect(1);
            _themeSettingsPageView.PreviewEffectStrongButton.Click += (_, _) => SetPreviewEffect(2);
            _themeSettingsPageWired = true;
            InitializeOverlayControls(_activeSettings);
            ApplyPreviewEffect();
        }

        if (_logsPageView != null && !_logsPageWired)
        {
            _logsPageView.OpenLogFileButton.Click += OpenLogFileButton_Click;
            _logsPageView.CopyLogButton.Click += CopyLogButton_Click;
            _logsPageView.ClearLogButton.Click += ClearLogButton_Click;
            _logsPageWired = true;
            InitializeLogWatcher();
        }

        if (_aboutPageView != null && !_aboutPageWired)
        {
            _aboutPageView.ProjectHomeButton.Click += GitHubButton_Click;
            _aboutPageView.CheckUpdateButton.Click += CheckUpdate_Click;
            _aboutPageView.LicenseButton.Click += OpenLicenseButton_Click;
            _aboutPageView.VersionText.Text = $"{UiText.VersionPrefix} {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.7.1"}";
            _aboutPageWired = true;
        }
    }

    private void NavigateTo(string key)
    {
        NavigationItem? item = _shellViewModel.NavigationItems.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal));
        if (item == null)
        {
            return;
        }

        _shellViewModel.SelectedItem = item;
        NavigationListBox.SelectedItem = item;
        ShowPage(item.Key);
    }

    public void SetPreviewPage(string key)
    {
        _suppressPageAnimation = true;
        try
        {
            NavigateTo(key);
            UpdateLayout();
        }
        finally
        {
            _suppressPageAnimation = false;
        }
    }

    private void ShowPage(string key)
    {
        FrameworkElement activePage = key switch
        {
            "FloatingSettings" => FloatingSettingsPageView,
            "Hotkeys" => HotkeySettingsPageView,
            "RemoteControl" => RemoteControlPageView,
            "Theme" => ThemeSettingsPageView,
            "Logs" => LogsPageView,
            "About" => AboutPageView,
            _ => NowPlayingPageView
        };

        WirePageControls();
        PageHost.Content = activePage;
        if (_suppressPageAnimation)
        {
            activePage.BeginAnimation(OpacityProperty, null);
            activePage.Opacity = 1;
        }
        else
        {
            RunPageFadeIn(activePage);
        }

        if (key == "Logs")
        {
            RefreshLogView();
        }
    }

    private static void RunPageFadeIn(UIElement element)
    {
        element.Opacity = 0;
        DoubleAnimation animation = new()
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180)
        };
        element.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void NavigationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shellViewModel.SelectedItem != null)
        {
            ShowPage(_shellViewModel.SelectedItem.Key);
        }
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

    private void SetPreviewEffect(int level)
    {
        _previewEffectLevel = Math.Clamp(level, 0, 2);
        ApplyPreviewEffect();
    }

    private void ApplyPreviewEffect()
    {
        if (ThemeSettingsPageView?.ThemePreviewCard == null)
        {
            return;
        }

        double opacity = _previewEffectLevel switch
        {
            0 => 0.10,
            2 => 0.28,
            _ => 0.18
        };
        double blurRadius = _previewEffectLevel switch
        {
            0 => 10,
            2 => 24,
            _ => 16
        };
        double shadowDepth = _previewEffectLevel switch
        {
            0 => 2,
            2 => 8,
            _ => 4
        };

        ThemeSettingsPageView.ThemePreviewCard.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = blurRadius,
            ShadowDepth = shadowDepth,
            Opacity = opacity
        };
    }

    private void ApplyThemeAccentColor(string colorHex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            Resources["PrimaryBrush"] = brush;
            Application.Current.Resources["PrimaryBrush"] = brush;
            SidebarIconBadge.Background = brush;
        }
        catch
        {
        }
    }

    private void UpdatePageMetaTexts()
    {
        bool useSmtc = IsSmtcSource();
        SidebarConnectionStateText.Text = useSmtc ? UiText.SidebarConnectedSmtc : UiText.SidebarConnectedNetease;
        SidebarConnectionSubText.Text = useSmtc ? "SMTC 媒体会话" : "CloudMusic(ProcessTitle)";
        ConnectionStatusText.Text = useSmtc ? UiText.Connected : UiText.SidebarConnectedNetease;
        ConnectionStatusSubText.Text = useSmtc ? "等待新的系统媒体会话。" : "等待网易云窗口标题刷新。";
    }

    private void InitializeLogWatcher()
    {
        try
        {
            string logPath = _diagnostic.LogFilePath;
            string directory = Path.GetDirectoryName(logPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string fileName = Path.GetFileName(logPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _logWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _logWatcher.Changed += (_, _) => Dispatcher.InvokeAsync(RefreshLogView);
            _logWatcher.Created += (_, _) => Dispatcher.InvokeAsync(RefreshLogView);
            _logWatcher.Renamed += (_, _) => Dispatcher.InvokeAsync(RefreshLogView);
            RefreshLogView();
        }
        catch
        {
        }
    }

    private void RefreshLogView()
    {
        try
        {
            LogTextBlock.Text = _diagnostic.ReadCurrentLogText();
            LogScrollViewer.ScrollToEnd();
        }
        catch
        {
        }
    }

    private void OpenLogFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _diagnostic.Event("打开日志文件。");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _diagnostic.LogFilePath,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void CopyLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(LogTextBlock.Text ?? string.Empty);
            _diagnostic.Event("复制日志内容。");
            SetStatus("状态：日志已复制。", true);
        }
        catch
        {
            SetStatus("状态：复制日志失败。", false);
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _diagnostic.Clear();
            RefreshLogView();
            SetStatus("状态：日志已清空。", true);
        }
        catch
        {
            SetStatus("状态：清空日志失败。", false);
        }
    }

    private void OpenLicenseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/",
                UseShellExecute = true
            });
        }
        catch
        {
        }
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
            Text = "网易云悬浮窗 v2.0.0",
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
            _trayIcon.ShowBalloonTip(2000, "网易云悬浮窗 v2.0.0", "已最小化到托盘，双击图标恢复。", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (_startupInitialized)
        {
            return;
        }

        _startupInitialized = true;
        _gamepadInputService.AttachRawInput(this, _diagnostic);
        bool ok = RebindGlobalHotkeys();
        SetStatus(ok
            ? "状态：快捷键已就绪，按应用快捷键会转发网易云快捷键。"
            : "状态：应用快捷键注册失败（被占用或格式无效）。", false);

        _lifecycle.StartAll();
        _ = RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
        Dispatcher.BeginInvoke(async () => await SilentCheckUpdateAsync(), DispatcherPriority.Background);

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
        _pageMetaUpdateTimer.Stop();
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
            bool isSmtcSource = IsSmtcSource();
            bool shouldSyncLyrics = SmtcLyricsSyncPolicy.ShouldPrioritizeTimelineSync(_activeSettings.EnableLyrics, isSmtcSource);
            bool shouldSyncNeteaseLyrics = _activeSettings.EnableLyrics && !isSmtcSource;
            bool shouldCheckPauseVisibility = _activeSettings.HideOverlayWhenPaused && isSmtcSource;
            long now = Environment.TickCount64;
            bool shouldSampleSmtcTimeline = (shouldSyncLyrics || shouldCheckPauseVisibility)
                && SmtcPollingPolicy.ShouldSample(
                    now,
                    _lastSmtcTimelineSampleAt,
                    SmtcPollingPolicy.TimelineSampleIntervalMs);
            if (shouldSampleSmtcTimeline)
            {
                (TimeSpan Position, bool IsPlaying)? playbackState = await _smtcTrackService.GetPlaybackStateAsync();
                _lastSmtcTimelineSampleAt = now;
                if (playbackState is { } state)
                {
                    await ApplyPauseOverlayVisibilityRuleAsync(isSmtcSource, state.IsPlaying);
                    if (shouldSyncLyrics)
                    {
                        _lastSmtcPlaybackPositionSeconds = state.Position.TotalSeconds;
                        SmtcLyricTimingSample timingSample = _smtcLyricTimingController.Update(state.Position.TotalSeconds, state.IsPlaying);
                        if (timingSample.IsDiscontinuity)
                        {
                            _diagnostic.Info(
                                $"SMTC lyric timing: raw={timingSample.RawPositionSeconds:F3}s, display={timingSample.DisplayPositionSeconds:F3}s, compensation={timingSample.CompensationSeconds:F3}s, discontinuity={timingSample.IsDiscontinuity}");
                        }
                    }
                }
            }

            if (shouldSyncLyrics && _smtcLyricTimingController.GetCurrentDisplayPositionSeconds() is { } displayPosition)
            {
                _lyricsService.SetPlaybackPosition(displayPosition);
            }

            if (shouldSyncNeteaseLyrics)
            {
                _lyricsService.SetPlaybackPosition(_neteaseLyricTimingController.GetCurrentPositionSeconds());
            }

            if (shouldSyncLyrics || shouldSyncNeteaseLyrics)
            {
                string? earlyLine = _lyricsService.UpdateCurrentLine();
                if (earlyLine != null && !string.Equals(_lastLyricsPreviewLine, earlyLine, StringComparison.Ordinal))
                {
                    _lastLyricsPreviewLine = earlyLine;
                    Dispatcher.BeginInvoke(() =>
                    {
                        _overlayWindow.SetLyrics(earlyLine);
                        SetTextIfChanged(LyricsPreviewText, earlyLine);
                    }, DispatcherPriority.Background);
                }
            }

            bool shouldRefreshTrack = isSmtcSource
                ? SmtcPollingPolicy.ShouldSample(
                    now,
                    _lastSmtcTrackRefreshAt,
                    SmtcPollingPolicy.TrackRefreshIntervalMs)
                : now - _lastNeteaseTrackRefreshAt >= PollSlowMs;
            if (shouldRefreshTrack)
            {
                if (isSmtcSource)
                {
                    _lastSmtcTrackRefreshAt = now;
                }
                else
                {
                    _lastNeteaseTrackRefreshAt = now;
                }

                await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: true);
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
        bool keepFast = _activeSettings.EnableLyrics &&
                        TrackSourcePolicy.ShouldEnableLyrics(_activeSettings.TrackSource);
        bool isMainWindowVisible = IsVisible && WindowState != WindowState.Minimized;
        bool isOverlayVisible = _overlayWindow.IsVisible;
        bool boosted = Environment.TickCount64 < _pollBoostUntil;
        int target = BackgroundPollingPolicy.GetPollIntervalMs(
            keepFast,
            isMainWindowVisible,
            isOverlayVisible,
            boosted);
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

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        await TogglePlayPauseAsync();
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

    private async Task TogglePlayPauseAsync()
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
        if (!_activeSettings.HideOverlayWhenPaused || !IsSmtcSource() || !RuntimeFeatureSupport.SupportsSmtc())
        {
            return;
        }

        (TimeSpan Position, bool IsPlaying)? state = await _smtcTrackService.GetPlaybackStateAsync();
        if (state is { } playback)
        {
            await ApplyPauseOverlayVisibilityRuleAsync(isSmtcSource: true, isPlaying: playback.IsPlaying);
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
                _lastSmtcPlaybackPositionSeconds = null;
                _smtcLyricTimingController.Reset();
                _neteaseLyricTimingController.Reset();
                if (!string.IsNullOrEmpty(_lastLyricsPreviewLine))
                {
                    _lastLyricsPreviewLine = string.Empty;
                Dispatcher.BeginInvoke(() => _overlayWindow.SetLyrics(null), DispatcherPriority.Background);
                }

                if (!string.IsNullOrEmpty(_lastDisplayTrackKey))
                {
                    SetTextIfChanged(CurrentTitle, useSmtc ? "未检测到系统媒体会话" : "未检测到网易云歌曲");
                    SetTextIfChanged(CurrentArtist, useSmtc ? "请先播放任意媒体内容" : "请打开网易云音乐并播放歌曲");
                    SetTextIfChanged(CurrentMeta, useSmtc
                        ? "来源：SMTC"
                        : $"来源：{NeteaseCoverDiagnosticPolicy.FormatSourceAppId("CloudMusic(ProcessTitle)", NeteaseCoverDiagnosticPolicy.WindowTitleMissing)}");
                    SetTextIfChanged(FooterSourceText, CurrentMeta.Text);
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
                    _lyricsService.Reset();
                    Dispatcher.BeginInvoke(() => _overlayWindow.SetLyrics(null), DispatcherPriority.Background);
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
                            Dispatcher.BeginInvoke(() => UpdateLyricsPreviewAfterFetch(line), DispatcherPriority.Background);
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
                    _lyricsService.Reset();
                    _neteaseLyricTimingController.Reset();
                    _neteaseLyricTimingController.Start();
                    Dispatcher.BeginInvoke(() => _overlayWindow.SetLyrics(null), DispatcherPriority.Background);
                    _lastLyricsPreviewLine = string.Empty;
                    SetTextIfChanged(LyricsPreviewText, UiText.LyricsPreviewPlaceholder);
                }
                else if (!_neteaseLyricTimingController.HasState)
                {
                    _neteaseLyricTimingController.Start();
                }

                double startTime = _neteaseLyricTimingController.GetCurrentPositionSeconds();
                _lyricsService.SetPlaybackPosition(startTime);
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
                            Dispatcher.BeginInvoke(() => UpdateLyricsPreviewAfterFetch(line), DispatcherPriority.Background);
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
                _lyricsService.Reset();
                _lastSmtcPlaybackPositionSeconds = null;
                _smtcLyricTimingController.Reset();
                _neteaseLyricTimingController.Reset();
                if (!string.IsNullOrEmpty(_lastLyricsPreviewLine))
                {
                    _lastLyricsPreviewLine = string.Empty;
                    Dispatcher.BeginInvoke(() => _overlayWindow.SetLyrics(null), DispatcherPriority.Background);
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
            return;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            _overlayWindow.SetLyrics(null);
            _lastLyricsPreviewLine = string.Empty;
            SetTextIfChanged(LyricsPreviewText, "歌词已获取，等待同步到当前时间点。");
            return;
        }

        _overlayWindow.SetLyrics(line);
        _lastLyricsPreviewLine = line;
        SetTextIfChanged(LyricsPreviewText, line);
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

    private Task<RemoteControlStatus> GetRemoteControlStatusAsync()
    {
        return Dispatcher.InvokeAsync(() =>
        {
            string title = _nowPlayingPageView?.CurrentTitle.Text ?? string.Empty;
            string artist = _nowPlayingPageView?.CurrentArtist.Text ?? string.Empty;
            return new RemoteControlStatus
            {
                Title = title,
                Artist = artist,
                Source = IsSmtcSource() ? "SMTC" : "网易云窗口标题",
                SupportsSmtc = RuntimeFeatureSupport.SupportsSmtc(),
                OverlayVisible = _overlayWindow.IsContentVisible,
                RemoteEnabled = _activeSettings.EnableRemoteControl
            };
        }).Task;
    }

    private Task HandleRemoteControlActionAsync(string action)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            switch (action)
            {
                case "previous":
                    await PrevAsync();
                    break;
                case "next":
                    await NextAsync();
                    break;
                case "togglePlayPause":
                    await TogglePlayPauseAsync();
                    break;
                case "toggleOverlay":
                    await ToggleOverlayVisibilityAsync();
                    break;
            }
        }).Task.Unwrap();
    }

    private string GetRemoteDisplayUrl()
    {
        return _activeSettings.RemoteControlAllowLan ? _remoteControlService.LanUrl : _remoteControlService.LocalUrl;
    }

    private void UpdateRemoteControlPage()
    {
        if (_remoteControlPageView == null)
        {
            return;
        }

        string address = GetRemoteDisplayUrl();
        _remoteControlPageView.RemoteControlStatusText.Text = _activeSettings.EnableRemoteControl
            ? _remoteControlService.StatusMessage
            : "未启用";
        _remoteControlPageView.RemoteControlAddressBox.Text = _activeSettings.EnableRemoteControl ? address : string.Empty;
        _remoteControlPageView.RemoteControlHelpText.Text = BuildRemoteControlHelpText();
        _remoteControlPageView.SetQrCode(_activeSettings.EnableRemoteControl ? address : string.Empty);
    }

    private string BuildRemoteControlHelpText()
    {
        if (!_activeSettings.EnableRemoteControl)
        {
            return "启用后会生成手机访问地址和二维码。";
        }

        if (!_remoteControlService.IsRunning)
        {
            return "服务未运行。请检查端口是否被占用，或换一个端口后重新启用。";
        }

        if (!_activeSettings.RemoteControlAllowLan)
        {
            return "当前只允许本机访问。若要用手机连接，请勾选允许局域网手机访问。";
        }

        IReadOnlyList<string> urls = _remoteControlService.LanUrls;
        if (urls.Count == 0)
        {
            return "未检测到可用局域网 IPv4。请确认电脑已连接 Wi-Fi/网线，且网络不是仅本机或虚拟网卡。";
        }

        if (urls.Count == 1)
        {
            return "手机打不开时，请确认手机和电脑在同一 Wi-Fi，并允许 Windows 防火墙放行本程序。";
        }

        return "备用地址：" + string.Join("  ", urls.Skip(1)) + "。手机打不开首选地址时可尝试备用地址。";
    }

    private void InitializeOverlayControls(OverlaySettings settings)
    {
        _isInitializingOverlayControls = true;
        try
        {
            if (_floatingSettingsPageView != null)
            {
                SelectTrackSource(settings.TrackSource);
                _floatingSettingsPageView.HorizontalSlider.Value = settings.LeftPercent * 100.0;
                _floatingSettingsPageView.BottomOffsetSlider.Value = settings.TopPercent * 100.0;
                _floatingSettingsPageView.ScaleSlider.Value = settings.Scale * 100.0;
                _floatingSettingsPageView.MinimizeToTrayCheckBox.IsChecked = settings.MinimizeToTray;
                _floatingSettingsPageView.AutoStartCheckBox.IsChecked = settings.AutoStartOnBoot;
                _floatingSettingsPageView.AlwaysShowCheckBox.IsChecked = settings.AlwaysShowOverlay;
                _floatingSettingsPageView.HideOverlayWhenPausedCheckBox.IsChecked = settings.HideOverlayWhenPaused;
                _floatingSettingsPageView.DiagnosticCheckBox.IsChecked = settings.DiagnosticMode;
                _floatingSettingsPageView.EnableLyricsCheckBox.IsChecked = settings.EnableLyrics;
                _floatingSettingsPageView.EnableCoverWingEffectCheckBox.IsChecked = settings.EnableCoverWingEffect;
            }

            if (_hotkeySettingsPageView != null)
            {
                _hotkeySettingsPageView.AppPrevHotkeyBox.Text = settings.AppPrevHotkey;
                _hotkeySettingsPageView.AppNextHotkeyBox.Text = settings.AppNextHotkey;
                _hotkeySettingsPageView.AppToggleHotkeyBox.Text = settings.AppToggleHotkey;
                _hotkeySettingsPageView.AppToggleOverlayHotkeyBox.Text = settings.AppToggleOverlayHotkey;
                _hotkeySettingsPageView.NeteasePrevHotkeyBox.Text = settings.NeteasePrevHotkey;
                _hotkeySettingsPageView.NeteaseNextHotkeyBox.Text = settings.NeteaseNextHotkey;
                _hotkeySettingsPageView.NeteaseToggleHotkeyBox.Text = settings.NeteaseToggleHotkey;
                _hotkeySettingsPageView.EnableGamepadCheckBox.IsChecked = settings.EnableGamepadHotkeys;
                _hotkeySettingsPageView.GamepadPrevHotkeyBox.Text = settings.GamepadPrevHotkey;
                _hotkeySettingsPageView.GamepadNextHotkeyBox.Text = settings.GamepadNextHotkey;
                _hotkeySettingsPageView.GamepadToggleHotkeyBox.Text = settings.GamepadToggleHotkey;
                _hotkeySettingsPageView.GamepadToggleOverlayHotkeyBox.Text = settings.GamepadToggleOverlayHotkey;
            }

            if (_remoteControlPageView != null)
            {
                _remoteControlPageView.EnableRemoteControlCheckBox.IsChecked = settings.EnableRemoteControl;
                _remoteControlPageView.RemoteControlPortBox.Text = settings.RemoteControlPort.ToString();
                _remoteControlPageView.RemoteControlAllowLanCheckBox.IsChecked = settings.RemoteControlAllowLan;
            }

            if (_themeSettingsPageView != null)
            {
                SelectTitleColor(settings.TitleColor);
                SelectArtistColor(settings.ArtistColor);
                SelectLyricsColor(settings.LyricsColor);
                _themeSettingsPageView.TitleOpacitySlider.Value = settings.TitleOpacity * 100.0;
                _themeSettingsPageView.ArtistOpacitySlider.Value = settings.ArtistOpacity * 100.0;
                _themeSettingsPageView.LyricsOpacitySlider.Value = settings.LyricsOpacity * 100.0;
            }

            ApplyDisplayColors(settings);
            UpdateOverlayControlLabels();
            UpdateRuntimeDependentControls();
            UpdateRemoteControlPage();
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
        _lastSmtcPlaybackPositionSeconds = null;
        _smtcLyricTimingController.Reset();
        _neteaseLyricTimingController.Reset();
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
            _remoteControlService.ApplySettings(_activeSettings);
            UpdateRemoteControlPage();
            SetStatus($"状态：已保存并应用。{_overlaySettingsService.SettingsFilePath}", false);
        }
        catch (Exception ex)
        {
            SetStatus($"状态：保存失败。{ex.Message}", true);
        }
    }

    private async void SettingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
        _diagnostic.Enabled = _activeSettings.DiagnosticMode;
        _overlaySettingsService.Save(_activeSettings);
        _remoteControlService.ApplySettings(_activeSettings);
        UpdateRemoteControlPage();
        await ApplyPauseOverlayVisibilityRuleAsync();
    }

    private void RemoteControlSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
        _overlaySettingsService.Save(_activeSettings);
        _remoteControlService.ApplySettings(_activeSettings);
        UpdateRemoteControlPage();
        SetStatus(_remoteControlService.IsRunning ? "状态：手机遥控已应用。" : $"状态：{_remoteControlService.StatusMessage}", !_remoteControlService.IsRunning && _activeSettings.EnableRemoteControl);
    }

    private void CopyRemoteAddress_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string address = _remoteControlPageView?.RemoteControlAddressBox.Text ?? GetRemoteDisplayUrl();
            if (string.IsNullOrWhiteSpace(address))
            {
                SetStatus("状态：手机遥控地址为空，请先启用遥控服务。", true);
                return;
            }

            Clipboard.SetText(address);
            SetStatus("状态：手机遥控地址已复制。", false);
        }
        catch (Exception ex)
        {
            SetStatus($"状态：复制手机遥控地址失败。{ex.Message}", true);
        }
    }

    private void ResetRemoteToken_Click(object sender, RoutedEventArgs e)
    {
        _activeSettings.RemoteControlToken = RemoteControlService.GenerateToken();
        _overlaySettingsService.Save(_activeSettings);
        _remoteControlService.ApplySettings(_activeSettings);
        UpdateRemoteControlPage();
        SetStatus("状态：手机遥控连接令牌已重置。", false);
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
        if (!AreHotkeysValid(_activeSettings))
        {
            SetStatus("状态：快捷键格式无效，示例 Ctrl+Shift+H 或 Back+Start。", true);
            return;
        }

        if (!RebindGlobalHotkeys())
        {
            SetStatus("状态：应用快捷键注册失败，请换一组按键。", true);
            return;
        }

        _overlaySettingsService.Save(_activeSettings);
        SetStatus("状态：快捷键已应用。", false);
    }

    private void ResetOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
        _activeSettings = new OverlaySettings();
        _smtcLyricTimingController.SetDelayOverrideMilliseconds(_activeSettings.SmtcLyricDelayOverrideMs);
        _overlayWindow.ApplySettings(_activeSettings);
        _remoteControlService.ApplySettings(_activeSettings);
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

    private void BilibiliLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
            SetStatus("状态：无法打开哔哩哔哩个人空间。", false);
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
        ConfigureKeyboardHotkeyInput(AppToggleOverlayHotkeyBox);
        ConfigureKeyboardHotkeyInput(NeteasePrevHotkeyBox);
        ConfigureKeyboardHotkeyInput(NeteaseNextHotkeyBox);
        ConfigureKeyboardHotkeyInput(NeteaseToggleHotkeyBox);

        ConfigureGamepadHotkeyInput(GamepadPrevHotkeyBox);
        ConfigureGamepadHotkeyInput(GamepadNextHotkeyBox);
        ConfigureGamepadHotkeyInput(GamepadToggleHotkeyBox);
        ConfigureGamepadHotkeyInput(GamepadToggleOverlayHotkeyBox);
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
        _gamepadCaptureBaseline[textBox] = GamepadInputService.GetCurrentButtonsSnapshot();
        _gamepadCaptureCandidates.Remove(textBox);
        _gamepadCaptureCandidateCounts.Remove(textBox);
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
        _gamepadCaptureBaseline.Remove(textBox);
        _gamepadCaptureCandidates.Remove(textBox);
        _gamepadCaptureCandidateCounts.Remove(textBox);

        _gamepadCaptureFocusCount = Math.Max(0, _gamepadCaptureFocusCount - 1);
        if (_gamepadCaptureFocusCount == 0)
        {
            _gamepadInputService.Enabled = _gamepadEnabledBeforeCapture;
        }
    }

    private void DisposeGamepadHotkeyCaptureTimers()
    {
        foreach (DispatcherTimer timer in _gamepadCommitTimers.Values)
        {
            timer.Stop();
        }

        _gamepadCommitTimers.Clear();
        _gamepadPressed.Clear();
        _gamepadCaptureBaseline.Clear();
        _gamepadCaptureCandidates.Clear();
        _gamepadCaptureCandidateCounts.Clear();
        _gamepadCaptureFocusCount = 0;
        _gamepadInputService.Enabled = _gamepadEnabledBeforeCapture;
    }

    private void CaptureGamepadInput(TextBox textBox)
    {
        GamepadButton currentButtons = GamepadInputService.GetCurrentButtonsSnapshot();
        if (currentButtons == GamepadButton.None)
        {
            _gamepadCaptureCandidates.Remove(textBox);
            _gamepadCaptureCandidateCounts.Remove(textBox);
            return;
        }

        if (_gamepadCaptureBaseline.TryGetValue(textBox, out GamepadButton baseline) &&
            currentButtons == baseline)
        {
            return;
        }

        if (!_gamepadCaptureCandidates.TryGetValue(textBox, out GamepadButton candidate) ||
            candidate != currentButtons)
        {
            _gamepadCaptureCandidates[textBox] = currentButtons;
            _gamepadCaptureCandidateCounts[textBox] = 1;
            return;
        }

        int count = _gamepadCaptureCandidateCounts.TryGetValue(textBox, out int previousCount)
            ? previousCount + 1
            : 1;
        _gamepadCaptureCandidateCounts[textBox] = count;
        if (count < 2)
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
        if (_floatingSettingsPageView == null)
        {
            return;
        }

        foreach (object item in _floatingSettingsPageView.TrackSourceComboBox.Items)
        {
            if (item is ComboBoxItem combo &&
                combo.Tag is string tag &&
                string.Equals(tag, trackSource, StringComparison.OrdinalIgnoreCase))
            {
                _floatingSettingsPageView.TrackSourceComboBox.SelectedItem = combo;
                return;
            }
        }

        _floatingSettingsPageView.TrackSourceComboBox.SelectedIndex = 0;
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
        if (_floatingSettingsPageView == null)
        {
            return;
        }

        bool smtcSupported = RuntimeFeatureSupport.SupportsSmtc();
        ComboBoxItem? smtcItem = null;
        foreach (object item in _floatingSettingsPageView.TrackSourceComboBox.Items)
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

        string selectedSource = GetSelectedTrackSource();
        bool useSmtc = smtcSupported &&
            string.Equals(selectedSource, "SMTC", StringComparison.OrdinalIgnoreCase);

        _floatingSettingsPageView.EnableLyricsCheckBox.IsEnabled = TrackSourcePolicy.ShouldEnableLyrics(selectedSource);
        _floatingSettingsPageView.EnableLyricsCheckBox.ToolTip = TrackSourcePolicy.GetLyricsTooltip(useSmtc);
    }

    private string GetSelectedTrackSource()
    {
        if (_floatingSettingsPageView?.TrackSourceComboBox.SelectedItem is ComboBoxItem combo && combo.Tag is string tag)
        {
            return tag;
        }

        return "NeteaseProcess";
    }

    private void ApplyOverlaySettingsFromControls()
    {
        OverlaySettings settings = new()
        {
            TrackSource = _activeSettings.TrackSource,
            LeftPercent = _activeSettings.LeftPercent,
            TopPercent = _activeSettings.TopPercent,
            Scale = _activeSettings.Scale,
            AppPrevHotkey = _activeSettings.AppPrevHotkey,
            AppNextHotkey = _activeSettings.AppNextHotkey,
            AppToggleHotkey = _activeSettings.AppToggleHotkey,
            AppToggleOverlayHotkey = _activeSettings.AppToggleOverlayHotkey,
            NeteasePrevHotkey = _activeSettings.NeteasePrevHotkey,
            NeteaseNextHotkey = _activeSettings.NeteaseNextHotkey,
            NeteaseToggleHotkey = _activeSettings.NeteaseToggleHotkey,
            EnableGamepadHotkeys = _activeSettings.EnableGamepadHotkeys,
            GamepadPrevHotkey = _activeSettings.GamepadPrevHotkey,
            GamepadNextHotkey = _activeSettings.GamepadNextHotkey,
            GamepadToggleHotkey = _activeSettings.GamepadToggleHotkey,
            GamepadToggleOverlayHotkey = _activeSettings.GamepadToggleOverlayHotkey,
            EnableRemoteControl = _activeSettings.EnableRemoteControl,
            RemoteControlPort = _activeSettings.RemoteControlPort,
            RemoteControlToken = _activeSettings.RemoteControlToken,
            RemoteControlAllowLan = _activeSettings.RemoteControlAllowLan,
            MinimizeToTray = _activeSettings.MinimizeToTray,
            AutoStartOnBoot = _activeSettings.AutoStartOnBoot,
            AlwaysShowOverlay = _activeSettings.AlwaysShowOverlay,
            HideOverlayWhenPaused = _activeSettings.HideOverlayWhenPaused,
            DiagnosticMode = _activeSettings.DiagnosticMode,
            EnableLyrics = _activeSettings.EnableLyrics,
            EnableCoverWingEffect = _activeSettings.EnableCoverWingEffect,
            TitleColor = _activeSettings.TitleColor,
            ArtistColor = _activeSettings.ArtistColor,
            LyricsColor = _activeSettings.LyricsColor,
            TitleOpacity = _activeSettings.TitleOpacity,
            ArtistOpacity = _activeSettings.ArtistOpacity,
            LyricsOpacity = _activeSettings.LyricsOpacity,
            SmtcLyricDelayOverrideMs = _activeSettings.SmtcLyricDelayOverrideMs
        };

        if (_floatingSettingsPageView != null)
        {
            settings.TrackSource = GetSelectedTrackSource();
            settings.LeftPercent = _floatingSettingsPageView.HorizontalSlider.Value / 100.0;
            settings.TopPercent = _floatingSettingsPageView.BottomOffsetSlider.Value / 100.0;
            settings.Scale = _floatingSettingsPageView.ScaleSlider.Value / 100.0;
            settings.MinimizeToTray = _floatingSettingsPageView.MinimizeToTrayCheckBox.IsChecked == true;
            settings.AutoStartOnBoot = _floatingSettingsPageView.AutoStartCheckBox.IsChecked == true;
            settings.AlwaysShowOverlay = _floatingSettingsPageView.AlwaysShowCheckBox.IsChecked == true;
            settings.HideOverlayWhenPaused = _floatingSettingsPageView.HideOverlayWhenPausedCheckBox.IsChecked == true;
            settings.DiagnosticMode = _floatingSettingsPageView.DiagnosticCheckBox.IsChecked == true;
            settings.EnableLyrics = _floatingSettingsPageView.EnableLyricsCheckBox.IsChecked == true;
            settings.EnableCoverWingEffect = _floatingSettingsPageView.EnableCoverWingEffectCheckBox.IsChecked == true;
        }

        if (_hotkeySettingsPageView != null)
        {
            settings.AppPrevHotkey = _hotkeySettingsPageView.AppPrevHotkeyBox.Text.Trim();
            settings.AppNextHotkey = _hotkeySettingsPageView.AppNextHotkeyBox.Text.Trim();
            settings.AppToggleHotkey = _hotkeySettingsPageView.AppToggleHotkeyBox.Text.Trim();
            settings.AppToggleOverlayHotkey = _hotkeySettingsPageView.AppToggleOverlayHotkeyBox.Text.Trim();
            settings.NeteasePrevHotkey = _hotkeySettingsPageView.NeteasePrevHotkeyBox.Text.Trim();
            settings.NeteaseNextHotkey = _hotkeySettingsPageView.NeteaseNextHotkeyBox.Text.Trim();
            settings.NeteaseToggleHotkey = _hotkeySettingsPageView.NeteaseToggleHotkeyBox.Text.Trim();
            settings.EnableGamepadHotkeys = _hotkeySettingsPageView.EnableGamepadCheckBox.IsChecked == true;
            settings.GamepadPrevHotkey = _hotkeySettingsPageView.GamepadPrevHotkeyBox.Text.Trim();
            settings.GamepadNextHotkey = _hotkeySettingsPageView.GamepadNextHotkeyBox.Text.Trim();
            settings.GamepadToggleHotkey = _hotkeySettingsPageView.GamepadToggleHotkeyBox.Text.Trim();
            settings.GamepadToggleOverlayHotkey = _hotkeySettingsPageView.GamepadToggleOverlayHotkeyBox.Text.Trim();
        }

        if (_remoteControlPageView != null)
        {
            settings.EnableRemoteControl = _remoteControlPageView.EnableRemoteControlCheckBox.IsChecked == true;
            settings.RemoteControlAllowLan = _remoteControlPageView.RemoteControlAllowLanCheckBox.IsChecked == true;
            if (int.TryParse(_remoteControlPageView.RemoteControlPortBox.Text.Trim(), out int remotePort))
            {
                settings.RemoteControlPort = remotePort;
            }
        }

        if (_themeSettingsPageView != null)
        {
            settings.TitleColor = GetSelectedTitleColor();
            settings.ArtistColor = GetSelectedArtistColor();
            settings.LyricsColor = GetSelectedLyricsColor();
            settings.TitleOpacity = _themeSettingsPageView.TitleOpacitySlider.Value / 100.0;
            settings.ArtistOpacity = _themeSettingsPageView.ArtistOpacitySlider.Value / 100.0;
            settings.LyricsOpacity = _themeSettingsPageView.LyricsOpacitySlider.Value / 100.0;
        }

        _activeSettings = OverlaySettingsService.NormalizeForTests(settings);
        _smtcLyricTimingController.SetDelayOverrideMilliseconds(settings.SmtcLyricDelayOverrideMs);
        _overlayWindow.ApplySettings(_activeSettings);
        ApplyGamepadSettings(_activeSettings);
        ApplyAutoStart(_activeSettings.AutoStartOnBoot);
        ApplyDisplayColors(_activeSettings);
        UpdateOverlayControlLabels();
        UpdateRemoteControlPage();
    }

    private void UpdateOverlayControlLabels()
    {
        if (_floatingSettingsPageView != null)
        {
            _floatingSettingsPageView.HorizontalValueText.Text = $"{_floatingSettingsPageView.HorizontalSlider.Value:0}%";
            _floatingSettingsPageView.BottomOffsetValueText.Text = $"{_floatingSettingsPageView.BottomOffsetSlider.Value:0}%";
            _floatingSettingsPageView.ScaleValueText.Text = $"{_floatingSettingsPageView.ScaleSlider.Value:0}%";
        }

        if (_themeSettingsPageView != null)
        {
            _themeSettingsPageView.TitleOpacityValueText.Text = $"{_themeSettingsPageView.TitleOpacitySlider.Value:0}%";
            _themeSettingsPageView.ArtistOpacityValueText.Text = $"{_themeSettingsPageView.ArtistOpacitySlider.Value:0}%";
            _themeSettingsPageView.LyricsOpacityValueText.Text = $"{_themeSettingsPageView.LyricsOpacitySlider.Value:0}%";
        }

        if (_remoteControlPageView != null)
        {
            _remoteControlPageView.RemoteControlPortBox.Text = _activeSettings.RemoteControlPort.ToString();
        }
    }

    private void SelectTitleColor(string color)
    {
        if (_themeSettingsPageView == null)
        {
            return;
        }

        color = color.ToUpperInvariant();
        _themeSettingsPageView.TitleColor_White.IsChecked = color == "#FFFFFF";
        _themeSettingsPageView.TitleColor_Light.IsChecked = color == "#D0E0F0";
        _themeSettingsPageView.TitleColor_Yellow.IsChecked = color == "#F0E080";
        _themeSettingsPageView.TitleColor_Green.IsChecked = color == "#90EE90";
        _themeSettingsPageView.TitleColor_Orange.IsChecked = color == "#FFB366";
    }

    private void SelectArtistColor(string color)
    {
        if (_themeSettingsPageView == null)
        {
            return;
        }

        color = color.ToUpperInvariant();
        _themeSettingsPageView.ArtistColor_Light.IsChecked = color == "#C0D0E0";
        _themeSettingsPageView.ArtistColor_White.IsChecked = color == "#FFFFFF";
        _themeSettingsPageView.ArtistColor_Yellow.IsChecked = color == "#F0E080";
        _themeSettingsPageView.ArtistColor_Green.IsChecked = color == "#90EE90";
        _themeSettingsPageView.ArtistColor_Orange.IsChecked = color == "#FFB366";
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
        if (_themeSettingsPageView == null)
        {
            return;
        }

        color = color.ToUpperInvariant();
        _themeSettingsPageView.LyricsColor_Light.IsChecked = color == "#A0B8D0";
        _themeSettingsPageView.LyricsColor_White.IsChecked = color == "#FFFFFF";
        _themeSettingsPageView.LyricsColor_Yellow.IsChecked = color == "#F0E080";
        _themeSettingsPageView.LyricsColor_Green.IsChecked = color == "#90EE90";
        _themeSettingsPageView.LyricsColor_Orange.IsChecked = color == "#FFB366";
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
        if (_nowPlayingPageView != null)
        {
            _nowPlayingPageView.CurrentTitle.ClearValue(TextBlock.ForegroundProperty);
            _nowPlayingPageView.CurrentTitle.ClearValue(UIElement.OpacityProperty);
            _nowPlayingPageView.CurrentArtist.ClearValue(TextBlock.ForegroundProperty);
            _nowPlayingPageView.CurrentArtist.ClearValue(UIElement.OpacityProperty);
        }

        if (_themeSettingsPageView == null)
        {
            return;
        }

        try
        {
            var titleColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.TitleColor);
            var titleBrush = new System.Windows.Media.SolidColorBrush(titleColor);
            _themeSettingsPageView.ThemePreviewTitle.Foreground = titleBrush.Clone();
            _themeSettingsPageView.ThemePreviewTitle.Opacity = settings.TitleOpacity;
        }
        catch { }

        try
        {
            var artistColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.ArtistColor);
            var artistBrush = new System.Windows.Media.SolidColorBrush(artistColor);
            _themeSettingsPageView.ThemePreviewArtist.Foreground = artistBrush.Clone();
            _themeSettingsPageView.ThemePreviewArtist.Opacity = settings.ArtistOpacity;
        }
        catch { }

        try
        {
            var lyricsColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.LyricsColor);
            _themeSettingsPageView.ThemePreviewLyrics.Foreground = new System.Windows.Media.SolidColorBrush(lyricsColor);
            _themeSettingsPageView.ThemePreviewLyrics.Opacity = settings.LyricsOpacity;
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
        _hotkeyService.TogglePlayPauseRequested += async (_, _) => await TogglePlayPauseAsync();
        _hotkeyService.ToggleOverlayRequested += async (_, _) => await ToggleOverlayVisibilityAsync();

        return _hotkeyService.Register(
            _activeSettings.AppPrevHotkey,
            _activeSettings.AppNextHotkey,
            _activeSettings.AppToggleHotkey,
            _activeSettings.AppToggleOverlayHotkey);
    }

    private static bool AreHotkeysValid(OverlaySettings settings)
    {
        bool keyboardOk = HotkeyParser.TryParse(settings.AppPrevHotkey, out _) &&
                          HotkeyParser.TryParse(settings.AppNextHotkey, out _) &&
                          HotkeyParser.TryParse(settings.AppToggleHotkey, out _) &&
                          HotkeyParser.TryParse(settings.AppToggleOverlayHotkey, out _) &&
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
               GamepadHotkeyParser.TryParse(settings.GamepadToggleHotkey, out _) &&
               GamepadHotkeyParser.TryParse(settings.GamepadToggleOverlayHotkey, out _);
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

        if (GamepadHotkeyParser.TryParse(settings.GamepadToggleOverlayHotkey, out var toggleOverlay))
        {
            _gamepadInputService.ToggleOverlayHotkey = toggleOverlay;
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
