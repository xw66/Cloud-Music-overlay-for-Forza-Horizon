using System.Runtime.Versioning;
using HorizonRadioOverlay.Services;
using Xunit;

namespace HorizonRadioOverlay.Tests;

[SupportedOSPlatform("windows")]
public class NeteaseMemoryNativeReaderTests
{
    [Theory]
    [InlineData(0x04u, true)]  // PAGE_READWRITE
    [InlineData(0x08u, true)]  // PAGE_WRITECOPY
    [InlineData(0x40u, true)]  // PAGE_EXECUTE_READWRITE
    [InlineData(0x80u, true)]  // PAGE_EXECUTE_WRITECOPY
    [InlineData(0x01u, false)] // PAGE_NOACCESS
    [InlineData(0x02u, false)] // PAGE_READONLY
    [InlineData(0x10u, false)] // PAGE_EXECUTE
    [InlineData(0x20u, false)] // PAGE_EXECUTE_READ
    public void IsWritable_IdentifiesWritablePagesCorrectly(uint protection, bool expected)
    {
        bool actual = NeteaseMemoryNativeReader.IsWritable(protection);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Median_ComputesCorrectValue_ForOddAndEvenCollections()
    {
        Assert.Equal(0, NeteaseMemoryClockScanner.Median([]));
        Assert.Equal(42, NeteaseMemoryClockScanner.Median([42]));
        Assert.Equal(20, NeteaseMemoryClockScanner.Median([30, 10, 20]));
        Assert.Equal(25, NeteaseMemoryClockScanner.Median([40, 10, 30, 20]));
    }

    [Fact]
    public void MemoryClockCandidate_AndLocation_AreInitializedCorrectly()
    {
        NeteaseMemoryClockCandidate candidate = new(0x1000, 0x0800, 500_000, 150);
        Assert.Equal(0x1000, candidate.Address);
        Assert.Equal(0x0800, candidate.AllocationBase);
        Assert.Equal(500_000, candidate.PositionMicroseconds);
        Assert.Equal(150, candidate.DriftMicroseconds);

        MemoryClockLocation location = new(0x2000, 0x1800);
        Assert.Equal(0x2000, location.Address);
        Assert.Equal(0x1800, location.AllocationBase);
    }
}
