using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class TrackIdentityTests
{
    [Fact]
    public void Smct_track_key_can_include_source_app_id()
    {
        TrackInfo track = new()
        {
            Name = "Same Song",
            Artist = "Same Artist",
            SourceAppId = "SMTC(AppA)"
        };

        string key = TrackIdentity.BuildTrackKey(track, includeSourceAppId: true);

        Assert.Equal("SMTC(AppA)|Same Song|Same Artist", key);
    }

    [Fact]
    public void Non_smtc_track_key_can_ignore_source_app_id()
    {
        TrackInfo track = new()
        {
            Name = "Same Song",
            Artist = "Same Artist",
            SourceAppId = "CloudMusic(ProcessTitle)"
        };

        string key = TrackIdentity.BuildTrackKey(track, includeSourceAppId: false);

        Assert.Equal("Same Song|Same Artist", key);
    }
}
