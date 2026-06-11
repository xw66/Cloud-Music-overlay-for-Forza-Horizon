namespace HorizonRadioOverlay.Services;

public static class TrackSourcePolicy
{
    public static bool ShouldEnableLyrics(string? trackSource)
    {
        return string.Equals(trackSource, "SMTC", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trackSource, "NeteaseProcess", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetLyricsTooltip(bool useSmtc)
    {
        return useSmtc
            ? "\u4f7f\u7528 SMTC \u65f6\u95f4\u8f74\u540c\u6b65\u663e\u793a\u6b4c\u8bcd"
            : "\u4f7f\u7528\u7f51\u6613\u4e91\u64ad\u653e\u8fdb\u5ea6\u4e0e\u672c\u5730\u8ba1\u65f6\u540c\u6b65\u663e\u793a\u6b4c\u8bcd";
    }
}
