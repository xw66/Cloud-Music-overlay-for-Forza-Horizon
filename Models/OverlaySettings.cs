namespace HorizonRadioOverlay.Models;

public sealed class OverlaySettings
{
    public string TrackSource { get; set; } = "NeteaseProcess";

    public double LeftPercent { get; set; } = 0.0;
    public double TopPercent { get; set; } = 0.59;
    public double Scale { get; set; } = 1.0;

    public string TitleColor { get; set; } = "#F4F9FF";
    public string ArtistColor { get; set; } = "#E2ECF7";
    public double TitleOpacity { get; set; } = 1.0;
    public double ArtistOpacity { get; set; } = 1.0;

    public string AppPrevHotkey { get; set; } = "Ctrl+Shift+Left";
    public string AppNextHotkey { get; set; } = "Ctrl+Shift+Right";
    public string AppToggleHotkey { get; set; } = "Ctrl+Shift+Down";

    public string NeteasePrevHotkey { get; set; } = "Ctrl+Alt+Left";
    public string NeteaseNextHotkey { get; set; } = "Ctrl+Alt+Right";
    public string NeteaseToggleHotkey { get; set; } = "Ctrl+Alt+P";

    public bool EnableGamepadHotkeys { get; set; } = false;
    public string GamepadPrevHotkey { get; set; } = "LB+Left";
    public string GamepadNextHotkey { get; set; } = "RB+Right";
    public string GamepadToggleHotkey { get; set; } = "LT+RT+Y";

    public bool MinimizeToTray { get; set; } = true;
    public bool AutoStartOnBoot { get; set; } = false;
}
