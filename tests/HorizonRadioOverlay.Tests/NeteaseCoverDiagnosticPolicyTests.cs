using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class NeteaseCoverDiagnosticPolicyTests
{
    [Theory]
    [InlineData(NeteaseCoverDiagnosticPolicy.ResolveFailed, "官方歌曲解析失败")]
    [InlineData(NeteaseCoverDiagnosticPolicy.CoverUrlMissing, "官方结果没有封面地址")]
    [InlineData(NeteaseCoverDiagnosticPolicy.DownloadFailed, "封面下载失败")]
    [InlineData(NeteaseCoverDiagnosticPolicy.WindowTitleMissing, "未检测到有效网易云窗口标题")]
    public void FormatSourceAppId_appends_cover_failure_reason(string coverSource, string expectedReason)
    {
        string result = NeteaseCoverDiagnosticPolicy.FormatSourceAppId("CloudMusic(OfficialSearch)", coverSource);

        Assert.Equal($"CloudMusic(OfficialSearch) | 封面失败：{expectedReason}", result);
    }

    [Theory]
    [InlineData(NeteaseCoverDiagnosticPolicy.CacheHit)]
    [InlineData(NeteaseCoverDiagnosticPolicy.Downloaded)]
    public void FormatSourceAppId_keeps_success_source_clean(string coverSource)
    {
        string result = NeteaseCoverDiagnosticPolicy.FormatSourceAppId("CloudMusic(OfficialSearch)", coverSource);

        Assert.Equal("CloudMusic(OfficialSearch)", result);
    }
}
