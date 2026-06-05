namespace HorizonRadioOverlay.Services;

public static class AppLaunchPolicy
{
    public const string AutoStartArgument = "--autostart";

    public static bool ShouldStartHiddenToTray(string[] args)
    {
        return args.Any(arg => string.Equals(arg, AutoStartArgument, StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildAutoStartCommand(string exePath)
    {
        return $"\"{exePath}\" {AutoStartArgument}";
    }
}
