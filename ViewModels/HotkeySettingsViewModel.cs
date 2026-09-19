using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.ViewModels;

[SupportedOSPlatform("windows")]
public sealed partial class HotkeySettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _appPrevHotkey = "Ctrl+Shift+Left";

    [ObservableProperty]
    private string _appNextHotkey = "Ctrl+Shift+Right";

    [ObservableProperty]
    private string _appToggleHotkey = "Ctrl+Shift+Down";

    [ObservableProperty]
    private string _appToggleOverlayHotkey = "Ctrl+Shift+H";

    [ObservableProperty]
    private string _neteasePrevHotkey = "Ctrl+Alt+Left";

    [ObservableProperty]
    private string _neteaseNextHotkey = "Ctrl+Alt+Right";

    [ObservableProperty]
    private string _neteaseToggleHotkey = "Ctrl+Alt+P";

    [ObservableProperty]
    private bool _enableGamepadHotkeys;

    [ObservableProperty]
    private string _gamepadPrevHotkey = "LB+Left";

    [ObservableProperty]
    private string _gamepadNextHotkey = "RB+Right";

    [ObservableProperty]
    private string _gamepadToggleHotkey = "LT+RT+Y";

    [ObservableProperty]
    private string _gamepadToggleOverlayHotkey = "Back+Start";

    public event Action? SettingsChanged;

    partial void OnAppPrevHotkeyChanged(string value) => SettingsChanged?.Invoke();
    partial void OnAppNextHotkeyChanged(string value) => SettingsChanged?.Invoke();
    partial void OnAppToggleHotkeyChanged(string value) => SettingsChanged?.Invoke();
    partial void OnAppToggleOverlayHotkeyChanged(string value) => SettingsChanged?.Invoke();
    partial void OnNeteasePrevHotkeyChanged(string value) => SettingsChanged?.Invoke();
    partial void OnNeteaseNextHotkeyChanged(string value) => SettingsChanged?.Invoke();
    partial void OnNeteaseToggleHotkeyChanged(string value) => SettingsChanged?.Invoke();
    partial void OnEnableGamepadHotkeysChanged(bool value) => SettingsChanged?.Invoke();
    partial void OnGamepadPrevHotkeyChanged(string value) => SettingsChanged?.Invoke();
    partial void OnGamepadNextHotkeyChanged(string value) => SettingsChanged?.Invoke();
    partial void OnGamepadToggleHotkeyChanged(string value) => SettingsChanged?.Invoke();
    partial void OnGamepadToggleOverlayHotkeyChanged(string value) => SettingsChanged?.Invoke();

    public void LoadFromSettings(OverlaySettings settings)
    {
        AppPrevHotkey = settings.AppPrevHotkey;
        AppNextHotkey = settings.AppNextHotkey;
        AppToggleHotkey = settings.AppToggleHotkey;
        AppToggleOverlayHotkey = settings.AppToggleOverlayHotkey;

        NeteasePrevHotkey = settings.NeteasePrevHotkey;
        NeteaseNextHotkey = settings.NeteaseNextHotkey;
        NeteaseToggleHotkey = settings.NeteaseToggleHotkey;

        EnableGamepadHotkeys = settings.EnableGamepadHotkeys;
        GamepadPrevHotkey = settings.GamepadPrevHotkey;
        GamepadNextHotkey = settings.GamepadNextHotkey;
        GamepadToggleHotkey = settings.GamepadToggleHotkey;
        GamepadToggleOverlayHotkey = settings.GamepadToggleOverlayHotkey;
    }

    public void ApplyToSettings(OverlaySettings settings)
    {
        settings.AppPrevHotkey = AppPrevHotkey.Trim();
        settings.AppNextHotkey = AppNextHotkey.Trim();
        settings.AppToggleHotkey = AppToggleHotkey.Trim();
        settings.AppToggleOverlayHotkey = AppToggleOverlayHotkey.Trim();

        settings.NeteasePrevHotkey = NeteasePrevHotkey.Trim();
        settings.NeteaseNextHotkey = NeteaseNextHotkey.Trim();
        settings.NeteaseToggleHotkey = NeteaseToggleHotkey.Trim();

        settings.EnableGamepadHotkeys = EnableGamepadHotkeys;
        settings.GamepadPrevHotkey = GamepadPrevHotkey.Trim();
        settings.GamepadNextHotkey = GamepadNextHotkey.Trim();
        settings.GamepadToggleHotkey = GamepadToggleHotkey.Trim();
        settings.GamepadToggleOverlayHotkey = GamepadToggleOverlayHotkey.Trim();
    }
}
