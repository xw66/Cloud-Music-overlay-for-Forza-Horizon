using HorizonRadioOverlay.ViewModels;

namespace HorizonRadioOverlay.Tests;

public sealed class MainShellViewModelTests
{
    [Fact]
    public void Constructor_SelectsNowPlayingByDefault()
    {
        var viewModel = new MainShellViewModel();

        Assert.NotNull(viewModel.SelectedItem);
        Assert.Equal("NowPlaying", viewModel.SelectedItem!.Key);
        Assert.Equal("正在播放", viewModel.CurrentPageTitle);
    }

    [Fact]
    public void SelectedItem_UpdatesCurrentPageTexts()
    {
        var viewModel = new MainShellViewModel();
        var target = viewModel.NavigationItems.Single(x => x.Key == "Logs");

        viewModel.SelectedItem = target;

        Assert.Equal("Logs", target.Key);
        Assert.Equal("日志", viewModel.CurrentPageTitle);
        Assert.Equal(target.Description, viewModel.CurrentPageDescription);
    }
}
