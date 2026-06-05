namespace HorizonRadioOverlay.Services;

public static class TrackSourcePolicy
{
    public static string GetLyricsTooltip(bool useSmtc)
    {
        return useSmtc
            ? "显示当前歌曲歌词；仅与系统 SMTC 会话同步"
            : "歌词功能仅在 SMTC 源可用";
    }
}
