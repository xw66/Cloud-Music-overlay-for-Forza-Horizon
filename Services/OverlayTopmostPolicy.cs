namespace HorizonRadioOverlay.Services;

public static class OverlayTopmostPolicy
{
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;

    private const uint SwpNosize = 0x0001;
    private const uint SwpNomove = 0x0002;
    private const uint SwpShowwindow = 0x0040;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpNoownerzorder = 0x0200;
    private const uint SwpNoSendChanging = 0x0400;

    public static TimeSpan ReassertInterval => TimeSpan.FromSeconds(1);

    public static TimeSpan BoostedReassertInterval => TimeSpan.FromMilliseconds(120);

    public static TimeSpan BoostedReassertDuration => TimeSpan.FromSeconds(12);

    public static int ApplyExtendedStyle(int existingStyle)
    {
        return existingStyle | WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow;
    }

    public static uint GetTopmostFlags()
    {
        return SwpNosize | SwpNomove | SwpNoactivate | SwpNoownerzorder | SwpNoSendChanging | SwpShowwindow;
    }
}
