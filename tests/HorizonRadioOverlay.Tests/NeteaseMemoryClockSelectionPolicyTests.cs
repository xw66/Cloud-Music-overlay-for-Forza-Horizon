using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class NeteaseMemoryClockValuePolicyTests
{
    [Fact]
    public void TryConvertSeconds_converts_valid_playback_position()
    {
        bool converted = NeteaseMemoryClockValuePolicy.TryConvertSeconds(
            264.849,
            NeteaseMemoryClockValuePolicy.MaximumPositionMicroseconds,
            out long valueMicroseconds);

        Assert.True(converted);
        Assert.Equal(264_849_000, valueMicroseconds);
    }

    [Theory]
    [InlineData(double.MaxValue)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    [InlineData(7_201.0)]
    public void TryConvertSeconds_rejects_invalid_or_out_of_range_values(double seconds)
    {
        bool converted = NeteaseMemoryClockValuePolicy.TryConvertSeconds(
            seconds,
            NeteaseMemoryClockValuePolicy.MaximumPositionMicroseconds,
            out long valueMicroseconds);

        Assert.False(converted);
        Assert.Equal(0, valueMicroseconds);
    }
}

public sealed class NeteaseMemoryClockSelectionPolicyTests
{
    [Fact]
    public void SelectConsensus_selects_matching_clocks_from_distinct_allocations()
    {
        NeteaseMemoryClockCandidate[] candidates =
        [
            new(0x1008, 0x1000, 55_170_748, 1_200),
            new(0x2008, 0x2000, 55_170_820, 900),
            new(0x3008, 0x3000, 16_777_216, 100),
            new(0x4008, 0x4000, 134_217_728, 100)
        ];

        IReadOnlyList<NeteaseMemoryClockCandidate> result =
            NeteaseMemoryClockSelectionPolicy.SelectConsensus(candidates);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, candidate => candidate.Address == 0x1008);
        Assert.Contains(result, candidate => candidate.Address == 0x2008);
    }

    [Fact]
    public void SelectConsensus_rejects_matching_values_from_one_allocation()
    {
        NeteaseMemoryClockCandidate[] candidates =
        [
            new(0x1008, 0x1000, 12_000_000, 100),
            new(0x1010, 0x1000, 12_000_020, 100),
            new(0x2008, 0x2000, 99_000_000, 100)
        ];

        IReadOnlyList<NeteaseMemoryClockCandidate> result =
            NeteaseMemoryClockSelectionPolicy.SelectConsensus(candidates);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectConsensus_prefers_non_backward_group_when_it_is_available()
    {
        NeteaseMemoryClockCandidate[] candidates =
        [
            new(0x1008, 0x1000, 800_000, 10),
            new(0x2008, 0x2000, 800_020, 10),
            new(0x3008, 0x3000, 800_040, 10),
            new(0x4008, 0x4000, 220_000_000, 80),
            new(0x5008, 0x5000, 220_000_020, 80)
        ];

        IReadOnlyList<NeteaseMemoryClockCandidate> result =
            NeteaseMemoryClockSelectionPolicy.SelectConsensus(
                candidates,
                preferredPositionMicroseconds: 219_800_000);

        Assert.Equal(2, result.Count);
        Assert.All(
            result,
            candidate => Assert.InRange(
                candidate.PositionMicroseconds,
                219_900_000,
                220_100_000));
    }

    [Fact]
    public void SelectConsensus_allows_backward_group_when_it_is_the_only_consensus()
    {
        NeteaseMemoryClockCandidate[] candidates =
        [
            new(0x1008, 0x1000, 20_000_000, 10),
            new(0x2008, 0x2000, 20_000_020, 10)
        ];

        IReadOnlyList<NeteaseMemoryClockCandidate> result =
            NeteaseMemoryClockSelectionPolicy.SelectConsensus(
                candidates,
                preferredPositionMicroseconds: 120_000_000);

        Assert.Equal(2, result.Count);
    }
}

public sealed class NeteaseMemoryClockPairingPolicyTests
{
    [Fact]
    public void SelectSegmentCorrelatedWithDouble_chooses_matching_int64_clock()
    {
        NeteaseMemoryClockCandidate[] int64Candidates =
        [
            new(0x1008, 0x1000, 4_160_000, 10),
            new(0x2008, 0x2000, 4_160_000, 10),
            new(0x3008, 0x3000, 4_160_000, 10),
            new(0x4008, 0x4000, 130_061_000, 100),
            new(0x5008, 0x5000, 130_061_020, 100)
        ];
        NeteaseMemoryClockCandidate[] doubleCandidates =
        [
            new(0x6008, 0x6000, 130_060_990, 80),
            new(0x7008, 0x7000, 130_061_010, 80)
        ];

        IReadOnlyList<NeteaseMemoryClockCandidate> result =
            NeteaseMemoryClockPairingPolicy.SelectSegmentCorrelatedWithDouble(
                int64Candidates,
                doubleCandidates);

        Assert.Equal(2, result.Count);
        Assert.All(
            result,
            candidate => Assert.InRange(candidate.PositionMicroseconds, 130_000_000, 130_100_000));
    }

    [Fact]
    public void SelectSegmentCorrelatedWithDouble_rejects_double_values_from_one_allocation()
    {
        NeteaseMemoryClockCandidate[] int64Candidates =
        [
            new(0x1008, 0x1000, 80_000_000, 10),
            new(0x2008, 0x2000, 80_000_000, 10)
        ];
        NeteaseMemoryClockCandidate[] doubleCandidates =
        [
            new(0x3008, 0x3000, 80_000_000, 10),
            new(0x3010, 0x3000, 80_000_000, 10)
        ];

        IReadOnlyList<NeteaseMemoryClockCandidate> result =
            NeteaseMemoryClockPairingPolicy.SelectSegmentCorrelatedWithDouble(
                int64Candidates,
                doubleCandidates);

        Assert.Empty(result);
    }
}

public sealed class NeteaseMemoryClockOffsetPolicyTests
{
    [Fact]
    public void ResolveBaseOffset_uses_absolute_clock_to_rebase_segment_after_seek()
    {
        long? result = NeteaseMemoryClockOffsetPolicy.ResolveBaseOffset(
            segmentPositionMicroseconds: 287_000,
            absolutePositionMicroseconds: 198_287_000);

        Assert.Equal(198_000_000, result);
    }

    [Fact]
    public void ResolveBaseOffset_clamps_small_sampling_jitter_to_zero()
    {
        long? result = NeteaseMemoryClockOffsetPolicy.ResolveBaseOffset(
            segmentPositionMicroseconds: 32_321_000,
            absolutePositionMicroseconds: 32_300_000);

        Assert.Equal(0, result);
    }

    [Fact]
    public void ResolveBaseOffset_rejects_absolute_clock_behind_segment()
    {
        long? result = NeteaseMemoryClockOffsetPolicy.ResolveBaseOffset(
            segmentPositionMicroseconds: 32_321_000,
            absolutePositionMicroseconds: 20_000_000);

        Assert.Null(result);
    }
}

public sealed class NeteaseMemorySegmentClockPolicyTests
{
    [Fact]
    public void IsReset_detects_segment_clock_restart_after_seek()
    {
        Assert.True(NeteaseMemorySegmentClockPolicy.IsReset(92_401_000, 287_000));
    }

    [Fact]
    public void IsReset_allows_normal_progress_and_small_jitter()
    {
        Assert.False(NeteaseMemorySegmentClockPolicy.IsReset(12_000_000, 12_100_000));
        Assert.False(NeteaseMemorySegmentClockPolicy.IsReset(12_000_000, 11_700_000));
        Assert.False(NeteaseMemorySegmentClockPolicy.IsReset(null, 2_000_000));
    }
}
