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
    private void SetupHotkeyCaptureInputs()
    {
        _hotkeySettingsPageView?.SetupCaptures(ConfigureKeyboardHotkeyInput, ConfigureGamepadHotkeyInput);
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
}
