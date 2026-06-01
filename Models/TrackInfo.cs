namespace HorizonRadioOverlay.Models;

public sealed class TrackInfo
{
    public required string Name { get; init; }

    public required string Artist { get; init; }

    public string SourceAppId { get; init; } = string.Empty;

    public byte[]? CoverBytes { get; init; }
}
