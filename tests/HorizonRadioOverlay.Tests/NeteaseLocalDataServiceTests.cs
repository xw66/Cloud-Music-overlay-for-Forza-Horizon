using HorizonRadioOverlay.Services;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Tests;

public class NeteaseLocalDataServiceTests
{
    [Fact]
    public void MetadataCache_reuses_complete_metadata_for_the_same_track()
    {
        TrackInfo liveTrack = new() { Name = "Song", Artist = "Artist" };
        TrackInfo cachedTrack = new()
        {
            Name = "Song",
            Artist = "Artist",
            CoverBytes = [1, 2, 3]
        };

        Assert.True(NeteaseTrackMetadataCachePolicy.ShouldReuse(
            liveTrack,
            cachedTrack,
            elapsedMilliseconds: 60_000));
    }

    [Fact]
    public void MetadataCache_retries_incomplete_metadata_after_cooldown()
    {
        TrackInfo liveTrack = new() { Name = "Song", Artist = "Artist" };
        TrackInfo cachedTrack = new() { Name = "Song", Artist = "Artist" };

        Assert.True(NeteaseTrackMetadataCachePolicy.ShouldReuse(
            liveTrack,
            cachedTrack,
            NeteaseTrackMetadataCachePolicy.RetryIncompleteMetadataAfterMilliseconds - 1));
        Assert.False(NeteaseTrackMetadataCachePolicy.ShouldReuse(
            liveTrack,
            cachedTrack,
            NeteaseTrackMetadataCachePolicy.RetryIncompleteMetadataAfterMilliseconds));
    }

    [Fact]
    public void MetadataCache_does_not_reuse_a_different_track()
    {
        TrackInfo liveTrack = new() { Name = "Next Song", Artist = "Artist" };
        TrackInfo cachedTrack = new()
        {
            Name = "Song",
            Artist = "Artist",
            CoverBytes = [1, 2, 3]
        };

        Assert.False(NeteaseTrackMetadataCachePolicy.ShouldReuse(
            liveTrack,
            cachedTrack,
            elapsedMilliseconds: 0));
    }

    [Fact]
    public void ParseSongIdHintFromJson_PrefersFmPlayCurrentIndexBeforeScoring()
    {
        const string json = """
            {
              "currentIndex": 1,
              "queue": [
                {
                  "id": "111",
                  "name": "Wrong Song",
                  "artists": [{ "name": "Wrong Artist" }]
                },
                {
                  "id": "222",
                  "name": "Target Song",
                  "artists": [{ "name": "Target Artist" }]
                }
              ]
            }
            """;

        NeteaseLocalDataService.LocalSongIdHint? hint = NeteaseLocalDataService.ParseSongIdHintFromJson(
            json,
            "Target Song",
            "Target Artist",
            hasTrackWrapper: false,
            fileLabel: "fmPlay");

        Assert.Equal("222", hint?.SongId);
        Assert.Equal("fmPlay:current-index", hint?.Source);
    }

    [Fact]
    public void ParseSongIdHintFromJson_PrefersRootSongIdHintImmediately()
    {
        const string json = """
            {
              "songId": "333",
              "list": [
                {
                  "track": {
                    "id": "111",
                    "name": "Some Song",
                    "artists": [{ "name": "Some Artist" }]
                  }
                }
              ]
            }
            """;

        NeteaseLocalDataService.LocalSongIdHint? hint = NeteaseLocalDataService.ParseSongIdHintFromJson(
            json,
            "Other Song",
            "Other Artist",
            hasTrackWrapper: true,
            fileLabel: "playingList");

        Assert.Equal("333", hint?.SongId);
        Assert.Equal("playingList:root-id", hint?.Source);
    }

    [Fact]
    public void ParseSongIdHintFromJson_ReturnsLocalCoverAndDurationForScoredMatch()
    {
        const string json = """
            {
              "list": [
                {
                  "track": {
                    "id": "444",
                    "name": "Target Song",
                    "duration": 213500,
                    "artists": [{ "name": "Target Artist" }],
                    "album": {
                      "picUrl": "https://example.test/cover.jpg"
                    }
                  }
                }
              ]
            }
            """;

        NeteaseLocalDataService.LocalSongIdHint? hint =
            NeteaseLocalDataService.ParseSongIdHintFromJson(
                json,
                "Target Song",
                "Target Artist",
                hasTrackWrapper: true,
                fileLabel: "playingList");

        Assert.Equal("444", hint?.SongId);
        Assert.Equal("https://example.test/cover.jpg", hint?.CoverUrl);
        Assert.Equal(213.5, hint?.DurationSeconds);
    }

    [Fact]
    public void ShouldTrustPreferredSongId_ReturnsFalse_WhenResolvedSongClearlyMismatchesProcessTitle()
    {
        TrackInfo liveTrack = new()
        {
            Name = "多远都要在一起",
            Artist = "G.E.M. 邓紫棋"
        };

        ResolvedSong resolved = new()
        {
            SongId = "401722144",
            Title = "1954",
            Artist = "Michaela May",
            Confidence = 100,
            ResolveSource = "netease-id"
        };

        Assert.False(NeteaseLocalDataService.ShouldTrustPreferredSongId(liveTrack, resolved));
    }

    [Fact]
    public void ShouldTrustPreferredSongId_ReturnsTrue_WhenResolvedSongMatchesProcessTitle()
    {
        TrackInfo liveTrack = new()
        {
            Name = "多远都要在一起",
            Artist = "G.E.M. 邓紫棋"
        };

        ResolvedSong resolved = new()
        {
            SongId = "30612793",
            Title = "多远都要在一起",
            Artist = "G.E.M. 邓紫棋",
            Confidence = 100,
            ResolveSource = "netease-id"
        };

        Assert.True(NeteaseLocalDataService.ShouldTrustPreferredSongId(liveTrack, resolved));
    }
}
