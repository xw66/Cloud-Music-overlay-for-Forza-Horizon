using System.Diagnostics;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

internal readonly record struct NeteaseMemoryClockCandidate(
    long Address,
    long AllocationBase,
    long PositionMicroseconds,
    long DriftMicroseconds);

internal static class NeteaseMemoryClockValuePolicy
{
    internal const long MaximumPositionMicroseconds =
        2L * 60 * 60 * 1_000_000;

    public static bool TryConvertSeconds(
        double seconds,
        long maximumPositionMicroseconds,
        out long valueMicroseconds)
    {
        valueMicroseconds = 0;
        if (!double.IsFinite(seconds) ||
            seconds < 0 ||
            maximumPositionMicroseconds < 0 ||
            seconds > maximumPositionMicroseconds / 1_000_000.0)
        {
            return false;
        }

        double microseconds = seconds * 1_000_000.0;
        if (!double.IsFinite(microseconds) ||
            microseconds < 0 ||
            microseconds > maximumPositionMicroseconds)
        {
            return false;
        }

        valueMicroseconds = (long)Math.Round(microseconds);
        return valueMicroseconds <= maximumPositionMicroseconds;
    }
}

internal static class NeteaseMemoryClockSelectionPolicy
{
    internal const long ConsensusToleranceMicroseconds = 100_000;
    private const long ContinuityBackwardToleranceMicroseconds = 1_000_000;

    public static IReadOnlyList<NeteaseMemoryClockCandidate> SelectConsensus(
        IEnumerable<NeteaseMemoryClockCandidate> candidates,
        long? preferredPositionMicroseconds = null)
    {
        NeteaseMemoryClockCandidate[] distinct = candidates
            .Where(candidate => candidate.PositionMicroseconds >= 0)
            .GroupBy(candidate => candidate.Address)
            .Select(group => group.OrderBy(candidate => candidate.DriftMicroseconds).First())
            .ToArray();

        if (preferredPositionMicroseconds.HasValue)
        {
            NeteaseMemoryClockCandidate[] continuous = distinct
                .Where(candidate =>
                    candidate.PositionMicroseconds >=
                    preferredPositionMicroseconds.Value -
                    ContinuityBackwardToleranceMicroseconds)
                .ToArray();
            IReadOnlyList<NeteaseMemoryClockCandidate> continuousConsensus =
                SelectBestConsensus(continuous);
            if (continuousConsensus.Count >= 2)
            {
                return continuousConsensus;
            }
        }

        return SelectBestConsensus(distinct);
    }

    private static IReadOnlyList<NeteaseMemoryClockCandidate> SelectBestConsensus(
        IReadOnlyList<NeteaseMemoryClockCandidate> candidates)
    {
        NeteaseMemoryClockCandidate[] best = [];
        int bestAllocationCount = 0;
        long bestDrift = long.MaxValue;

        foreach (NeteaseMemoryClockCandidate center in candidates)
        {
            NeteaseMemoryClockCandidate[] group = candidates
                .Where(candidate =>
                    Math.Abs(candidate.PositionMicroseconds - center.PositionMicroseconds) <=
                    ConsensusToleranceMicroseconds)
                .ToArray();
            int allocationCount = group
                .Select(candidate => candidate.AllocationBase)
                .Distinct()
                .Count();
            long drift = group.Sum(candidate => candidate.DriftMicroseconds);

            if (allocationCount > bestAllocationCount ||
                (allocationCount == bestAllocationCount && group.Length > best.Length) ||
                (allocationCount == bestAllocationCount && group.Length == best.Length && drift < bestDrift))
            {
                best = group;
                bestAllocationCount = allocationCount;
                bestDrift = drift;
            }
        }

        return bestAllocationCount >= 2 ? best : [];
    }
}

