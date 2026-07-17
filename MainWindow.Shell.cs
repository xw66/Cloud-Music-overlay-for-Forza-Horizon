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
            _floatingSettingsPageView.EnableNeteaseMemoryTimelineCheckBox.Checked += SettingCheckBox_Changed;
            _floatingSettingsPageView.EnableNeteaseMemoryTimelineCheckBox.Unchecked += SettingCheckBox_Changed;
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
            _aboutPageView.VersionText.Text = $"{UiText.VersionPrefix} {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "3.0.1"}";
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
        UpdatePlaybackHealthText(_playbackSession.LatestSnapshot);
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
}
