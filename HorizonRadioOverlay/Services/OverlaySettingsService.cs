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

    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;

    public string SettingsFilePath => _settingsFilePath;

    public OverlaySettingsService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsDirectory = Path.Combine(appData, "HorizonRadioOverlay");
        _settingsFilePath = Path.Combine(_settingsDirectory, "overlay-settings.json");
    }

    public OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new OverlaySettings();
            }

            string json = File.ReadAllText(_settingsFilePath);
            OverlaySettings? loaded = JsonSerializer.Deserialize<OverlaySettings>(json);
            if (loaded == null)
            {
                return new OverlaySettings();
            }

            loaded = Migrate(loaded);
            return Normalize(loaded);
        }
        catch
        {
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        OverlaySettings normalized = Normalize(settings);
        normalized.SchemaVersion = OverlaySettings.CurrentVersion;
        Directory.CreateDirectory(_settingsDirectory);
        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
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
            // v2 migration placeholder: add new fields with defaults here
            // Example: settings.NewField = "default";
        }

        settings.SchemaVersion = version;
        return settings;
    }

    private static OverlaySettings Normalize(OverlaySettings settings)
    {
        if (!string.Equals(settings.TrackSource, "NeteaseProcess", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.TrackSource, "SMTC", StringComparison.OrdinalIgnoreCase))
        {
            settings.TrackSource = "NeteaseProcess";
        }

        settings.LeftPercent = Clamp(settings.LeftPercent, 0.0, 1.0);
        settings.TopPercent = Clamp(settings.TopPercent, 0.0, 1.0);
        settings.Scale = Clamp(settings.Scale, 0.8, 1.8);
        return settings;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
