namespace HorizonRadioOverlay.Models;

public sealed class ThemePack
{
    public string Id { get; init; } = "theme-minimal-dark";
    public string DisplayName { get; init; } = "Minimal Dark";
    public ThemeColors Colors { get; init; } = new();
    public ThemeShape Shape { get; init; } = new();
    public ThemeEffects Effects { get; init; } = new();
    public string Layout { get; init; } = "horizon-slide-card";
}

public sealed class ThemeColors
{
    public string Accent { get; init; } = "#6BB7FF";
    public string Secondary { get; init; } = "#D8F3FF";
    public string TextPrimary { get; init; } = "#FFFFFF";
    public string TextSecondary { get; init; } = "#C0D0E0";
    public string Lyrics { get; init; } = "#A0B8D0";
    public string Surface { get; init; } = "#101820";
    public string Border { get; init; } = "#66E6EEF7";
}

public sealed class ThemeShape
{
    public double CornerRadius { get; init; } = 6;
    public double CoverRadius { get; init; } = 4;
}

public sealed class ThemeEffects
{
    public double GlassOpacity { get; init; } = 0.22;
    public bool MotionBlur { get; init; } = true;
    public bool Scanline { get; init; }
}
