using HorizonRadioOverlay.Services;

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
}
