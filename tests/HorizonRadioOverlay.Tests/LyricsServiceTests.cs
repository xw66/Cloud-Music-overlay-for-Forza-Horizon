using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class LyricsServiceTests
{
    [Fact]
    public void Reset_clears_cached_state()
    {
        using LyricsService service = new(new DiagnosticService());

        service.SetPlaybackPosition(42);
        service.Reset();

        Assert.False(service.HasLyrics);
        Assert.Null(service.GetCurrentLine());
        Assert.Null(service.UpdateCurrentLine());
    }

    [Fact]
    public void Dispose_clears_cached_state()
    {
        LyricsService service = new(new DiagnosticService());

        service.SetPlaybackPosition(18);
        service.Dispose();

        Assert.False(service.HasLyrics);
        Assert.Null(service.GetCurrentLine());
        Assert.Null(service.UpdateCurrentLine());
    }
}
