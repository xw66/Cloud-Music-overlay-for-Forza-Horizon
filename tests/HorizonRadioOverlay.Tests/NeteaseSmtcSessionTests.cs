using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class NeteaseSmtcSessionTests
{
    [Fact]
    public void Selects_netease_among_other_players()
    {
        string[] sessions = ["Chrome", "Spotify.exe", "CloudMusic.exe"];

        Assert.Equal("CloudMusic.exe", SmtcTrackService.FindNeteaseSession(sessions, id => id));
    }

    [Theory]
    [InlineData("Chrome", "notcloudmusic.exe")]
    [InlineData("CloudMusic.exe", "cloudmusic.exe")]
    public void Rejects_missing_or_ambiguous_netease_identity(string first, string second)
    {
        string[] sessions = [first, second];

        Assert.Null(SmtcTrackService.FindNeteaseSession(sessions, id => id));
    }
}
