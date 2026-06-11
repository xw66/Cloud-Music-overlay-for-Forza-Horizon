using System.Net;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class DiagnosticContextTests
{
    [Fact]
    public void Format_includes_trace_component_stage_and_fields()
    {
        string line = DiagnosticContext.Format(
            "ABC12345",
            "netease-cover",
            "download",
            ("status", "download-failed"),
            ("title", "A B"));

        Assert.Contains("[trace=ABC12345]", line);
        Assert.Contains("[component=netease-cover]", line);
        Assert.Contains("[stage=download]", line);
        Assert.Contains("status=download-failed", line);
        Assert.Contains("title=\"A B\"", line);
    }

    [Theory]
    [InlineData(typeof(TaskCanceledException), "timeout")]
    [InlineData(typeof(UnauthorizedAccessException), "file-access-denied")]
    [InlineData(typeof(System.Security.Authentication.AuthenticationException), "proxy-or-tls-failed")]
    public void ClassifyException_returns_actionable_root_cause(Type exceptionType, string expected)
    {
        Exception exception = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.Equal(expected, DiagnosticContext.ClassifyException(exception));
    }

    [Fact]
    public void ClassifyHttpStatus_includes_status_code()
    {
        Assert.Equal("http-403", DiagnosticContext.ClassifyHttpStatus(HttpStatusCode.Forbidden));
        Assert.Equal("http-503", DiagnosticContext.ClassifyHttpStatus(HttpStatusCode.ServiceUnavailable));
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, true)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, true)]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D }, false)]
    public void IsLikelyImage_checks_known_image_headers(byte[] bytes, bool expected)
    {
        Assert.Equal(expected, DiagnosticContext.IsLikelyImage(bytes));
    }
}
