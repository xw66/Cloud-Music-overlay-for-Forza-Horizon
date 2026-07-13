using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class ThemeService
{
    private readonly IReadOnlyList<ThemePack> _themes;

    public ThemeService()
    {
        _themes =
        [
            new ThemePack
            {
                Id = "theme-minimal-dark",
                DisplayName = "Minimal Dark",
                Colors = new ThemeColors
                {
                    Accent = "#6BB7FF",
                    Secondary = "#D8F3FF",
                    TextPrimary = "#FFFFFF",
                    TextSecondary = "#C0D0E0",
                    Lyrics = "#A0B8D0",
                    Surface = "#101820",
                    Border = "#66E6EEF7"
                }
            },
            new ThemePack
            {
                Id = "theme-forza-horizon-radio",
                DisplayName = "Horizon Radio",
                Colors = new ThemeColors
                {
                    Accent = "#F15A24",
                    Secondary = "#00AEEF",
                    TextPrimary = "#FFFFFF",
                    TextSecondary = "#D8F3FF",
                    Lyrics = "#B8ECFF",
                    Surface = "#101820",
                    Border = "#99F15A24"
                },
                Effects = new ThemeEffects
                {
                    GlassOpacity = 0.24,
                    MotionBlur = true
                }
            },
            new ThemePack
            {
                Id = "theme-lol-rift",
                DisplayName = "Summoner Rift",
                Colors = new ThemeColors
                {
                    Accent = "#C89B3C",
                    Secondary = "#0AC8B9",
                    TextPrimary = "#F0E6D2",
                    TextSecondary = "#C8AA6E",
                    Lyrics = "#A09B8C",
                    Surface = "#091428",
                    Border = "#88C89B3C"
                },
                Shape = new ThemeShape
                {
                    CornerRadius = 4,
                    CoverRadius = 3
                },
                Effects = new ThemeEffects
                {
                    GlassOpacity = 0.28,
                    MotionBlur = true
                }
            }
        ];
    }

    public IReadOnlyList<ThemePack> GetThemes()
    {
        return _themes;
    }

    public ThemePack GetByIdOrDefault(string? id)
    {
        return _themes.FirstOrDefault(theme =>
                string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) ??
            _themes[0];
    }
}
