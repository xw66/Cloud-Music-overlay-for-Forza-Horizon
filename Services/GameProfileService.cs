using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class GameProfileService
{
    private readonly IReadOnlyList<GameProfile> _profiles;

    public GameProfileService()
    {
        _profiles =
        [
            new GameProfile
            {
                Id = "generic-game",
                DisplayName = "通用游戏",
                ProcessNames = [],
                WindowTitlePatterns = [],
                ThemeId = "theme-minimal-dark",
                DefaultPlacement = new OverlayPlacement
                {
                    Anchor = "top-right",
                    XPercent = 82,
                    YPercent = 14,
                    Scale = 1.0
                },
                OverlayMode = OverlayMode.SlideRadio,
                InputPresetId = "default-media"
            },
            new GameProfile
            {
                Id = "forza-horizon",
                DisplayName = "Forza Horizon",
                ProcessNames = ["ForzaHorizon4.exe", "ForzaHorizon5.exe"],
                WindowTitlePatterns = ["Forza Horizon"],
                ThemeId = "theme-forza-horizon-radio",
                DefaultPlacement = new OverlayPlacement
                {
                    Anchor = "left-middle",
                    XPercent = 0,
                    YPercent = 59,
                    Scale = 1.0
                },
                OverlayMode = OverlayMode.SlideRadio,
                InputPresetId = "default-media"
            },
            new GameProfile
            {
                Id = "league-of-legends",
                DisplayName = "League of Legends",
                ProcessNames = ["League of Legends.exe", "LeagueClientUx.exe"],
                WindowTitlePatterns = ["League of Legends"],
                ThemeId = "theme-lol-rift",
                DefaultPlacement = new OverlayPlacement
                {
                    Anchor = "top-right",
                    XPercent = 82,
                    YPercent = 14,
                    Scale = 1.0
                },
                SafeAreas =
                [
                    new OverlaySafeArea
                    {
                        Name = "minimap",
                        Anchor = "bottom-right",
                        WidthPercent = 22,
                        HeightPercent = 26
                    }
                ],
                OverlayMode = OverlayMode.SlideRadio,
                InputPresetId = "default-media"
            }
        ];
    }

    public IReadOnlyList<GameProfile> GetProfiles()
    {
        return _profiles;
    }

    public GameProfile GetByIdOrDefault(string? id)
    {
        return _profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase)) ??
            _profiles[0];
    }

    public GameProfile MatchOrDefault(string? processName, string? windowTitle)
    {
        foreach (GameProfile profile in _profiles.Skip(1))
        {
            bool processMatches = !string.IsNullOrWhiteSpace(processName) &&
                profile.ProcessNames.Any(candidate => string.Equals(candidate, processName, StringComparison.OrdinalIgnoreCase));

            bool titleMatches = !string.IsNullOrWhiteSpace(windowTitle) &&
                profile.WindowTitlePatterns.Any(candidate => windowTitle.Contains(candidate, StringComparison.OrdinalIgnoreCase));

            if (processMatches || titleMatches)
            {
                return profile;
            }
        }

        return _profiles[0];
    }
}
