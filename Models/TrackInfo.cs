namespace HorizonRadioOverlay.Models;

public sealed class TrackInfo
{
    public required string Name { get; init; }

    public required string Artist { get; init; }

    public string SourceAppId { get; init; } = string.Empty;

    public string? SongId { get; init; }

    public byte[]? CoverBytes { get; init; }

    public double DurationSeconds { get; init; }

    public string CoverSource { get; init; } = "none";
}
