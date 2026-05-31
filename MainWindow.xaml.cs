using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtMainwindow = 2;
    private readonly NeteaseLocalDataService _neteaseLocalDataService;
    private readonly SmtcTrackService _smtcTrackService;
    private readonly OverlaySettingsService _overlaySettingsService;
    private readonly OverlayWindow _overlayWindow;
    private readonly NeteaseShortcutSender _neteaseShortcutSender;
    private readonly GamepadInputService _gamepadInputService;
    private readonly UpdateService _updateService;
    private readonly DispatcherTimer _pollTimer;

    private GlobalHotkeyService? _hotkeyService;
    private bool _isPolling;
    private bool _isInitializingOverlayControls;
    private OverlaySettings _activeSettings;
    private string _lastTrackKey = string.Empty;
    private string _lastDisplayTrackKey = string.Empty;
    private byte[]? _lastPreviewCoverBytes;
    private string _lastStatusText = string.Empty;
    private readonly Dictionary<TextBox, HashSet<GamepadButton>> _gamepadPressed = new();
    private readonly Dictionary<TextBox, DispatcherTimer> _gamepadCommitTimers = new();
    private bool _gamepadEnabledBeforeCapture;
    private int _gamepadCaptureFocusCount;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _isRealExit;

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

    public MainWindow(bool startMinimized = false)
    {
        _isInitializingOverlayControls = true;

        _neteaseLocalDataService = new NeteaseLocalDataService();
        _smtcTrackService = new SmtcTrackService();
        _overlaySettingsService = new OverlaySettingsService();
        _overlayWindow = new OverlayWindow();
        _neteaseShortcutSender = new NeteaseShortcutSender();
        _gamepadInputService = new GamepadInputService();
        _updateService = new UpdateService();
        OverlaySettings loadedSettings = _overlaySettingsService.Load();
        _activeSettings = loadedSettings;
        _overlayWindow.ApplySettings(loadedSettings);
        ApplyGamepadSettings(loadedSettings);

        InitializeComponent();
        InitializeTrayIcon();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _pollTimer.Tick += PollTimer_Tick;

        _gamepadInputService.PrevTriggered += async (_, _) => await PrevAsync();
        _gamepadInputService.NextTriggered += async (_, _) => await NextAsync();
        _gamepadInputService.ToggleTriggered += (_, _) => TogglePlayPause();
        _gamepadInputService.Start();

        InitializeOverlayControls(loadedSettings);
        SetupHotkeyCaptureInputs();

        LoadEmbeddedResources();

        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;

        if (startMinimized)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
            SourceInitialized += (_, _) =>
            {
                if (_trayIcon != null) _trayIcon.Visible = true;
            };
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
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    iconStream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                Icon = decoder.Frames[0];
            }
        }
        catch
        {
        }

        try
        {
            var imgUri = new Uri("pack://application:,,,/Assets/Icons/icons8-github-50.png");
            var imgStream = Application.GetResourceStream(imgUri)?.Stream;
            if (imgStream != null && GitHubImage != null)
            {
                var img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit();
                img.StreamSource = imgStream;
                img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                img.EndInit();
                GitHubImage.Source = img;
            }
        }
        catch
        {
        }
    }

    private void InitializeTrayIcon()
    {
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
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (System.IO.File.Exists(iconPath))
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
        if (_isRealExit || !_activeSettings.MinimizeToTray)
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void TitleColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string color)
        {
            TitleColorBox.Text = color;
            if (!_isInitializingOverlayControls)
            {
                ApplyOverlaySettingsFromControls();
            }
        }
    }

    private void ArtistColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string color)
        {
            ArtistColorBox.Text = color;
            if (!_isInitializingOverlayControls)
            {
                ApplyOverlaySettingsFromControls();
            }
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int backdropType = DwmsbtMainwindow;
        _ = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdropType, sizeof(int));

        EnsureAutoStartRegistry(_activeSettings);

        bool ok = RebindGlobalHotkeys();
        if (ok)
        {
            SetStatus("状态：快捷键已就绪，按应用快捷键会转发网易云快捷键。", false);
            _ = RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
            _pollTimer.Start();
        }
        else
        {
            SetStatus("状态：应用快捷键注册失败（被占用或格式无效）。", false);
            _ = RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
            _pollTimer.Start();
        }

        _ = SilentCheckUpdateAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            _overlaySettingsService.Save(_activeSettings);
        }
        catch
        {
        }

        _hotkeyService?.Dispose();
        _pollTimer.Stop();
        _gamepadInputService.Dispose();
        _updateService.Dispose();
        _overlayWindow.Close();
        _trayIcon?.Dispose();
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
        }
        finally
        {
            _isPolling = false;
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
        await Task.Delay(650);
        await RefreshCurrentTrackAsync(showOverlay: true, allowOverlayOnTrackChange: false);
    }

    private async Task RefreshCurrentTrackAsync(bool showOverlay, bool allowOverlayOnTrackChange)
    {
        try
        {
            bool useSmtc = string.Equals(_activeSettings.TrackSource, "SMTC", StringComparison.OrdinalIgnoreCase);
            TrackInfo? track = useSmtc
                ? await _smtcTrackService.GetCurrentTrackAsync()
                : await _neteaseLocalDataService.GetCurrentTrackAsync();

            if (track == null)
            {
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

            string currentTrackKey = $"{track.Name}|{track.Artist}";
            bool changed = !string.Equals(_lastTrackKey, currentTrackKey, StringComparison.Ordinal);
            _lastTrackKey = currentTrackKey;

            bool displayChanged = !string.Equals(_lastDisplayTrackKey, currentTrackKey, StringComparison.Ordinal);
            if (displayChanged)
            {
                CurrentTitle.Text = track.Name;
                CurrentArtist.Text = track.Artist;
                CurrentMeta.Text = $"来源：{track.SourceAppId}";
                SetCover(track.CoverBytes);
                _lastDisplayTrackKey = currentTrackKey;
            }

            if (showOverlay || (allowOverlayOnTrackChange && changed))
            {
                await _overlayWindow.ShowTrackAsync(track);
            }

            SetStatus(useSmtc ? "状态：已从 SMTC 同步。" : "状态：已从网易云窗口标题同步。", false);
        }
        catch (Exception ex)
        {
            SetStatus($"状态：读取歌曲数据失败。{ex.Message}", true);
        }
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

    private void InitializeOverlayControls()
    {
        InitializeOverlayControls(_overlayWindow.CurrentSettings);
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
            TitleColorBox.Text = settings.TitleColor;
            ArtistColorBox.Text = settings.ArtistColor;
            SelectColorRadio(TitleColorBox.Text, true);
            SelectColorRadio(ArtistColorBox.Text, false);
            TitleOpacitySlider.Value = settings.TitleOpacity * 100.0;
            ArtistOpacitySlider.Value = settings.ArtistOpacity * 100.0;
            UpdateOverlayControlLabels();
            UpdateColorLabels();
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
        _lastTrackKey = string.Empty;
        _lastDisplayTrackKey = string.Empty;
        _lastPreviewCoverBytes = null;
        SetCover(null);

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

    private void ResetOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
        _activeSettings = new OverlaySettings();
        _overlayWindow.ApplySettings(_activeSettings);
        InitializeOverlayControls();
        SetStatus("状态：已重置为默认值，点击“保存”后生效并持久化。", false);
    }

    private async void TestOverlay_Click(object sender, RoutedEventArgs e)
    {
        TrackInfo previewTrack = new()
        {
            Name = "正在播放",
            Artist = "网易云音乐",
            SourceAppId = "预览"
        };

        await _overlayWindow.ShowTrackAsync(previewTrack);
        SetStatus("状态：已显示悬浮窗预览。", false);
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
            Key.LeftCtrl or Key.RightCtrl or Key.LeftCtrl => "Ctrl",
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
            TitleColor = TitleColorBox.Text.Trim(),
            ArtistColor = ArtistColorBox.Text.Trim(),
            TitleOpacity = TitleOpacitySlider.Value / 100.0,
            ArtistOpacity = ArtistOpacitySlider.Value / 100.0
        };

        _activeSettings = settings;
        _overlayWindow.ApplySettings(settings);
        ApplyGamepadSettings(settings);
        ApplyAutoStart(settings.AutoStartOnBoot);
        UpdateOverlayControlLabels();
        UpdateColorLabels();
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
    }

    private void SelectColorRadio(string color, bool isTitle)
    {
        string name = isTitle ? $"TitleColor_" : "ArtistColor_";
        foreach (var child in ((WrapPanel)(isTitle ? TitleColor_White.Parent : ArtistColor_White.Parent)).Children)
        {
            if (child is RadioButton rb && string.Equals(rb.Tag as string, color, StringComparison.OrdinalIgnoreCase))
            {
                rb.IsChecked = true;
                return;
            }
        }
    }

    private void UpdateColorLabels()
    {
        if (TitleOpacityValueText == null || ArtistOpacityValueText == null)
        {
            return;
        }

        TitleOpacityValueText.Text = $"{TitleOpacitySlider.Value:0}%";
        ArtistOpacityValueText.Text = $"{ArtistOpacitySlider.Value:0}%";
    }

    private void TitleOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls)
        {
            return;
        }

        ApplyOverlaySettingsFromControls();
    }

    private void ArtistOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls)
        {
            return;
        }

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
                    key.SetValue("HorizonRadioOverlay", $"\"{exePath}\" --autostart");
                }
            }
            else
            {
                key.DeleteValue("HorizonRadioOverlay", false);
            }
        }
        catch
        {
            // Registry access failure is non-fatal.
        }
    }

    private static void EnsureAutoStartRegistry(OverlaySettings settings)
    {
        if (!settings.AutoStartOnBoot)
        {
            return;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", false);

            string? currentValue = key?.GetValue("HorizonRadioOverlay") as string;
            string? exePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(exePath))
            {
                return;
            }

            string correctValue = $"\"{exePath}\" --autostart";

            if (!string.Equals(currentValue, correctValue, StringComparison.Ordinal))
            {
                using var writeKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                writeKey?.SetValue("HorizonRadioOverlay", correctValue);
            }
        }
        catch
        {
        }
    }
}
