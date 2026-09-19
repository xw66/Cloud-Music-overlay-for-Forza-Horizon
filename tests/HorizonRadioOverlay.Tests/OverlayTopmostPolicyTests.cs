using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class OverlayTopmostPolicyTests
{
    [Fact]
    public void ApplyExtendedStyle_preserves_existing_bits_and_adds_overlay_bits()
    {
        int existingStyle = 0x00040000;

        int result = OverlayTopmostPolicy.ApplyExtendedStyle(existingStyle);

        Assert.Equal(existingStyle | 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080, result);
    }

    [Fact]
    public void ApplyInteractiveExtendedStyle_removes_transparent_and_noactivate_bits()
    {
        int existingWithTransparent = 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080;

        int result = OverlayTopmostPolicy.ApplyInteractiveExtendedStyle(existingWithTransparent);

        // 验证 WS_EX_TRANSPARENT (0x20) 和 WS_EX_NOACTIVATE (0x08000000) 被清除
        Assert.Equal(0, result & 0x00000020);
        Assert.Equal(0, result & 0x08000000);
        // 验证 WS_EX_LAYERED (0x80000) 和 WS_EX_TOOLWINDOW (0x80) 保持存在
        Assert.NotEqual(0, result & 0x00080000);
        Assert.NotEqual(0, result & 0x00000080);
    }

    [Fact]
    public void GetTopmostFlags_uses_non_activating_size_preserving_topmost_refresh()
    {
        uint flags = OverlayTopmostPolicy.GetTopmostFlags();

        Assert.Equal(0x0001u | 0x0002u | 0x0010u | 0x0200u | 0x0400u | 0x0040u, flags);
    }

    [Fact]
    public void ReassertInterval_uses_low_frequency_refresh()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), OverlayTopmostPolicy.ReassertInterval);
    }

    [Fact]
    public void BoostedReassert_uses_short_high_frequency_recovery()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(120), OverlayTopmostPolicy.BoostedReassertInterval);
        Assert.Equal(TimeSpan.FromSeconds(12), OverlayTopmostPolicy.BoostedReassertDuration);
    }
}
