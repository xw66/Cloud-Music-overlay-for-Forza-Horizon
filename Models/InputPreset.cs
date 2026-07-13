namespace HorizonRadioOverlay.Models;

public sealed class InputPreset
{
    public string Id { get; init; } = "default-media";
    public KeyboardInputPreset Keyboard { get; init; } = new();
    public GamepadInputPreset Gamepad { get; init; } = new();
    public InputTargetPreset Target { get; init; } = new();
}

public sealed class KeyboardInputPreset
{
    public string Previous { get; init; } = "Ctrl+Shift+Left";
    public string Next { get; init; } = "Ctrl+Shift+Right";
    public string PlayPause { get; init; } = "Ctrl+Shift+Down";
}

public sealed class GamepadInputPreset
{
    public string Previous { get; init; } = "LB+Left";
    public string Next { get; init; } = "LB+Right";
    public string PlayPause { get; init; } = "LB+A";
}

public sealed class InputTargetPreset
{
    public string Type { get; init; } = "smtc-or-hotkey";
    public string Fallback { get; init; } = "cloudmusic-hotkey";
}
