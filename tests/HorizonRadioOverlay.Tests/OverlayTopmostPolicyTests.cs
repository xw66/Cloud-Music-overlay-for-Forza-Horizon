using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class OverlayTopmostPolicyTests
{
    [Fact]
    public void ApplyExtendedStyle_preserves_existing_bits_and_adds_overlay_bits()
    {
        int existingStyle = 0x00040000;

        int result = OverlayTopmostPolicy.ApplyExtendedStyle(existingStyle);

        Assert.Equal(existingStyle | 0x00080000 | 0x00000020 | 0x00000080, result);
    }

    [Fact]
    public void GetTopmostFlags_uses_non_activating_size_preserving_topmost_refresh()
    {
        uint flags = OverlayTopmostPolicy.GetTopmostFlags();

        Assert.Equal(0x0001u | 0x0002u | 0x0010u | 0x0200u | 0x0040u, flags);
    }

    [Fact]
    public void ReassertInterval_uses_low_frequency_refresh()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), OverlayTopmostPolicy.ReassertInterval);
    }
}
