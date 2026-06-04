using System.Runtime.Versioning;

namespace HorizonRadioOverlay.Services;

public static class RuntimeFeatureSupport
{
    [SupportedOSPlatformGuard("windows10.0.17763")]
    public static bool SupportsSmtc()
    {
        return EvaluateSmtcSupport(OperatingSystem.IsWindows(), Environment.OSVersion.Version.Build);
    }

    [SupportedOSPlatformGuard("windows")]
    public static bool SupportsTrayIcon()
    {
        return OperatingSystem.IsWindows();
    }

    [SupportedOSPlatformGuard("windows")]
    public static bool SupportsRegistryAutoStart()
    {
        return OperatingSystem.IsWindows();
    }

    public static bool EvaluateSmtcSupport(bool isWindows, int build)
    {
        return isWindows && build >= 17763;
    }
}