internal static class NeteaseMemoryClockPairingPolicy
{
    public static IReadOnlyList<NeteaseMemoryClockCandidate> SelectSegmentCorrelatedWithDouble(
        IEnumerable<NeteaseMemoryClockCandidate> int64Candidates,
        IEnumerable<NeteaseMemoryClockCandidate> doubleCandidates)
    {
        NeteaseMemoryClockCandidate[] int64Values = int64Candidates.ToArray();
        NeteaseMemoryClockCandidate[] doubleValues = doubleCandidates.ToArray();
        NeteaseMemoryClockCandidate[] best = [];
        int bestAllocationScore = 0;
        long bestDrift = long.MaxValue;

        foreach (NeteaseMemoryClockCandidate center in int64Values)
        {
            NeteaseMemoryClockCandidate[] int64Group = int64Values
                .Where(candidate =>
                    Math.Abs(candidate.PositionMicroseconds - center.PositionMicroseconds) <=
                    NeteaseMemoryClockSelectionPolicy.ConsensusToleranceMicroseconds)
                .ToArray();
            NeteaseMemoryClockCandidate[] doubleGroup = doubleValues
                .Where(candidate =>
                    Math.Abs(candidate.PositionMicroseconds - center.PositionMicroseconds) <=
                    NeteaseMemoryClockSelectionPolicy.ConsensusToleranceMicroseconds)
                .ToArray();
            int int64Allocations = int64Group
                .Select(candidate => candidate.AllocationBase)
                .Distinct()
                .Count();
            int doubleAllocations = doubleGroup
                .Select(candidate => candidate.AllocationBase)
                .Distinct()
                .Count();
            if (int64Allocations < 2 || doubleAllocations < 2)
            {
                continue;
            }

            int allocationScore = int64Allocations + doubleAllocations;
            long drift = int64Group.Sum(candidate => candidate.DriftMicroseconds) +
                         doubleGroup.Sum(candidate => candidate.DriftMicroseconds);
            if (allocationScore > bestAllocationScore ||
                (allocationScore == bestAllocationScore && int64Group.Length > best.Length) ||
                (allocationScore == bestAllocationScore && int64Group.Length == best.Length && drift < bestDrift))
            {
                best = int64Group;
                bestAllocationScore = allocationScore;
                bestDrift = drift;
            }
        }

        return best;
    }
}

internal static class NeteaseMemoryClockOffsetPolicy
{
    private const long NegativeToleranceMicroseconds = 150_000;
    private const long NearZeroOffsetMicroseconds = 1_000_000;

    public static long? ResolveBaseOffset(
        long segmentPositionMicroseconds,
        long absolutePositionMicroseconds)
    {
        long offset = absolutePositionMicroseconds - segmentPositionMicroseconds;
        if (offset < -NegativeToleranceMicroseconds)
        {
            return null;
        }

        return Math.Max(0, offset);
    }

    public static bool IsPlausibleSeekRebase(
        long? previousAbsolutePositionMicroseconds,
        long baseOffsetMicroseconds)
    {
        return !previousAbsolutePositionMicroseconds.HasValue ||
               previousAbsolutePositionMicroseconds.Value <= NearZeroOffsetMicroseconds ||
               baseOffsetMicroseconds >= NearZeroOffsetMicroseconds;
    }
}

internal static class NeteaseMemorySegmentClockPolicy
{
    internal const long SeekResetThresholdMicroseconds = 500_000;

    public static bool IsReset(long? previousPositionMicroseconds, long positionMicroseconds)
    {
        return previousPositionMicroseconds.HasValue &&
               positionMicroseconds <
               previousPositionMicroseconds.Value - SeekResetThresholdMicroseconds;
    }
}

internal static class NeteaseMemoryActivityPolicy
{
    public static long ExtrapolatePosition(
        long lastPositionMicroseconds,
        DateTime lastSampleUtc,
        bool wasPlaying,
        DateTime nowUtc,
        long maximumPositionMicroseconds)
    {
        if (!wasPlaying || lastSampleUtc == default || nowUtc <= lastSampleUtc)
        {
            return Math.Clamp(
                lastPositionMicroseconds,
                0,
                maximumPositionMicroseconds);
        }

        long elapsedMicroseconds = (long)Math.Round(
            (nowUtc - lastSampleUtc).TotalMilliseconds * 1_000);
        return Math.Clamp(
            lastPositionMicroseconds + Math.Max(0, elapsedMicroseconds),
            0,
            maximumPositionMicroseconds);
    }
}
