using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay;

public sealed partial class MainWindow : Window
{
    private readonly NeteaseLocalDataService _neteaseLocalDataService;
    private readonly SmtcTrackService _smtcTrackService;
    private readonly OverlaySettingsService _overlaySettingsService;
    private readonly OverlayWindow _overlayWindow;
    private readonly NeteaseShortcutSender _neteaseShortcutSender;
    private readonly GamepadInputService _gamepadInputService;
    private readonly UpdateService _updateService;

    private GlobalHotkeyService? _hotkeyService;
    private bool _isPolling;
    private bool _isInitializingOverlayControls;
    private OverlaySettings _activeSettings;
    private string _lastTrackKey = string.Empty;
    private string _lastDisplayTrackKey = string.Empty;
    private byte[]? _lastPreviewCoverBytes;
    private string _lastStatusText = string.Empty;
    private bool _isRealExit;
    private Microsoft.UI.Xaml.DispatcherTimer? _pollTimer;

    public MainWindow(bool startMinimized = false)
    {
        InitializeComponent();

        AppWindow.Title = "网易云悬浮窗";
        AppWindow.Resize(new SizeInt32(1200, 720));
        ExtendsContentIntoTitleBar = true;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
        }

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

        _pollTimer = new Microsoft.UI.Xaml.DispatcherTimer
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

        SetTitleBar(TitleBar);

        if (startMinimized)
        {
            ExtendsContentIntoTitleBar = true;
        }

        Closed += MainWindow_Closed;

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            bool ok = RebindGlobalHotkeys();
            if (ok)
            {
                SetStatus("状态：快捷键已就绪", false);
            }
            else
            {
                SetStatus("状态：应用快捷键注册失败", false);
            }

