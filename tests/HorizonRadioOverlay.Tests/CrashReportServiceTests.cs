using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class CrashReportServiceTests
{
    [Fact]
    public void FormatReport_includes_origin_exception_and_playback_context()
    {
        InvalidOperationException exception = new("boom");

        string report = CrashReportService.FormatReport(
            "test-origin",
            exception,
            terminating: true,
            "source=NeteaseProcess; positionSeconds=42.000",
            new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero));

        Assert.Contains("Origin: test-origin", report);
        Assert.Contains("Terminating: True", report);
        Assert.Contains("System.InvalidOperationException", report);
        Assert.Contains("source=NeteaseProcess", report);
        Assert.Contains("boom", report);
    }

    [Theory]
    [InlineData(typeof(OverflowException), true)]
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    public void IsRecoverableUiException_classifies_known_failures(
        Type exceptionType,
        bool expected)
    {
        Exception exception = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.Equal(expected, CrashReportService.IsRecoverableUiException(exception));
    }
}
