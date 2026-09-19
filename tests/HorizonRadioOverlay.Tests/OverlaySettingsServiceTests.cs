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
    public void Normalize_clamps_font_size_range()
    {
        OverlaySettings settings = new()
        {
            TitleFontSize = 100.0,
            ArtistFontSize = 5.0,
            LyricsFontSize = 50.0
        };

        OverlaySettings normalized = OverlaySettingsService.NormalizeForTests(settings);

        Assert.Equal(32.0, normalized.TitleFontSize);
        Assert.Equal(10.0, normalized.ArtistFontSize);
        Assert.Equal(20.0, normalized.LyricsFontSize);

        OverlaySettings settingsLow = new()
        {
            TitleFontSize = 2.0,
            ArtistFontSize = 30.0,
            LyricsFontSize = 3.0
        };

        OverlaySettings normalizedLow = OverlaySettingsService.NormalizeForTests(settingsLow);

        Assert.Equal(12.0, normalizedLow.TitleFontSize);
        Assert.Equal(24.0, normalizedLow.ArtistFontSize);
        Assert.Equal(9.0, normalizedLow.LyricsFontSize);
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

    [Fact]
    public void Save_and_Load_roundtrip_with_custom_path()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HRO_Test_{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDir, "custom-settings.json");

        try
        {
            var service = new OverlaySettingsService(settingsPath);
            var initial = service.Load();
            Assert.Equal(OverlaySettings.CurrentVersion, initial.SchemaVersion);

            var settingsToSave = new OverlaySettings
            {
                AppPrevHotkey = "Ctrl+Alt+P",
                TitleColor = "#123456",
                Scale = 1.5,
                RemoteControlPort = 18888
            };

            service.Save(settingsToSave);

            Assert.True(File.Exists(settingsPath));

            var loaded = service.Load();
            Assert.Equal("Ctrl+Alt+P", loaded.AppPrevHotkey);
            Assert.Equal("#123456", loaded.TitleColor);
            Assert.Equal(1.5, loaded.Scale);
            Assert.Equal(18888, loaded.RemoteControlPort);

            // 第二次保存，验证 .bak 生成
            settingsToSave.TitleColor = "#654321";
            service.Save(settingsToSave);

            Assert.True(File.Exists(service.BackupFilePath));
            string backupContent = File.ReadAllText(service.BackupFilePath);
            Assert.Contains("#123456", backupContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_recovers_from_backup_when_main_file_is_corrupted()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HRO_Test_{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDir, "custom-settings.json");

        try
        {
            var service = new OverlaySettingsService(settingsPath);
            var settings = new OverlaySettings
            {
                AppPrevHotkey = "Ctrl+Shift+Z",
                TitleColor = "#AABBCC"
            };

            // 第一次保存，生成主文件
            service.Save(settings);
            // 第二次保存，生成备份文件（备份内容为 #AABBCC）
            service.Save(settings);

            // 人为破坏主文件（写坏 JSON 模拟异常断电 0 字节或垃圾数据）
            File.WriteAllText(settingsPath, "{ corrupted invalid json ... 00000");

            var recovered = service.Load();

            // 验证从备份文件中完好恢复
            Assert.Equal("Ctrl+Shift+Z", recovered.AppPrevHotkey);
            Assert.Equal("#AABBCC", recovered.TitleColor);

            // 验证自动修复了主文件
            string repairedJson = File.ReadAllText(settingsPath);
            Assert.Contains("#AABBCC", repairedJson);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_returns_defaults_when_both_files_corrupted()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HRO_Test_{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDir, "custom-settings.json");

        try
        {
            Directory.CreateDirectory(tempDir);
            var service = new OverlaySettingsService(settingsPath);

            File.WriteAllText(settingsPath, "INVALID_MAIN");
            File.WriteAllText(service.BackupFilePath, "INVALID_BACKUP");

            var result = service.Load();

            Assert.NotNull(result);
            Assert.Equal(OverlaySettings.CurrentVersion, result.SchemaVersion);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
