using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class AppLaunchPolicyTests
{
    [Fact]
    public void BuildAutoStartCommand_appends_autostart_argument()
    {
        string command = AppLaunchPolicy.BuildAutoStartCommand(@"E:\Apps\HorizonRadioOverlay.exe");

        Assert.Equal("\"E:\\Apps\\HorizonRadioOverlay.exe\" --autostart", command);
    }

    [Fact]
    public void ShouldStartHiddenToTray_returns_true_for_autostart_argument()
    {
        bool result = AppLaunchPolicy.ShouldStartHiddenToTray(["--autostart"]);

        Assert.True(result);
    }

    [Fact]
    public void ShouldStartHiddenToTray_returns_false_for_normal_launch()
    {
        bool result = AppLaunchPolicy.ShouldStartHiddenToTray([]);

        Assert.False(result);
    }
}
