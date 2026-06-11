namespace HorizonRadioOverlay.Services;

public static class NeteaseCoverDiagnosticPolicy
{
    public const string CacheHit = "netease-cache";
    public const string Downloaded = "netease-download";
    public const string ResolveFailed = "resolve-failed";
    public const string CoverUrlMissing = "cover-url-missing";
    public const string DownloadFailed = "download-failed";
    public const string WindowTitleMissing = "window-title-missing";

    public static string FormatSourceAppId(string sourceAppId, string coverSource)
    {
        string? failureReason = GetFailureReason(coverSource);
        return string.IsNullOrWhiteSpace(failureReason)
            ? sourceAppId
            : $"{sourceAppId} | \u5c01\u9762\u5931\u8d25\uff1a{failureReason}";
    }

    public static string? GetFailureReason(string coverSource)
    {
        return coverSource switch
        {
            ResolveFailed => "\u5b98\u65b9\u6b4c\u66f2\u89e3\u6790\u5931\u8d25",
            CoverUrlMissing => "\u5b98\u65b9\u7ed3\u679c\u6ca1\u6709\u5c01\u9762\u5730\u5740",
            DownloadFailed => "\u5c01\u9762\u4e0b\u8f7d\u5931\u8d25",
            WindowTitleMissing => "\u672a\u68c0\u6d4b\u5230\u6709\u6548\u7f51\u6613\u4e91\u7a97\u53e3\u6807\u9898",
            _ => null
        };
    }
}
