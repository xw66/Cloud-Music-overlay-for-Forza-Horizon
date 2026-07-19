using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace HorizonRadioOverlay.Services;

public readonly record struct NeteaseClientCompatibilityResult(
    bool IsSupported,
    int? ProcessId,
    string ExecutablePath,
    string ProductVersion,
    string Sha256,
    nint IpcWindowHandle,
    string Reason);

public static class NeteaseClientCompatibility
{
    public const string SupportedProductVersion = "3.1.36.205322";
    public const string SupportedExecutableSha256 =
        "1B86292DA1056A729226DF9205EB9F69C1E5558BC3EDDBE0639C06A2443BF2D3";

    private static readonly nint MessageOnlyWindow = new(-3);

    public static bool IsSupported(string? productVersion, string? executableSha256)
    {
        return string.Equals(productVersion, SupportedProductVersion, StringComparison.Ordinal) &&
               string.Equals(
                   executableSha256,
                   SupportedExecutableSha256,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static NeteaseClientCompatibilityResult InspectRunningClient()
    {
        using Process? process = FindMainProcess();
        if (process == null)
        {
            return Unsupported("未找到网易云主进程");
        }

        string executablePath;
        string productVersion;
        try
        {
            executablePath = process.MainModule?.FileName ?? string.Empty;
            productVersion = process.MainModule?.FileVersionInfo.ProductVersion ?? string.Empty;
        }
        catch (Exception ex)
        {
            return Unsupported($"无法读取网易云版本：{ex.Message}", process.Id);
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return Unsupported("网易云主程序路径无效", process.Id);
        }

        string sha256;
        try
        {
            using FileStream stream = File.OpenRead(executablePath);
            sha256 = Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex)
        {
            return Unsupported($"无法校验网易云主程序：{ex.Message}", process.Id, executablePath, productVersion);
        }

        nint ipcWindow = FindMessageOnlyWindow("orpheus_ipc_window", process.Id.ToString());
        if (!string.Equals(productVersion, SupportedProductVersion, StringComparison.Ordinal))
        {
            return Unsupported(
                $"未适配的网易云版本 {productVersion}",
                process.Id,
                executablePath,
                productVersion,
                sha256,
                ipcWindow);
        }

        if (!IsSupported(productVersion, sha256))
        {
            return Unsupported(
                "网易云主程序哈希与已验证版本不一致",
                process.Id,
                executablePath,
                productVersion,
                sha256,
                ipcWindow);
        }

        if (ipcWindow == 0)
        {
            return Unsupported(
                "未找到 orpheus_ipc_window",
                process.Id,
                executablePath,
                productVersion,
                sha256);
        }

        return new NeteaseClientCompatibilityResult(
            true,
            process.Id,
            executablePath,
            productVersion,
            sha256,
            ipcWindow,
            "已验证网易云版本及 IPC 入口");
    }

    private static NeteaseClientCompatibilityResult Unsupported(
        string reason,
        int? processId = null,
        string executablePath = "",
        string productVersion = "",
        string sha256 = "",
        nint ipcWindow = 0)
    {
        return new NeteaseClientCompatibilityResult(
            false,
            processId,
            executablePath,
            productVersion,
            sha256,
            ipcWindow,
            reason);
    }

    private static Process? FindMainProcess()
    {
        return Process.GetProcessesByName("cloudmusic")
            .Where(process => process.MainWindowHandle != 0)
            .OrderBy(process => process.StartTime)
            .FirstOrDefault();
    }

    private static nint FindMessageOnlyWindow(string className, string title)
    {
        nint current = 0;
        while (true)
        {
            current = FindWindowEx(MessageOnlyWindow, current, className, title);
            if (current == 0)
            {
                return 0;
            }

            _ = GetWindowThreadProcessId(current, out uint processId);
            if (uint.TryParse(title, out uint expectedProcessId) && processId == expectedProcessId)
            {
                return current;
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowEx(
        nint parentHandle,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}
