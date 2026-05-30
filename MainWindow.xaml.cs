using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay;

public partial class MainWindow : Window
{
    private readonly NeteaseLocalDataService _neteaseLocalDataService;
    private readonly OverlaySettingsService _overlaySettingsService;
    private readonly OverlayWindow _overlayWindow;
    private readonly NeteaseShortcutSender _neteaseShortcutSender;
    private readonly GamepadInputService _gamepadInputService;
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

    public MainWindow()
    {
        _isInitializingOverlayControls = true;

        _neteaseLocalDataService = new NeteaseLocalDataService();
        _overlaySettingsService = new OverlaySettingsService();
        _overlayWindow = new OverlayWindow();
        _neteaseShortcutSender = new NeteaseShortcutSender();
        _gamepadInputService = new GamepadInputService();
        OverlaySettings loadedSettings = _overlaySettingsService.Load();
        _activeSettings = loadedSettings;
        _overlayWindow.ApplySettings(loadedSettings);
        ApplyGamepadSettings(loadedSettings);

        InitializeComponent();

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

        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        bool ok = RebindGlobalHotkeys();
        if (ok)
        {
            SetStatus("状态：快捷键已就绪，按应用快捷键会转发网易云快捷键。", false);
            _ = RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
            _pollTimer.Start();
            return;
        }

        SetStatus("状态：应用快捷键注册失败（被占用或格式无效）。", false);
        _ = RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
        _pollTimer.Start();
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
        _overlayWindow.Close();
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
        bool ok = _neteaseShortcutSender.Send(_activeSettings.NeteasePrevHotkey);
        if (!ok)
        {
            SetStatus("状态：发送“上一首”快捷键失败，请检查快捷键格式。", false);
            return;
        }

        await RefreshAfterControlAsync();
    }

    private async Task NextAsync()
    {
        bool ok = _neteaseShortcutSender.Send(_activeSettings.NeteaseNextHotkey);
        if (!ok)
        {
            SetStatus("状态：发送“下一首”快捷键失败，请检查快捷键格式。", false);
            return;
        }

        await RefreshAfterControlAsync();
    }

    private async void TogglePlayPause()
    {
        bool ok = _neteaseShortcutSender.Send(_activeSettings.NeteaseToggleHotkey);
        if (!ok)
        {
            SetStatus("状态：发送“播放/暂停”快捷键失败，请检查快捷键格式。", false);
            return;
        }

        SetStatus("状态：已发送播放/暂停快捷键。", false);
        await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
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
            TrackInfo? track = await _neteaseLocalDataService.GetCurrentTrackAsync();
            if (track == null)
            {
                if (!string.IsNullOrEmpty(_lastDisplayTrackKey))
                {
                    CurrentTitle.Text = "未检测到网易云歌曲";
                    CurrentArtist.Text = "请打开网易云音乐并播放歌曲";
                    CurrentMeta.Text = "来源：CloudMusic(ProcessTitle)";
                    SetCover(null);
                }

                SetStatus("状态：未读取到网易云窗口标题。", false);
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

            SetStatus("状态：已从网易云窗口标题同步。", false);
        }
        catch (Exception ex)
        {
            SetStatus($"状态：读取网易云数据失败。{ex.Message}", true);
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
            UpdateOverlayControlLabels();
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

    private void ApplyOverlaySettingsFromControls()
    {
        if (HorizontalSlider == null || BottomOffsetSlider == null || ScaleSlider == null)
        {
            return;
        }

        OverlaySettings settings = new()
        {
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
            GamepadToggleHotkey = GamepadToggleHotkeyBox.Text.Trim()
        };

        _activeSettings = settings;
        _overlayWindow.ApplySettings(settings);
        ApplyGamepadSettings(settings);
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

}
