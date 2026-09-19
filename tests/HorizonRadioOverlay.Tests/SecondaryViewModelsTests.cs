using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;
using HorizonRadioOverlay.ViewModels;

namespace HorizonRadioOverlay.Tests;

public sealed class SecondaryViewModelsTests
{
    [Fact]
    public void HotkeySettingsViewModel_LoadAndApply_RoundTripsCorrectly()
    {
        var vm = new HotkeySettingsViewModel();
        var settings = new OverlaySettings
        {
            AppPrevHotkey = "Alt+Left",
            AppNextHotkey = "Alt+Right",
            AppToggleHotkey = "Alt+Down",
            AppToggleOverlayHotkey = "Alt+H",
            NeteasePrevHotkey = "Ctrl+Shift+P",
            NeteaseNextHotkey = "Ctrl+Shift+N",
            NeteaseToggleHotkey = "Ctrl+Shift+Space",
            EnableGamepadHotkeys = true,
            GamepadPrevHotkey = "A+B",
            GamepadNextHotkey = "X+Y",
            GamepadToggleHotkey = "LB+RB",
            GamepadToggleOverlayHotkey = "LT+RT"
        };

        vm.LoadFromSettings(settings);

        Assert.Equal("Alt+Left", vm.AppPrevHotkey);
        Assert.Equal("Alt+Right", vm.AppNextHotkey);
        Assert.Equal("Alt+Down", vm.AppToggleHotkey);
        Assert.Equal("Alt+H", vm.AppToggleOverlayHotkey);
        Assert.Equal("Ctrl+Shift+P", vm.NeteasePrevHotkey);
        Assert.True(vm.EnableGamepadHotkeys);
        Assert.Equal("A+B", vm.GamepadPrevHotkey);

        // 修改后再应用
        vm.AppPrevHotkey = "Ctrl+F1";
        vm.EnableGamepadHotkeys = false;

        var targetSettings = new OverlaySettings();
        vm.ApplyToSettings(targetSettings);

        Assert.Equal("Ctrl+F1", targetSettings.AppPrevHotkey);
        Assert.False(targetSettings.EnableGamepadHotkeys);
    }

    [Fact]
    public void HotkeySettingsViewModel_PropertyChange_TriggersSettingsChanged()
    {
        var vm = new HotkeySettingsViewModel();
        bool changed = false;
        vm.SettingsChanged += () => changed = true;

        vm.AppPrevHotkey = "Ctrl+Alt+1";

        Assert.True(changed);
    }

    [Fact]
    public void RemoteControlViewModel_LoadAndApply_ValidatesPort()
    {
        var vm = new RemoteControlViewModel();
        var settings = new OverlaySettings
        {
            EnableRemoteControl = true,
            RemoteControlPort = 19090,
            RemoteControlAllowLan = false
        };

        vm.LoadFromSettings(settings);
        Assert.True(vm.EnableRemoteControl);
        Assert.Equal("19090", vm.PortText);
        Assert.False(vm.AllowLan);

        // 测试合法端口写入
        vm.PortText = "20000";
        vm.AllowLan = true;
        vm.ApplyToSettings(settings);
        Assert.Equal(20000, settings.RemoteControlPort);
        Assert.True(settings.RemoteControlAllowLan);

        // 测试非法端口不会覆盖原有有效端口
        vm.PortText = "invalid-port";
        vm.ApplyToSettings(settings);
        Assert.Equal(20000, settings.RemoteControlPort);
    }

    [Fact]
    public void RemoteControlViewModel_TogglingProperties_TriggersSettingsChanged()
    {
        var vm = new RemoteControlViewModel();
        int changedCount = 0;
        vm.SettingsChanged += () => changedCount++;

        vm.EnableRemoteControl = true;
        Assert.Equal(1, changedCount);

        vm.AllowLan = false;
        Assert.Equal(2, changedCount);

        vm.PortText = "37888";
        Assert.Equal(3, changedCount);
    }

    [Fact]
    public void RemoteControlViewModel_UpdateRuntimeStatus_WhenServiceStopped_HidesAddress()
    {
        var vm = new RemoteControlViewModel();
        using var service = new RemoteControlService();
        var settings = new OverlaySettings
        {
            EnableRemoteControl = true,
            RemoteControlPort = 39992,
            RemoteControlAllowLan = true
        };

        // service 未启动 (IsRunning == false)
        vm.UpdateRuntimeStatus(service, settings);

        // 此时 Address 和二维码应保持空，防止展示无效地址
        Assert.Empty(vm.Address);
        Assert.Null(vm.QrCodeImage);
        Assert.Equal("未启用", vm.StatusText);
    }

    [Fact]
    public void LogsViewModel_Refresh_InvokesScrollCallback()
    {
        var vm = new LogsViewModel();
        bool scrollRequested = false;
        vm.RequestScrollToEnd += () => scrollRequested = true;

        // 无诊断服务时刷新安全退出
        vm.Refresh();
        Assert.False(scrollRequested);
    }

    [Fact]
    public void AboutViewModel_ContainsDefaultInfo()
    {
        var vm = new AboutViewModel();

        Assert.False(string.IsNullOrWhiteSpace(vm.AppTitle));
        Assert.False(string.IsNullOrWhiteSpace(vm.VersionText));
        Assert.False(string.IsNullOrWhiteSpace(vm.Description));
    }

    [Fact]
    public void ThemeSettingsViewModel_FontSize_LoadAndApply_And_Formatting()
    {
        var vm = new ThemeSettingsViewModel();
        var settings = new OverlaySettings
        {
            TitleFontSize = 24.0,
            ArtistFontSize = 18.0,
            LyricsFontSize = 13.0
        };

        vm.LoadFrom(settings);

        Assert.Equal(24.0, vm.TitleFontSize);
        Assert.Equal(18.0, vm.ArtistFontSize);
        Assert.Equal(13.0, vm.LyricsFontSize);
        Assert.Equal("24 pt", vm.TitleFontSizeText);
        Assert.Equal("18 pt", vm.ArtistFontSizeText);
        Assert.Equal("13 pt", vm.LyricsFontSizeText);

        bool eventFired = false;
        vm.SettingsChanged += () => eventFired = true;

        vm.TitleFontSize = 28.0;
        Assert.True(eventFired);
        Assert.Equal("28 pt", vm.TitleFontSizeText);

        var targetSettings = new OverlaySettings();
        vm.ApplyTo(targetSettings);
        Assert.Equal(28.0, targetSettings.TitleFontSize);
        Assert.Equal(18.0, targetSettings.ArtistFontSize);
        Assert.Equal(13.0, targetSettings.LyricsFontSize);
    }
}
