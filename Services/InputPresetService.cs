using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class InputPresetService
{
    private readonly IReadOnlyList<InputPreset> _presets =
    [
        new InputPreset
        {
            Id = "default-media",
            Keyboard = new KeyboardInputPreset
            {
                Previous = "Ctrl+Shift+Left",
                Next = "Ctrl+Shift+Right",
                PlayPause = "Ctrl+Shift+Down"
            },
            Gamepad = new GamepadInputPreset
            {
                Previous = "LB+Left",
                Next = "RB+Right",
                PlayPause = "LT+RT+Y"
            },
            Target = new InputTargetPreset
            {
                Type = "smtc-or-hotkey",
                Fallback = "cloudmusic-hotkey"
            }
        }
    ];

    public IReadOnlyList<InputPreset> GetPresets()
    {
        return _presets;
    }

    public InputPreset GetByIdOrDefault(string? id)
    {
        return _presets.FirstOrDefault(preset =>
                string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase)) ??
            _presets[0];
    }
}
