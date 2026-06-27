using System.Net;

namespace HorizonRadioOverlay.Services;

public static class RemoteControlPolicy
{
    public const int DefaultPort = 37666;
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "previous",
        "next",
        "togglePlayPause",
        "toggleOverlay"
    };

    public static bool IsAllowedAction(string? action)
    {
        return !string.IsNullOrWhiteSpace(action) && AllowedActions.Contains(action.Trim());
    }

    public static bool IsAuthorized(string? providedToken, string expectedToken)
    {
        return !string.IsNullOrWhiteSpace(expectedToken)
            && !string.IsNullOrWhiteSpace(providedToken)
            && string.Equals(providedToken.Trim(), expectedToken, StringComparison.Ordinal);
    }

    public static int NormalizePort(int port)
    {
        if (port < MinPort) return DefaultPort;
        if (port > MaxPort) return DefaultPort;
        return port;
    }

    public static bool IsUsableLanAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && !IPAddress.IsLoopback(address)
            && bytes.Length == 4
            && bytes[0] != 0
            && !(bytes[0] == 169 && bytes[1] == 254);
    }

    public static bool IsPrivateIpv4(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4
            && (bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168));
    }
}
