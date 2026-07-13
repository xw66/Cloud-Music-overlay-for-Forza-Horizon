using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class ThemeServiceTests
{
    [Fact]
    public void GetByIdOrDefault_returns_requested_theme()
    {
        ThemeService service = new();

        var theme = service.GetByIdOrDefault("theme-lol-rift");

        Assert.Equal("Summoner Rift", theme.DisplayName);
        Assert.Equal("#C89B3C", theme.Colors.Accent);
    }

    [Fact]
    public void GetByIdOrDefault_falls_back_to_minimal_theme()
    {
        ThemeService service = new();

        var theme = service.GetByIdOrDefault("missing-theme");

        Assert.Equal("theme-minimal-dark", theme.Id);
    }
}
