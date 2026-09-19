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

    [Fact]
    public void NeteaseHttpPolicy_AppliesAntiRiskHeaders()
    {
        using var request = NeteaseHttpPolicy.CreateRequest("https://music.163.com/api/test");

        Assert.Equal("https://music.163.com/", request.Headers.Referrer?.ToString());
        Assert.Contains("Chrome", request.Headers.UserAgent.ToString());
        Assert.True(request.Headers.TryGetValues("Cookie", out var cookies));
        string cookieHeader = string.Join(";", cookies);
        Assert.Contains("os=pc", cookieHeader);
        Assert.Contains("appver=3.0.0", cookieHeader);
        Assert.True(request.Headers.TryGetValues("X-Real-IP", out var realIps));
        Assert.Equal(NeteaseHttpPolicy.DomesticProxyIp, realIps.First());
    }

    [Fact]
    public void RankDataDirs_PrefersFoldersWithExistingFilesAndNewerTimestamps()
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "HRONeteaseTest_" + Guid.NewGuid().ToString("N"));
        string dirOld = Path.Combine(baseTemp, "OldInstall");
        string dirNew = Path.Combine(baseTemp, "NewPortable");
        string dirEmpty = Path.Combine(baseTemp, "EmptyDir");

        try
        {
            Directory.CreateDirectory(Path.Combine(dirOld, "webdata", "file"));
            Directory.CreateDirectory(Path.Combine(dirNew, "webdata", "file"));
            Directory.CreateDirectory(dirEmpty);

            string fileOld = Path.Combine(dirOld, "webdata", "file", "playingList");
            string fileNew = Path.Combine(dirNew, "webdata", "file", "playingList");

            File.WriteAllText(fileOld, "{}");
            File.SetLastWriteTimeUtc(fileOld, DateTime.UtcNow.AddHours(-2));

            File.WriteAllText(fileNew, "{}");
            File.SetLastWriteTimeUtc(fileNew, DateTime.UtcNow);

            var ranked = NeteaseLocalDataService.RankDataDirs([dirEmpty, dirOld, dirNew]);

            Assert.Equal(3, ranked.Count);
            Assert.Equal(Path.GetFullPath(dirNew), ranked[0]);
            Assert.Equal(Path.GetFullPath(dirOld), ranked[1]);
            Assert.Equal(Path.GetFullPath(dirEmpty), ranked[2]);
        }
        finally
        {
            if (Directory.Exists(baseTemp))
            {
                Directory.Delete(baseTemp, true);
            }
        }
    }

    [Fact]
    public void FindAllNeteaseDataDirs_WithCustomRoots_DiscoversCandidateSubfolders()
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "HRONeteaseCustom_" + Guid.NewGuid().ToString("N"));
        string portableData = Path.Combine(baseTemp, "UserData", "webdata", "file");

        try
        {
            Directory.CreateDirectory(portableData);
            string file = Path.Combine(portableData, "playingList");
            File.WriteAllText(file, "{}");

            var dirs = NeteaseLocalDataService.FindAllNeteaseDataDirs([baseTemp]);

            Assert.Contains(dirs, d => d.EndsWith("UserData", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(baseTemp))
            {
                Directory.Delete(baseTemp, true);
            }
        }
    }
}
