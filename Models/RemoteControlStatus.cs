namespace HorizonRadioOverlay.Models;

public sealed class RemoteControlStatus
{
    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public bool SupportsSmtc { get; set; }

    public bool OverlayVisible { get; set; }

    public bool RemoteEnabled { get; set; }
}