            _ = RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
            _pollTimer.Start();
            _ = SilentCheckUpdateAsync();
        });
    }

    private void LoadEmbeddedResources()
    {
        try
        {
            var stream = typeof(MainWindow).Assembly
                .GetManifestResourceStream("HorizonRadioOverlay.Assets.Icons.icons8-github-50.png");
            if (stream != null)
            {
                var bitmap = new BitmapImage();
                bitmap.SetSource(stream.AsRandomAccessStream());
                GitHubImage.Source = bitmap;
            }
        }
        catch { }
    }

    private void MainWindow_Closed(object? sender, WindowEventArgs e)
    {
        try { _overlaySettingsService.Save(_activeSettings); } catch { }
        _hotkeyService?.Dispose();
        _pollTimer?.Stop();
        _gamepadInputService.Dispose();
        _overlayWindow.Close();
    }

    private async void PollTimer_Tick(object? sender, object e)
    {
        if (_isPolling) return;
        _isPolling = true;
        try { await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: true); }
        finally { _isPolling = false; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshCurrentTrackAsync(showOverlay: true, allowOverlayOnTrackChange: false);
    private async void Prev_Click(object sender, RoutedEventArgs e) => await PrevAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await NextAsync();
    private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

    private async Task PrevAsync()
    {
        if (IsSmtcSource())
        {
            if (!await _smtcTrackService.PreviousAsync()) { SetStatus("状态：SMTC 上一首失败", false); return; }
        }
        else
        {
            if (!_neteaseShortcutSender.Send(_activeSettings.NeteasePrevHotkey)) { SetStatus("状态：发送快捷键失败", false); return; }
        }
        await RefreshAfterControlAsync();
    }

    private async Task NextAsync()
    {
        if (IsSmtcSource())
        {
            if (!await _smtcTrackService.NextAsync()) { SetStatus("状态：SMTC 下一首失败", false); return; }
        }
        else
        {
            if (!_neteaseShortcutSender.Send(_activeSettings.NeteaseNextHotkey)) { SetStatus("状态：发送快捷键失败", false); return; }
        }
        await RefreshAfterControlAsync();
    }

    private async void TogglePlayPause()
    {
        if (IsSmtcSource())
        {
            if (!await _smtcTrackService.TogglePlayPauseAsync()) { SetStatus("状态：SMTC 播放/暂停失败", false); return; }
        }
        else
        {
            if (!_neteaseShortcutSender.Send(_activeSettings.NeteaseToggleHotkey)) { SetStatus("状态：发送快捷键失败", false); return; }
        }
        SetStatus("状态：已发送播放/暂停指令", false);
        await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
    }

    private bool IsSmtcSource() => string.Equals(_activeSettings.TrackSource, "SMTC", StringComparison.OrdinalIgnoreCase);

    private async Task RefreshAfterControlAsync()
    {
        await Task.Delay(650);
        await RefreshCurrentTrackAsync(showOverlay: true, allowOverlayOnTrackChange: false);
    }

    private async Task RefreshCurrentTrackAsync(bool showOverlay, bool allowOverlayOnTrackChange)
    {
        try
        {
            bool useSmtc = IsSmtcSource();
            TrackInfo? track = useSmtc
                ? await _smtcTrackService.GetCurrentTrackAsync()
                : await _neteaseLocalDataService.GetCurrentTrackAsync();

            if (track == null)
            {
                if (!string.IsNullOrEmpty(_lastDisplayTrackKey))
                {
                    CurrentTitle.Text = useSmtc ? "未检测到系统媒体会话" : "未检测到网易云歌曲";
                    CurrentArtist.Text = useSmtc ? "请先播放任意媒体内容" : "请打开网易云音乐并播放";
                    CurrentMeta.Text = useSmtc ? "来源：SMTC" : "来源：CloudMusic";
                    SetCover(null);
                }
                SetStatus(useSmtc ? "状态：未读取到 SMTC" : "状态：未读取到网易云窗口标题", false);
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

            SetStatus(useSmtc ? "状态：已从 SMTC 同步" : "状态：已从网易云窗口标题同步", false);
        }
        catch (Exception ex)
        {
            SetStatus($"状态：读取歌曲数据失败。{ex.Message}", true);
        }
    }

    private void SetCover(byte[]? coverBytes)
    {
        if (ReferenceEquals(_lastPreviewCoverBytes, coverBytes)) return;
        _lastPreviewCoverBytes = coverBytes;
        if (coverBytes == null || coverBytes.Length == 0) { CoverPreview.Source = null; return; }
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(coverBytes);
            image.SetSource(stream.AsRandomAccessStream());
            CoverPreview.Source = image;
        }
        catch { CoverPreview.Source = null; }
    }

    private void SetStatus(string text, bool isError = false)
    {
        if (string.Equals(_lastStatusText, text, StringComparison.Ordinal)) return;
        _lastStatusText = text;
        StatusText.Text = text;
    }

    private void InitializeOverlayControls() => InitializeOverlayControls(_overlayWindow.CurrentSettings);

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
        finally { _isInitializingOverlayControls = false; }
    }

    private void SelectColorRadio(string color, bool isTitle)
    {
        var parent = isTitle ? TitleColor_White.Parent as StackPanel : ArtistColor_White.Parent as StackPanel;
        if (parent == null) return;
        foreach (var child in parent.Children)
        {
            if (child is RadioButton rb && string.Equals(rb.Tag?.ToString(), color, StringComparison.OrdinalIgnoreCase))
            {
                rb.IsChecked = true;
                return;
            }
        }
    }

    private void HorizontalSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (!_isInitializingOverlayControls) ApplyOverlaySettingsFromControls(); }
    private void BottomOffsetSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (!_isInitializingOverlayControls) ApplyOverlaySettingsFromControls(); }
    private void ScaleSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (!_isInitializingOverlayControls) ApplyOverlaySettingsFromControls(); }
    private void TitleOpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (!_isInitializingOverlayControls) ApplyOverlaySettingsFromControls(); }
    private void ArtistOpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (!_isInitializingOverlayControls) ApplyOverlaySettingsFromControls(); }

    private async void TrackSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
        _lastTrackKey = string.Empty;
        _lastDisplayTrackKey = string.Empty;
        _lastPreviewCoverBytes = null;
        SetCover(null);
        await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
        SetStatus("状态：数据来源已切换", false);
    }

    private void TitleColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string color)
        {
            TitleColorBox.Text = color;
            if (!_isInitializingOverlayControls) ApplyOverlaySettingsFromControls();
        }
    }

    private void ArtistColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string color)
        {
            ArtistColorBox.Text = color;
            if (!_isInitializingOverlayControls) ApplyOverlaySettingsFromControls();
        }
    }

    private void SaveOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyOverlaySettingsFromControls();
            if (!AreHotkeysValid(_activeSettings)) { SetStatus("状态：快捷键格式无效", true); return; }
            if (!RebindGlobalHotkeys()) { SetStatus("状态：应用快捷键注册失败", true); return; }
            _overlaySettingsService.Save(_activeSettings);
            SetStatus($"状态：已保存并应用。{_overlaySettingsService.SettingsFilePath}", false);
        }
        catch (Exception ex) { SetStatus($"状态：保存失败。{ex.Message}", true); }
    }

    private void ResetOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
        _activeSettings = new OverlaySettings();
        _overlayWindow.ApplySettings(_activeSettings);
        InitializeOverlayControls();
        SetStatus("状态：已重置为默认值", false);
    }

    private async void TestOverlay_Click(object sender, RoutedEventArgs e)
    {
        var previewTrack = new TrackInfo { Name = "正在播放", Artist = "网易云音乐", SourceAppId = "预览" };
        await _overlayWindow.ShowTrackAsync(previewTrack);
        SetStatus("状态：已显示悬浮窗预览", false);
    }

    private async void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/xw66/Cloud-Music-overlay-for-Forza-Horizon"));
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("状态：正在检查更新…", false);
        UpdateInfo info = await _updateService.CheckForUpdateAsync();
        if (!string.IsNullOrEmpty(info.ErrorMessage)) { SetStatus($"状态：{info.ErrorMessage}", true); return; }
        if (!info.HasUpdate) { SetStatus("状态：已是最新版本", false); return; }

        var dialog = new ContentDialog
        {
            Title = $"发现新版本 {info.LatestVersion}",
            Content = string.IsNullOrEmpty(info.ReleaseNotes) ? "是否前往 GitHub 下载？" : $"更新内容：\n{info.ReleaseNotes}\n\n是否前往 GitHub 下载？",
            PrimaryButtonText = "下载",
            SecondaryButtonText = "取消",
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(info.DownloadUrl));
        }
    }

    private async Task SilentCheckUpdateAsync()
    {
        try
        {
            UpdateInfo info = await _updateService.CheckForUpdateAsync();
            if (info.HasUpdate && string.IsNullOrEmpty(info.ErrorMessage))
            {
                SetStatus($"状态：发现新版本 {info.LatestVersion}，点击「检查更新」查看", false);
            }
        }
        catch { }
    }

    private void SelectTrackSource(string trackSource)
    {
        if (TrackSourceComboBox == null) return;
        foreach (var item in TrackSourceComboBox.Items)
        {
            if (item is ComboBoxItem combo && combo.Tag is string tag && string.Equals(tag, trackSource, StringComparison.OrdinalIgnoreCase))
            {
                TrackSourceComboBox.SelectedItem = combo;
                return;
            }
        }
        TrackSourceComboBox.SelectedIndex = 0;
    }

    private string GetSelectedTrackSource()
    {
        if (TrackSourceComboBox?.SelectedItem is ComboBoxItem combo && combo.Tag is string tag) return tag;
        return "NeteaseProcess";
    }

    private void ApplyOverlaySettingsFromControls()
    {
        if (HorizontalSlider == null || BottomOffsetSlider == null || ScaleSlider == null) return;

        var settings = new OverlaySettings
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
        if (HorizontalValueText == null || BottomOffsetValueText == null || ScaleValueText == null) return;
        HorizontalValueText.Text = $"{HorizontalSlider.Value:0}%";
        BottomOffsetValueText.Text = $"{BottomOffsetSlider.Value:0}%";
        ScaleValueText.Text = $"{ScaleSlider.Value:0}%";
    }

    private void UpdateColorLabels()
    {
        if (TitleOpacityValueText == null || ArtistOpacityValueText == null) return;
        TitleOpacityValueText.Text = $"{TitleOpacitySlider.Value:0}%";
        ArtistOpacityValueText.Text = $"{ArtistOpacitySlider.Value:0}%";
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
        if (!keyboardOk) return false;
        if (!settings.EnableGamepadHotkeys) return true;
        return GamepadHotkeyParser.TryParse(settings.GamepadPrevHotkey, out _) &&
               GamepadHotkeyParser.TryParse(settings.GamepadNextHotkey, out _) &&
               GamepadHotkeyParser.TryParse(settings.GamepadToggleHotkey, out _);
    }

    private void ApplyGamepadSettings(OverlaySettings settings)
    {
        _gamepadInputService.Enabled = settings.EnableGamepadHotkeys;
        if (GamepadHotkeyParser.TryParse(settings.GamepadPrevHotkey, out var prev)) _gamepadInputService.PrevHotkey = prev;
        if (GamepadHotkeyParser.TryParse(settings.GamepadNextHotkey, out var next)) _gamepadInputService.NextHotkey = next;
        if (GamepadHotkeyParser.TryParse(settings.GamepadToggleHotkey, out var toggle)) _gamepadInputService.ToggleHotkey = toggle;
    }

    private static void ApplyAutoStart(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath)) key.SetValue("HorizonRadioOverlay", $"\"{exePath}\" --autostart");
            }
            else { key.DeleteValue("HorizonRadioOverlay", false); }
        }
        catch { }
    }

    private static void EnsureAutoStartRegistry(OverlaySettings settings)
    {
        if (!settings.AutoStartOnBoot) return;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            string? currentValue = key?.GetValue("HorizonRadioOverlay") as string;
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) return;
            string correctValue = $"\"{exePath}\" --autostart";
            if (!string.Equals(currentValue, correctValue, StringComparison.Ordinal))
            {
                using var writeKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                writeKey?.SetValue("HorizonRadioOverlay", correctValue);
            }
        }
        catch { }
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
        textBox.KeyDown += KeyboardHotkeyInput_KeyDown;
    }

    private void KeyboardHotkeyInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        e.Handled = true;

        if (e.Key == Windows.System.VirtualKey.Back || e.Key == Windows.System.VirtualKey.Delete)
        {
            textBox.Text = "";
            return;
        }

        string? keyToken = WinUIKeyToToken(e.Key);
        if (string.IsNullOrWhiteSpace(keyToken)) return;

        bool isModifier = e.Key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Menu
            or Windows.System.VirtualKey.Shift or Windows.System.VirtualKey.LeftWindows or Windows.System.VirtualKey.RightWindows;
        if (isModifier) return;

        var modifiers = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) > 0
            ? "Ctrl+" : "";
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu) > 0) modifiers += "Alt+";
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) > 0) modifiers += "Shift+";

        string captured = modifiers + keyToken;
        if (!string.IsNullOrWhiteSpace(captured))
        {
            textBox.Text = captured;
        }
    }

    private void ConfigureGamepadHotkeyInput(TextBox textBox)
    {
        textBox.IsReadOnly = true;
    }

    private static string? WinUIKeyToToken(Windows.System.VirtualKey key)
    {
        return key switch
        {
            >= Windows.System.VirtualKey.A and <= Windows.System.VirtualKey.Z => ((char)('A' + (key - Windows.System.VirtualKey.A))).ToString(),
            >= Windows.System.VirtualKey.Number0 and <= Windows.System.VirtualKey.Number9 => ((char)('0' + (key - Windows.System.VirtualKey.Number0))).ToString(),
            >= Windows.System.VirtualKey.NumberPad0 and <= Windows.System.VirtualKey.NumberPad9 => ((char)('0' + (key - Windows.System.VirtualKey.NumberPad0))).ToString(),
            Windows.System.VirtualKey.Left => "Left",
            Windows.System.VirtualKey.Right => "Right",
            Windows.System.VirtualKey.Up => "Up",
            Windows.System.VirtualKey.Down => "Down",
            Windows.System.VirtualKey.Space => "Space",
            >= Windows.System.VirtualKey.F1 and <= Windows.System.VirtualKey.F12 => key.ToString(),
            _ => key.ToString()
        };
    }
}
