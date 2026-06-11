using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class OverlaySettingsServiceTests
{
    [Fact]
    public void Normalize_clamps_hidden_smtc_delay_override_range()
    {
        OverlaySettings settings = new()
        {
            SmtcLyricDelayOverrideMs = 99999
        };

        OverlaySettings normalized = OverlaySettingsService.NormalizeForTests(settings);

        Assert.Equal(3000, normalized.SmtcLyricDelayOverrideMs);
    }

    [Fact]
    public void OverlaySettings_defaults_include_overlay_visibility_controls()
    {
        OverlaySettings settings = new();

        Assert.Equal("Ctrl+Shift+H", settings.AppToggleOverlayHotkey);
        Assert.Equal("Back+Start", settings.GamepadToggleOverlayHotkey);
        Assert.False(settings.HideOverlayWhenPaused);
    }
}
