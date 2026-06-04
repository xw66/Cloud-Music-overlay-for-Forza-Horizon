namespace HorizonRadioOverlay.Models;

public sealed class ResolvedSong
{
    public required string SongId { get; init; }

    public string? AlbumId { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public string? CoverUrl { get; init; }

    public double DurationSeconds { get; init; }

    public double Confidence { get; init; }

    public string ResolveSource { get; init; } = "none";
}
