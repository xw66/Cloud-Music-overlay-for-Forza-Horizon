namespace HorizonRadioOverlay.Models;

public sealed class GameProfile
{
    public string Id { get; init; } = "generic-game";
    public string DisplayName { get; init; } = "通用游戏";
    public string[] ProcessNames { get; init; } = [];
    public string[] WindowTitlePatterns { get; init; } = [];
    public string ThemeId { get; init; } = "theme-minimal-dark";
    public OverlayPlacement DefaultPlacement { get; init; } = new();
    public OverlaySafeArea[] SafeAreas { get; init; } = [];
    public string OverlayMode { get; init; } = HorizonRadioOverlay.Models.OverlayMode.SlideRadio;
    public string InputPresetId { get; init; } = "default-media";
}

public sealed class OverlayPlacement
{
    public string Anchor { get; init; } = "top-right";
    public double XPercent { get; init; } = 82;
    public double YPercent { get; init; } = 14;
    public double Scale { get; init; } = 1.0;
}

public sealed class OverlaySafeArea
{
    public string Name { get; init; } = string.Empty;
    public string Anchor { get; init; } = string.Empty;
    public double WidthPercent { get; init; }
    public double HeightPercent { get; init; }
}
