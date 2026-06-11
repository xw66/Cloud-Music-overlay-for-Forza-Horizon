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
            AlbumTitle = "Album A",
            DurationSeconds = 240,
            SourceAppId = "SMTC(AppA)"
        };

        string key = TrackIdentity.BuildTrackKey(track, includeSourceAppId: true);

        Assert.Equal("SMTC(AppA)|Same Song|Same Artist|Album A|240", key);
    }

    [Fact]
    public void Non_smtc_track_key_can_ignore_source_app_id()
    {
        TrackInfo track = new()
        {
            Name = "Same Song",
            Artist = "Same Artist",
            AlbumTitle = "Album A",
            DurationSeconds = 240,
            SourceAppId = "CloudMusic(ProcessTitle)"
        };

        string key = TrackIdentity.BuildTrackKey(track, includeSourceAppId: false);

        Assert.Equal("Same Song|Same Artist|Album A|240", key);
    }

    [Fact]
    public void Track_key_distinguishes_album_and_duration_variants()
    {
        TrackInfo trackA = new()
        {
            Name = "Same Song",
            Artist = "Same Artist",
            AlbumTitle = "Album A",
            DurationSeconds = 240,
            SourceAppId = "SMTC(AppA)"
        };

        TrackInfo trackB = new()
        {
            Name = "Same Song",
            Artist = "Same Artist",
            AlbumTitle = "Album B",
            DurationSeconds = 260,
            SourceAppId = "SMTC(AppA)"
        };

        string keyA = TrackIdentity.BuildTrackKey(trackA, includeSourceAppId: true);
        string keyB = TrackIdentity.BuildTrackKey(trackB, includeSourceAppId: true);

        Assert.NotEqual(keyA, keyB);
    }
}
