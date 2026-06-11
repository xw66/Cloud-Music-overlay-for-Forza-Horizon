using HorizonRadioOverlay.Services;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Tests;

public class NeteaseLocalDataServiceTests
{
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
