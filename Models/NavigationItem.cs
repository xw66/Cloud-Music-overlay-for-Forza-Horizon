namespace HorizonRadioOverlay.Models;

public sealed class NavigationItem
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public required string IconGlyph { get; init; }

    public string? Description { get; init; }
}
