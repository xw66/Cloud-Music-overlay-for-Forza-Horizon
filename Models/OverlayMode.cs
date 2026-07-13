namespace HorizonRadioOverlay.Models;

public static class OverlayMode
{
    public const string SlideRadio = "slide-radio";
    public const string CompactPill = "compact-pill";
    public const string CoverFlow = "cover-flow";

    public static bool IsKnown(string? value)
    {
        return string.Equals(value, SlideRadio, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, CompactPill, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, CoverFlow, StringComparison.OrdinalIgnoreCase);
    }
}
