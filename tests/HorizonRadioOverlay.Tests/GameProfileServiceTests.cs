using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class GameProfileServiceTests
{
    [Fact]
    public void MatchOrDefault_detects_forza_by_process_name()
    {
        GameProfileService service = new();

        var profile = service.MatchOrDefault("ForzaHorizon5.exe", null);

        Assert.Equal("forza-horizon", profile.Id);
        Assert.Equal("theme-forza-horizon-radio", profile.ThemeId);
    }

    [Fact]
    public void MatchOrDefault_detects_league_by_window_title()
    {
        GameProfileService service = new();

        var profile = service.MatchOrDefault(null, "League of Legends (TM) Client");

        Assert.Equal("league-of-legends", profile.Id);
        Assert.Equal("theme-lol-rift", profile.ThemeId);
    }

    [Fact]
    public void MatchOrDefault_falls_back_to_generic_profile()
    {
        GameProfileService service = new();

        var profile = service.MatchOrDefault("UnknownGame.exe", "Unknown Game");

        Assert.Equal("generic-game", profile.Id);
    }
}
