using System.IO;
using System.Text.Json;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class OverlaySettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public const string BackupFileExtension = ".bak";

    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;
    private readonly string _backupFilePath;

    public string SettingsFilePath => _settingsFilePath;
    public string BackupFilePath => _backupFilePath;

    public OverlaySettingsService(string? customSettingsFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customSettingsFilePath))
        {
            _settingsFilePath = Path.GetFullPath(customSettingsFilePath);
            _settingsDirectory = Path.GetDirectoryName(_settingsFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _settingsDirectory = Path.Combine(appData, "HorizonRadioOverlay");
            _settingsFilePath = Path.Combine(_settingsDirectory, "overlay-settings.json");
        }

        _backupFilePath = _settingsFilePath + BackupFileExtension;
    }

    public OverlaySettings Load()
    {
        // 1. 尝试从主配置文件加载
        if (File.Exists(_settingsFilePath))
        {
            try
            {
                string json = File.ReadAllText(_settingsFilePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    OverlaySettings? loaded = JsonSerializer.Deserialize<OverlaySettings>(json);
                    if (loaded != null)
                    {
                        return Normalize(Migrate(loaded));
                    }
                }
            }
            catch
            {
                // 主配置文件损坏或格式异常，尝试从备份恢复
            }
        }

        // 2. 尝试从备份文件 (.bak) 容灾恢复
        if (File.Exists(_backupFilePath))
        {
            try
            {
                string backupJson = File.ReadAllText(_backupFilePath);
                if (!string.IsNullOrWhiteSpace(backupJson))
                {
                    OverlaySettings? backupLoaded = JsonSerializer.Deserialize<OverlaySettings>(backupJson);
                    if (backupLoaded != null)
                    {
                        OverlaySettings recovered = Normalize(Migrate(backupLoaded));
                        // 自动修复被损坏的主文件
                        try
                        {
                            Save(recovered);
                        }
                        catch
                        {
                        }
                        return recovered;
                    }
                }
            }
            catch
            {
                // 备份文件亦不可用，降级返回默认配置
            }
        }

        return new OverlaySettings();
    }

    public void Save(OverlaySettings settings)
    {
        OverlaySettings normalized = Normalize(settings);
        normalized.SchemaVersion = OverlaySettings.CurrentVersion;
        Directory.CreateDirectory(_settingsDirectory);

        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(json);
        string tempFilePath = $"{_settingsFilePath}.tmp.{Guid.NewGuid():N}";

        try
        {
            // 1. 写入独立临时文件并强刷物理磁盘（避免截断写入造成 0 字节损坏）
            using (var stream = new FileStream(
                tempFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(utf8Bytes, 0, utf8Bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            // 2. 若已有主文件，先快照备份至 .bak
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    File.Copy(_settingsFilePath, _backupFilePath, overwrite: true);
                }
                catch
                {
                }
            }

            // 3. 原子重命名覆盖（Windows NTFS/ReFS 原子替换）
            File.Move(tempFilePath, _settingsFilePath, overwrite: true);
        }
        finally
        {
            // 4. 清理残留临时文件
            if (File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch
                {
                }
            }
        }
    }

    private static OverlaySettings Migrate(OverlaySettings settings)
    {
        int version = settings.SchemaVersion;

        if (version < 1)
        {
            version = 1;
        }

        if (version < 2)
        {
            settings.EnableNeteaseMemoryTimeline = true;
            version = 2;
        }

        settings.SchemaVersion = version;
        return settings;
    }

    private static OverlaySettings Normalize(OverlaySettings settings)
    {
        if (!string.Equals(settings.TrackSource, PlaybackSourceIds.Netease, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.TrackSource, PlaybackSourceIds.Smtc, StringComparison.OrdinalIgnoreCase))
        {
            settings.TrackSource = PlaybackSourceIds.Netease;
        }

        settings.LeftPercent = Clamp(settings.LeftPercent, 0.0, 1.0);
        settings.TopPercent = Clamp(settings.TopPercent, 0.0, 1.0);
        settings.Scale = Clamp(settings.Scale, 0.8, 1.8);
        settings.TitleFontSize = settings.TitleFontSize <= 0 ? 19.0 : Clamp(settings.TitleFontSize, 12.0, 32.0);
        settings.ArtistFontSize = settings.ArtistFontSize <= 0 ? 14.0 : Clamp(settings.ArtistFontSize, 10.0, 24.0);
        settings.LyricsFontSize = settings.LyricsFontSize <= 0 ? 11.0 : Clamp(settings.LyricsFontSize, 9.0, 20.0);
        settings.RemoteControlPort = RemoteControlPolicy.NormalizePort(settings.RemoteControlPort);
        if (string.IsNullOrWhiteSpace(settings.RemoteControlToken))
        {
            settings.RemoteControlToken = RemoteControlService.GenerateToken();
        }
        if (settings.SmtcLyricDelayOverrideMs.HasValue)
        {
            settings.SmtcLyricDelayOverrideMs = Clamp(settings.SmtcLyricDelayOverrideMs.Value, -3000, 3000);
        }
        return settings;
    }

    internal static OverlaySettings NormalizeForTests(OverlaySettings settings)
    {
        return Normalize(settings);
    }

    internal static OverlaySettings MigrateForTests(OverlaySettings settings)
    {
        return Migrate(settings);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
