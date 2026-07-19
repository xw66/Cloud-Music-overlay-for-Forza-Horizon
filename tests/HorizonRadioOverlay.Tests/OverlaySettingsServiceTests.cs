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
        Assert.True(settings.EnableNeteaseMemoryTimeline);
        Assert.Equal(PlaybackSourceIds.Netease, settings.TrackSource);
        Assert.Equal(OverlaySettings.CurrentVersion, settings.SchemaVersion);
    }

    [Fact]
    public void OverlaySettings_defaults_include_disabled_remote_control()
    {
        OverlaySettings settings = new();

        Assert.False(settings.EnableRemoteControl);
        Assert.Equal(RemoteControlPolicy.DefaultPort, settings.RemoteControlPort);
        Assert.True(settings.RemoteControlAllowLan);
        Assert.False(string.IsNullOrWhiteSpace(settings.RemoteControlToken));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(70000)]
    public void Normalize_resets_invalid_remote_control_port(int port)
    {
        OverlaySettings settings = new()
        {
            RemoteControlPort = port
        };

        OverlaySettings normalized = OverlaySettingsService.NormalizeForTests(settings);

        Assert.Equal(RemoteControlPolicy.DefaultPort, normalized.RemoteControlPort);
    }

    [Fact]
    public void Normalize_generates_missing_remote_control_token()
    {
        OverlaySettings settings = new()
        {
            RemoteControlToken = ""
        };

        OverlaySettings normalized = OverlaySettingsService.NormalizeForTests(settings);

        Assert.False(string.IsNullOrWhiteSpace(normalized.RemoteControlToken));
    }

    [Fact]
    public void Migrate_enables_read_only_memory_timeline_for_existing_settings()
    {
        OverlaySettings settings = new()
        {
            SchemaVersion = 1,
            EnableNeteaseMemoryTimeline = false
        };

        OverlaySettings migrated = OverlaySettingsService.MigrateForTests(settings);

        Assert.Equal(2, migrated.SchemaVersion);
        Assert.True(migrated.EnableNeteaseMemoryTimeline);
    }
}
