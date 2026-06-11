using Windows.Media.Control;
using System.Runtime.Versioning;

namespace HorizonRadioOverlay.Services;

public readonly record struct NeteasePlaybackSample(
    double PositionSeconds,
    bool IsPlaying,
    string Title,
    string Artist);

public sealed class NeteasePlaybackProbe
{
    private readonly DiagnosticService? _diagnostic;

    public NeteasePlaybackProbe(DiagnosticService? diagnostic = null)
    {
        _diagnostic = diagnostic;
    }

    public async Task<NeteasePlaybackSample?> TryReadAsync(
        string expectedTitle,
        string expectedArtist,
        double predictedPositionSeconds)
    {
        if (!RuntimeFeatureSupport.SupportsSmtc())
        {
            return null;
        }

        try
        {
            GlobalSystemMediaTransportControlsSessionManager manager =
                await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession[] sessions = GetNeteaseSessions(manager).ToArray();
            if (sessions.Length == 0)
            {
                return null;
            }

            List<NeteasePlaybackSample> matches = new();

            foreach (GlobalSystemMediaTransportControlsSession session in sessions)
            {
                GlobalSystemMediaTransportControlsSessionMediaProperties media =
                    await session.TryGetMediaPropertiesAsync();
                string title = SmtcReadPolicy.NormalizeTitle(media?.Title);
                string artist = SmtcReadPolicy.NormalizeArtist(media?.Artist, media?.AlbumTitle);
                if (!NeteasePlaybackProbePolicy.IsMatchingTrack(expectedTitle, expectedArtist, title, artist))
                {
                    continue;
                }

                GlobalSystemMediaTransportControlsSessionTimelineProperties timeline =
                    session.GetTimelineProperties();
                GlobalSystemMediaTransportControlsSessionPlaybackInfo playback =
                    session.GetPlaybackInfo();
                if (timeline == null || playback == null)
                {
                    continue;
                }

                double positionSeconds = Math.Max(0, timeline.Position.TotalSeconds);
                double endSeconds = Math.Max(0, timeline.EndTime.TotalSeconds);
                if (!NeteasePlaybackProbePolicy.HasUsableTimeline(positionSeconds, endSeconds))
                {
                    _diagnostic?.Info(
                        $"Netease lyric probe ignored unusable timeline: title={title}, artist={artist}, pos={positionSeconds:F3}s, end={endSeconds:F3}s");
                    continue;
                }

                bool isPlaying =
                    playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                matches.Add(new NeteasePlaybackSample(
                    positionSeconds,
                    isPlaying,
                    title,
                    artist));
            }

            if (matches.Count == 0)
            {
                return null;
            }

            NeteasePlaybackSample selected =
                NeteasePlaybackProbePolicy.SelectBestSample(matches, predictedPositionSeconds);
            if (matches.Count > 1)
            {
                _diagnostic?.Info(
                    $"Netease lyric probe selected best session: predicted={predictedPositionSeconds:F3}s, chosen={selected.PositionSeconds:F3}s, candidates={matches.Count}");
            }

            return selected;
        }
        catch (Exception ex)
        {
            _diagnostic?.Info($"Netease lyric probe unavailable, using wall clock: {ex.Message}");
            return null;
        }
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private static IEnumerable<GlobalSystemMediaTransportControlsSession> GetNeteaseSessions(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        foreach (GlobalSystemMediaTransportControlsSession candidate in manager.GetSessions())
        {
            if (NeteasePlaybackProbePolicy.IsNeteaseSession(candidate.SourceAppUserModelId))
            {
                yield return candidate;
            }
        }
    }
}
