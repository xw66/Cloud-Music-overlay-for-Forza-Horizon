using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
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

[SupportedOSPlatform("windows")]
public sealed class NeteaseMemoryPlaybackProbe : ITrackAwarePlaybackStateProvider
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint MemCommit = 0x1000;
    private const uint MemPrivate = 0x20000;
    private const uint PageNoAccess = 0x01;
    private const uint PageReadWrite = 0x04;
    private const uint PageWriteCopy = 0x08;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint PageExecuteWriteCopy = 0x80;
    private const uint PageGuard = 0x100;
    private const int ScanChunkBytes = 1024 * 1024;
    private const long MaximumScanBytes = 768L * 1024 * 1024;
    private const long MaximumFastRebaseBytesPerAllocation = 32L * 1024 * 1024;
    private const int MaximumSeedCount = 1_000_000;
    private const int MaximumDoubleSeedCount = 500_000;
    private const int MaximumPassCandidateCount = 8_192;
    private const long MinimumPositionMicroseconds = 250_000;
    private const long DefaultMaximumPositionMicroseconds = 20L * 60 * 1_000_000;
    private const long ScanCooldownMilliseconds = 4_000;
    private const long CachedConsensusToleranceMicroseconds = 150_000;
    private const long MovementThresholdMicroseconds = 20_000;
    private const int PauseDetectionMilliseconds = 350;
    private const int FastRebasePassDelayMilliseconds = 120;
    private const int FastRebaseFinalDelayMilliseconds = 160;

    private readonly DiagnosticService _diagnostic;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _stateGate = new();
    private string _trackKey = string.Empty;
    private int _trackGeneration;
    private long _maximumPositionMicroseconds = DefaultMaximumPositionMicroseconds;
    private int? _processId;
    private MemoryClockLocation[] _locations = [];
    private MemoryClockLocation[] _absoluteClockLocations = [];
    private MemoryScanHint[] _absoluteClockHints = [];
    private long _baseOffsetMicroseconds;
    private long? _lastSegmentPositionMicroseconds;
    private long? _lastPositionMicroseconds;
    private DateTime _lastSampleUtc;
    private bool _lastIsPlaying = true;
    private bool _pendingRebase;
    private long _lastScanAttemptAt;

    public NeteaseMemoryPlaybackProbe(DiagnosticService diagnostic)
    {
        _diagnostic = diagnostic;
    }

    public void SetTrackContext(TrackInfo? track)
    {
        string trackKey = track == null
            ? string.Empty
            : $"meta:{TrackIdentity.BuildTrackKey(track, includeSourceAppId: false)}";
        long maximumPositionMicroseconds = track?.DurationSeconds > 0
            ? checked((long)Math.Ceiling((track.DurationSeconds + 5) * 1_000_000))
            : DefaultMaximumPositionMicroseconds;
        maximumPositionMicroseconds = Math.Clamp(
            maximumPositionMicroseconds,
            30L * 1_000_000,
            NeteaseMemoryClockValuePolicy.MaximumPositionMicroseconds);

        lock (_stateGate)
        {
            _maximumPositionMicroseconds = maximumPositionMicroseconds;
            if (string.Equals(_trackKey, trackKey, StringComparison.Ordinal))
            {
                return;
            }

            _trackKey = trackKey;
            _trackGeneration++;
            _processId = null;
            _locations = [];
            _absoluteClockLocations = [];
            _absoluteClockHints = [];
            _baseOffsetMicroseconds = 0;
            _lastSegmentPositionMicroseconds = null;
            _lastPositionMicroseconds = null;
            _lastSampleUtc = default;
            _lastIsPlaying = true;
            _pendingRebase = false;
            _lastScanAttemptAt = 0;
        }
    }

    public async Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync()
    {
        if (!Environment.Is64BitProcess)
        {
            return null;
        }

        if (TryReadCachedStateSafely(out (TimeSpan Position, bool IsPlaying) cachedState))
        {
            return cachedState;
        }

        await _scanGate.WaitAsync();
        try
        {
            if (TryReadCachedStateSafely(out cachedState))
            {
                return cachedState;
            }

            int generation;
            long maximumPositionMicroseconds;
            MemoryClockLocation[] knownSegmentLocations;
            MemoryScanHint[] knownAbsoluteClockHints;
            long? preferredPositionMicroseconds;
            lock (_stateGate)
            {
                long now = Environment.TickCount64;
                if (!_pendingRebase &&
                    _lastScanAttemptAt != 0 &&
                    now - _lastScanAttemptAt < ScanCooldownMilliseconds)
                {
                    return null;
                }

                _lastScanAttemptAt = now;
                generation = _trackGeneration;
                maximumPositionMicroseconds = _maximumPositionMicroseconds;
                knownSegmentLocations = _pendingRebase ? _locations : [];
                knownAbsoluteClockHints = _pendingRebase ? _absoluteClockHints : [];
                preferredPositionMicroseconds = _lastPositionMicroseconds;
            }

            MemoryClockDiscovery? discovery = await Task.Run(
                () => DiscoverClock(
                    maximumPositionMicroseconds,
                    knownSegmentLocations,
                    knownAbsoluteClockHints,
                    preferredPositionMicroseconds));
            if (discovery == null)
            {
                return null;
            }

            long absolutePositionMicroseconds = discovery.PositionMicroseconds;
            lock (_stateGate)
            {
                if (generation != _trackGeneration)
                {
                    return null;
                }

                _processId = discovery.ProcessId;
                _locations = discovery.Locations;
                _absoluteClockLocations = discovery.AbsoluteClockLocations;
                _absoluteClockHints = discovery.AbsoluteClockHints;
                _baseOffsetMicroseconds = discovery.BaseOffsetMicroseconds;
                _lastSegmentPositionMicroseconds = discovery.SegmentPositionMicroseconds;
                _lastPositionMicroseconds = absolutePositionMicroseconds;
                _lastSampleUtc = DateTime.UtcNow;
                _lastIsPlaying = true;
                _pendingRebase = false;
            }

            _diagnostic.Info(
                $"Netease read-only memory clock discovered: version={discovery.ProductVersion}, " +
                $"kind={(discovery.Locations.Length >= 2 ? "paired-segment" : "absolute-double-anchor")}, " +
                $"locations={discovery.Locations.Length}, " +
                $"base={discovery.BaseOffsetMicroseconds / 1_000_000.0:F3}s, " +
                $"position={absolutePositionMicroseconds / 1_000_000.0:F3}s");
            return (TimeSpan.FromTicks(absolutePositionMicroseconds * 10), true);
        }
        catch (Exception ex)
        {
            _diagnostic.Info($"Netease read-only memory clock unavailable: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private bool TryReadCachedStateSafely(
        out (TimeSpan Position, bool IsPlaying) state)
    {
        try
        {
            return TryReadCachedState(out state);
        }
        catch (Exception ex)
        {
            state = default;
            InvalidateLocations();
            _diagnostic.Info(
                $"Netease cached memory clock invalidated after read failure: " +
                $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private bool TryReadCachedState(out (TimeSpan Position, bool IsPlaying) state)
    {
        state = default;
        int? processId;
        MemoryClockLocation[] segmentLocations;
        MemoryClockLocation[] absoluteLocations;
        long maximumPositionMicroseconds;
        bool pendingRebase;
        long? preferredPositionMicroseconds;

        lock (_stateGate)
        {
            processId = _processId;
            segmentLocations = _locations;
            absoluteLocations = _absoluteClockLocations;
            maximumPositionMicroseconds = _maximumPositionMicroseconds;
            pendingRebase = _pendingRebase;
            preferredPositionMicroseconds = _lastPositionMicroseconds;
        }

        if (!processId.HasValue ||
            (segmentLocations.Length < 2 && absoluteLocations.Length < 2))
        {
            return false;
        }

        using SafeProcessHandle process = OpenProcess(
            ProcessVmRead | ProcessQueryLimitedInformation,
            false,
            processId.Value);
        if (process.IsInvalid)
        {
            InvalidateLocations();
            return false;
        }

        List<NeteaseMemoryClockCandidate> segmentSamples =
            CaptureCurrentIntCandidates(
                process,
                segmentLocations,
                maximumPositionMicroseconds);
        IReadOnlyList<NeteaseMemoryClockCandidate> segmentConsensus =
            SelectCachedConsensus(segmentSamples);
        List<NeteaseMemoryClockCandidate> absoluteSamples =
            CaptureCurrentDoubleCandidates(
                process,
                absoluteLocations,
                maximumPositionMicroseconds);
        IReadOnlyList<NeteaseMemoryClockCandidate> absoluteConsensus =
            SelectCachedConsensus(
                absoluteSamples,
                preferredPositionMicroseconds);
        DateTime nowUtc = DateTime.UtcNow;
        bool isPlaying;
        long absolutePositionMicroseconds;

        if (segmentConsensus.Count == 0)
        {
            if (absoluteConsensus.Count == 0)
            {
                MarkLocationsForRebase();
                return false;
            }

            absolutePositionMicroseconds = Median(
                absoluteConsensus.Select(sample => sample.PositionMicroseconds));
            lock (_stateGate)
            {
                isPlaying = UpdateMovementStateLocked(
                    absolutePositionMicroseconds,
                    nowUtc);
                _pendingRebase = _locations.Length >= 2;
            }

            state = (TimeSpan.FromTicks(absolutePositionMicroseconds * 10), isPlaying);
            return true;
        }

        long segmentPositionMicroseconds = Median(
            segmentConsensus.Select(sample => sample.PositionMicroseconds));
        lock (_stateGate)
        {
            if (absoluteConsensus.Count > 0)
            {
                long directAbsolutePositionMicroseconds = Median(
                    absoluteConsensus.Select(sample => sample.PositionMicroseconds));
                long? directBaseOffsetMicroseconds =
                    NeteaseMemoryClockOffsetPolicy.ResolveBaseOffset(
                        segmentPositionMicroseconds,
                        directAbsolutePositionMicroseconds);
                if (directBaseOffsetMicroseconds.HasValue)
                {
                    if (pendingRebase ||
                        Math.Abs(
                            directBaseOffsetMicroseconds.Value -
                            _baseOffsetMicroseconds) >=
                        NeteaseMemorySegmentClockPolicy.SeekResetThresholdMicroseconds)
                    {
                        _baseOffsetMicroseconds = directBaseOffsetMicroseconds.Value;
                    }
                    _pendingRebase = false;
                }
                else if (pendingRebase)
                {
                    return false;
                }
            }
            else
            {
                if (pendingRebase)
                {
                    return false;
                }

                if (NeteaseMemorySegmentClockPolicy.IsReset(
                        _lastSegmentPositionMicroseconds,
                        segmentPositionMicroseconds))
                {
                    _pendingRebase = true;
                    _lastSegmentPositionMicroseconds = segmentPositionMicroseconds;
                    _lastScanAttemptAt = 0;
                    return false;
                }
            }

            absolutePositionMicroseconds = checked(
                _baseOffsetMicroseconds + segmentPositionMicroseconds);
            if (absolutePositionMicroseconds > maximumPositionMicroseconds)
            {
                _processId = null;
                _locations = [];
                _absoluteClockLocations = [];
                _absoluteClockHints = [];
                _lastScanAttemptAt = 0;
                return false;
            }

            _lastSegmentPositionMicroseconds = segmentPositionMicroseconds;
            isPlaying = UpdateMovementStateLocked(
                absolutePositionMicroseconds,
                nowUtc);
        }

        state = (TimeSpan.FromTicks(absolutePositionMicroseconds * 10), isPlaying);
        return true;
    }

    private bool UpdateMovementStateLocked(
        long absolutePositionMicroseconds,
        DateTime nowUtc)
    {
        bool isPlaying = _lastIsPlaying;
        if (_lastPositionMicroseconds.HasValue && _lastSampleUtc != default)
        {
            long delta = absolutePositionMicroseconds - _lastPositionMicroseconds.Value;
            double elapsedMilliseconds = (nowUtc - _lastSampleUtc).TotalMilliseconds;
            if (delta > MovementThresholdMicroseconds)
            {
                isPlaying = true;
            }
            else if (Math.Abs(delta) <= MovementThresholdMicroseconds &&
                     elapsedMilliseconds >= PauseDetectionMilliseconds)
            {
                isPlaying = false;
            }
        }

        _lastPositionMicroseconds = absolutePositionMicroseconds;
        _lastSampleUtc = nowUtc;
        _lastIsPlaying = isPlaying;
        return isPlaying;
    }

    private static IReadOnlyList<NeteaseMemoryClockCandidate> SelectCachedConsensus(
        IReadOnlyList<NeteaseMemoryClockCandidate> samples,
        long? preferredPositionMicroseconds = null)
    {
        if (preferredPositionMicroseconds.HasValue)
        {
            NeteaseMemoryClockCandidate[] continuous = samples
                .Where(sample =>
                    sample.PositionMicroseconds >=
                    preferredPositionMicroseconds.Value - 1_000_000)
                .ToArray();
            IReadOnlyList<NeteaseMemoryClockCandidate> continuousConsensus =
                SelectBestCachedConsensus(continuous);
            return continuousConsensus;
        }

        return SelectBestCachedConsensus(samples);
    }

    private static IReadOnlyList<NeteaseMemoryClockCandidate> SelectBestCachedConsensus(
        IReadOnlyList<NeteaseMemoryClockCandidate> samples)
    {
        NeteaseMemoryClockCandidate[] best = [];
        int bestAllocationCount = 0;
        foreach (NeteaseMemoryClockCandidate center in samples)
        {
            NeteaseMemoryClockCandidate[] group = samples
                .Where(sample =>
                    Math.Abs(sample.PositionMicroseconds - center.PositionMicroseconds) <=
                    CachedConsensusToleranceMicroseconds)
                .ToArray();
            int allocationCount = group
                .Select(sample => sample.AllocationBase)
                .Distinct()
                .Count();
            if (allocationCount > bestAllocationCount ||
                (allocationCount == bestAllocationCount && group.Length > best.Length))
            {
                best = group;
                bestAllocationCount = allocationCount;
            }
        }

        return bestAllocationCount >= 2 ? best : [];
    }

    private void InvalidateLocations()
    {
        lock (_stateGate)
        {
            _processId = null;
            _locations = [];
            _absoluteClockLocations = [];
            _absoluteClockHints = [];
            _baseOffsetMicroseconds = 0;
            _lastSegmentPositionMicroseconds = null;
            _pendingRebase = false;
        }
    }

    private void MarkLocationsForRebase()
    {
        lock (_stateGate)
        {
            if (_locations.Length < 2)
            {
                return;
            }

            _pendingRebase = true;
            _lastScanAttemptAt = 0;
        }
    }

    private MemoryClockDiscovery? DiscoverClock(
        long maximumPositionMicroseconds,
        IReadOnlyList<MemoryClockLocation> knownSegmentLocations,
        IReadOnlyList<MemoryScanHint> knownAbsoluteClockHints,
        long? preferredPositionMicroseconds)
    {
        using Process? process = FindMainNeteaseProcess();
        if (process == null)
        {
            return null;
        }

        string productVersion;
        try
        {
            productVersion = process.MainModule?.FileVersionInfo.ProductVersion ?? "unknown";
        }
        catch
        {
            productVersion = "unknown";
        }

        using SafeProcessHandle processHandle = OpenProcess(
            ProcessVmRead | ProcessQueryLimitedInformation,
            false,
            process.Id);
        if (processHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (knownSegmentLocations.Count >= 2 && knownAbsoluteClockHints.Count > 0)
        {
            FastAbsoluteClockDiscovery? fastAbsoluteClock =
                TryDiscoverAbsoluteClockFromHints(
                    processHandle,
                    knownAbsoluteClockHints,
                    maximumPositionMicroseconds,
                    preferredPositionMicroseconds);
            if (fastAbsoluteClock != null)
            {
                List<NeteaseMemoryClockCandidate> knownSegmentCandidates =
                    CaptureCurrentIntCandidates(
                        processHandle,
                        knownSegmentLocations,
                        maximumPositionMicroseconds);
                IReadOnlyList<NeteaseMemoryClockCandidate> knownSegmentConsensus =
                    SelectCachedConsensus(knownSegmentCandidates);
                if (knownSegmentConsensus.Count >= 2)
                {
                    long fastSegmentPositionMicroseconds = Median(
                        knownSegmentConsensus.Select(candidate => candidate.PositionMicroseconds));
                    long? baseOffsetMicroseconds = NeteaseMemoryClockOffsetPolicy.ResolveBaseOffset(
                        fastSegmentPositionMicroseconds,
                        fastAbsoluteClock.PositionMicroseconds);
                    if (baseOffsetMicroseconds.HasValue)
                    {
                        _diagnostic.Info(
                            $"Netease read-only memory fast rebase: " +
                            $"hints={knownAbsoluteClockHints.Count}, " +
                            $"base={baseOffsetMicroseconds.Value / 1_000_000.0:F3}s, " +
                            $"position={fastAbsoluteClock.PositionMicroseconds / 1_000_000.0:F3}s");
                        return new MemoryClockDiscovery(
                            process.Id,
                            productVersion,
                            knownSegmentLocations.ToArray(),
                            checked(baseOffsetMicroseconds.Value + fastSegmentPositionMicroseconds),
                            baseOffsetMicroseconds.Value,
                            fastSegmentPositionMicroseconds,
                            fastAbsoluteClock.Locations,
                            fastAbsoluteClock.Hints);
                    }
                }
            }
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        List<MemorySeedChunk> chunks = CaptureSeeds(
            processHandle,
            stopwatch,
            maximumPositionMicroseconds);
        if (chunks.Count == 0)
        {
            _diagnostic.Info("Netease read-only memory clock scan: no seed chunks.");
            return null;
        }

        Thread.Sleep(350);
        List<MemoryPassCandidate> passCandidates = CaptureAdvancingCandidates(
            processHandle,
            stopwatch,
            chunks,
            maximumPositionMicroseconds,
            out List<MemoryDoublePassCandidate> doublePassCandidates);
        if (passCandidates.Count == 0 && doublePassCandidates.Count == 0)
        {
            _diagnostic.Info($"Netease read-only memory clock scan: no advancing candidates (seedChunks={chunks.Count}).");
            return null;
        }

        Thread.Sleep(450);
        List<NeteaseMemoryClockCandidate> finalCandidates = CaptureFinalCandidates(
            processHandle,
            stopwatch,
            passCandidates,
            maximumPositionMicroseconds);
        List<NeteaseMemoryClockCandidate> finalDoubleCandidates = CaptureFinalDoubleCandidates(
            processHandle,
            stopwatch,
            doublePassCandidates,
            maximumPositionMicroseconds);
        if (_diagnostic.Enabled)
        {
            _diagnostic.Info(
                $"Netease read-only memory clock scan: int64Groups={DescribeCandidateGroups(finalCandidates)}, " +
                $"doubleGroups={DescribeCandidateGroups(finalDoubleCandidates)}");
        }
        IReadOnlyList<NeteaseMemoryClockCandidate> finalDoubleConsensus =
            NeteaseMemoryClockSelectionPolicy.SelectConsensus(
                finalDoubleCandidates,
                preferredPositionMicroseconds);
        if (finalDoubleConsensus.Count < 2)
        {
            _diagnostic.Info(
                $"Netease read-only memory clock scan: no absolute double consensus " +
                $"(int64={finalCandidates.Count}, double={finalDoubleCandidates.Count}).");
            return null;
        }

        long absolutePositionMicroseconds = Median(
            finalDoubleConsensus.Select(candidate => candidate.PositionMicroseconds));
        MemoryClockLocation[] absoluteClockLocations = finalDoubleConsensus
            .Select(candidate => new MemoryClockLocation(
                candidate.Address,
                candidate.AllocationBase))
            .ToArray();
        MemoryScanHint[] absoluteClockHints = SelectScanHints(
            finalDoubleConsensus,
            doublePassCandidates);

        if (knownSegmentLocations.Count >= 2)
        {
            List<NeteaseMemoryClockCandidate> knownSegmentCandidates =
                CaptureCurrentIntCandidates(
                    processHandle,
                    knownSegmentLocations,
                    maximumPositionMicroseconds);
            IReadOnlyList<NeteaseMemoryClockCandidate> knownSegmentConsensus =
                SelectCachedConsensus(knownSegmentCandidates);
            if (knownSegmentConsensus.Count < 2)
            {
                _diagnostic.Info(
                    $"Netease read-only memory rebase: known segment clock unavailable " +
                    $"(locations={knownSegmentLocations.Count}, current={knownSegmentCandidates.Count}).");
                return null;
            }

            long rebasedSegmentPositionMicroseconds = Median(
                knownSegmentConsensus.Select(candidate => candidate.PositionMicroseconds));
            long? baseOffsetMicroseconds = NeteaseMemoryClockOffsetPolicy.ResolveBaseOffset(
                rebasedSegmentPositionMicroseconds,
                absolutePositionMicroseconds);
            if (!baseOffsetMicroseconds.HasValue)
            {
                _diagnostic.Info(
                    $"Netease read-only memory rebase rejected: " +
                    $"segment={rebasedSegmentPositionMicroseconds / 1_000_000.0:F3}s, " +
                    $"absolute={absolutePositionMicroseconds / 1_000_000.0:F3}s");
                return null;
            }

            return new MemoryClockDiscovery(
                process.Id,
                productVersion,
                knownSegmentLocations.ToArray(),
                checked(baseOffsetMicroseconds.Value + rebasedSegmentPositionMicroseconds),
                baseOffsetMicroseconds.Value,
                rebasedSegmentPositionMicroseconds,
                absoluteClockLocations,
                absoluteClockHints);
        }

        IReadOnlyList<NeteaseMemoryClockCandidate> correlatedSegmentCandidates =
            NeteaseMemoryClockPairingPolicy.SelectSegmentCorrelatedWithDouble(
                finalCandidates,
                finalDoubleCandidates);
        if (correlatedSegmentCandidates.Count < 2)
        {
            return new MemoryClockDiscovery(
                process.Id,
                productVersion,
                [],
                absolutePositionMicroseconds,
                0,
                null,
                absoluteClockLocations,
                absoluteClockHints);
        }

        long initialSegmentPositionMicroseconds = Median(
            correlatedSegmentCandidates.Select(candidate => candidate.PositionMicroseconds));
        long? initialBaseOffsetMicroseconds = NeteaseMemoryClockOffsetPolicy.ResolveBaseOffset(
            initialSegmentPositionMicroseconds,
            absolutePositionMicroseconds);
        if (!initialBaseOffsetMicroseconds.HasValue)
        {
            return null;
        }

        long sustainedValidationStartedAt = stopwatch.ElapsedTicks;
        Thread.Sleep(650);
        List<NeteaseMemoryClockCandidate> sustainedSegmentCandidates = CaptureSustainedCandidates(
            processHandle,
            stopwatch,
            correlatedSegmentCandidates,
            sustainedValidationStartedAt,
            maximumPositionMicroseconds);
        if (_diagnostic.Enabled)
        {
            _diagnostic.Info(
                $"Netease read-only memory sustained segment groups=" +
                DescribeCandidateGroups(sustainedSegmentCandidates));
        }
        IReadOnlyList<NeteaseMemoryClockCandidate> sustainedSegmentConsensus =
            NeteaseMemoryClockSelectionPolicy.SelectConsensus(sustainedSegmentCandidates);
        if (sustainedSegmentConsensus.Count < 2)
        {
            long currentAbsolutePositionMicroseconds = checked(
                absolutePositionMicroseconds + StopwatchTicksToMicroseconds(
                    stopwatch.ElapsedTicks - sustainedValidationStartedAt));
            return new MemoryClockDiscovery(
                process.Id,
                productVersion,
                [],
                currentAbsolutePositionMicroseconds,
                0,
                null,
                absoluteClockLocations,
                absoluteClockHints);
        }

        MemoryClockLocation[] locations = sustainedSegmentConsensus
            .Select(candidate => new MemoryClockLocation(candidate.Address, candidate.AllocationBase))
            .ToArray();
        long segmentPositionMicroseconds = Median(
            sustainedSegmentConsensus.Select(candidate => candidate.PositionMicroseconds));
        long positionMicroseconds = checked(
            initialBaseOffsetMicroseconds.Value + segmentPositionMicroseconds);
        if (_diagnostic.Enabled)
        {
            _diagnostic.Info(
                "Netease read-only memory clock scan: selected=" +
                string.Join(
                    " | ",
                    sustainedSegmentConsensus.Select(candidate =>
                        $"0x{candidate.Address:X}@0x{candidate.AllocationBase:X} " +
                        DescribeNearbyClockValues(
                            processHandle,
                            candidate.Address,
                            maximumPositionMicroseconds))));
        }
        return new MemoryClockDiscovery(
            process.Id,
            productVersion,
            locations,
            positionMicroseconds,
            initialBaseOffsetMicroseconds.Value,
            segmentPositionMicroseconds,
            absoluteClockLocations,
            absoluteClockHints);
    }

    private static FastAbsoluteClockDiscovery? TryDiscoverAbsoluteClockFromHints(
        SafeProcessHandle process,
        IReadOnlyList<MemoryScanHint> hints,
        long maximumPositionMicroseconds,
        long? preferredPositionMicroseconds)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<MemorySeedChunk> chunks = CaptureHintSeeds(
            process,
            stopwatch,
            hints,
            maximumPositionMicroseconds);
        if (chunks.Count == 0)
        {
            return null;
        }

        Thread.Sleep(FastRebasePassDelayMilliseconds);
        CaptureAdvancingCandidates(
            process,
            stopwatch,
            chunks,
            maximumPositionMicroseconds,
            out List<MemoryDoublePassCandidate> doublePassCandidates);
        if (doublePassCandidates.Count == 0)
        {
            return null;
        }

        Thread.Sleep(FastRebaseFinalDelayMilliseconds);
        List<NeteaseMemoryClockCandidate> finalDoubleCandidates =
            CaptureFinalDoubleCandidates(
                process,
                stopwatch,
                doublePassCandidates,
                maximumPositionMicroseconds);
        IReadOnlyList<NeteaseMemoryClockCandidate> consensus =
            NeteaseMemoryClockSelectionPolicy.SelectConsensus(
                finalDoubleCandidates,
                preferredPositionMicroseconds);
        if (consensus.Count < 2)
        {
            return null;
        }

        return new FastAbsoluteClockDiscovery(
            Median(consensus.Select(candidate => candidate.PositionMicroseconds)),
            consensus
                .Select(candidate => new MemoryClockLocation(
                    candidate.Address,
                    candidate.AllocationBase))
                .ToArray(),
            SelectScanHints(consensus, doublePassCandidates));
    }

    private static List<MemorySeedChunk> CaptureHintSeeds(
        SafeProcessHandle process,
        Stopwatch stopwatch,
        IReadOnlyList<MemoryScanHint> hints,
        long maximumPositionMicroseconds)
    {
        List<MemorySeedChunk> chunks = [];
        int doubleSeedCount = 0;
        foreach (MemoryScanHint hint in ExpandScanHints(process, hints)
                     .DistinctBy(hint => (hint.Address, hint.Length)))
        {
            byte[] buffer = new byte[hint.Length];
            if (!TryReadBuffer(process, hint.Address, buffer))
            {
                continue;
            }

            long capturedAt = stopwatch.ElapsedTicks;
            List<MemoryDoubleSeed> doubleSeeds = [];
            for (int index = 0; index <= buffer.Length - sizeof(long); index += sizeof(long))
            {
                if (doubleSeedCount >= MaximumDoubleSeedCount)
                {
                    break;
                }

                if (TryNormalizeDoubleSeconds(
                        BitConverter.ToDouble(buffer, index),
                        maximumPositionMicroseconds,
                        out long positionMicroseconds))
                {
                    doubleSeeds.Add(new MemoryDoubleSeed(index, positionMicroseconds));
                    doubleSeedCount++;
                }
            }

            if (doubleSeeds.Count > 0)
            {
                chunks.Add(new MemorySeedChunk(
                    hint.Address,
                    hint.AllocationBase,
                    hint.Length,
                    capturedAt,
                    [],
                    doubleSeeds));
            }
        }

        return chunks;
    }

    private static IEnumerable<MemoryScanHint> ExpandScanHints(
        SafeProcessHandle process,
        IReadOnlyList<MemoryScanHint> hints)
    {
        HashSet<(long Address, int Length)> yielded = [];
        foreach (MemoryScanHint hint in hints)
        {
            for (int direction = -1; direction <= 1; direction++)
            {
                long address = hint.Address + (long)direction * hint.Length;
                if (address < 0 ||
                    !TryGetReadablePrivateRegion(process, address, out MemoryRegion region) ||
                    region.AllocationBase != hint.AllocationBase ||
                    address < region.Address ||
                    address + hint.Length > region.Address + region.Size)
                {
                    continue;
                }

                if (yielded.Add((address, hint.Length)))
                {
                    yield return new MemoryScanHint(
                        address,
                        hint.AllocationBase,
                        hint.Length);
                }
            }
        }

        foreach (long allocationBase in hints
                     .Select(hint => hint.AllocationBase)
                     .Distinct())
        {
            long scannedBytes = 0;
            foreach (MemoryRegion region in EnumerateReadablePrivateRegions(process)
                         .Where(region => region.AllocationBase == allocationBase))
            {
                for (long offset = 0; offset < region.Size; offset += ScanChunkBytes)
                {
                    int length = checked((int)Math.Min(
                        ScanChunkBytes,
                        region.Size - offset));
                    if (scannedBytes + length > MaximumFastRebaseBytesPerAllocation)
                    {
                        break;
                    }

                    scannedBytes += length;
                    long address = region.Address + offset;
                    if (yielded.Add((address, length)))
                    {
                        yield return new MemoryScanHint(
                            address,
                            allocationBase,
                            length);
                    }
                }

                if (scannedBytes >= MaximumFastRebaseBytesPerAllocation)
                {
                    break;
                }
            }
        }
    }

    private static MemoryScanHint[] SelectScanHints(
        IReadOnlyList<NeteaseMemoryClockCandidate> consensus,
        IReadOnlyList<MemoryDoublePassCandidate> passCandidates)
    {
        HashSet<long> selectedAddresses = consensus
            .Select(candidate => candidate.Address)
            .ToHashSet();
        return passCandidates
            .Where(candidate =>
                selectedAddresses.Contains(candidate.ChunkAddress + candidate.Offset))
            .Select(candidate => new MemoryScanHint(
                candidate.ChunkAddress,
                candidate.AllocationBase,
                candidate.ChunkLength))
            .Distinct()
            .ToArray();
    }

    private static Process? FindMainNeteaseProcess()
    {
        Process[] processes = Process.GetProcessesByName("cloudmusic");
        if (processes.Length == 0)
        {
            return null;
        }

        Process? selected = null;
        DateTime selectedStart = DateTime.MaxValue;
        foreach (Process process in processes)
        {
            try
            {
                DateTime startTime = process.StartTime;
                if (startTime < selectedStart)
                {
                    selected?.Dispose();
                    selected = process;
                    selectedStart = startTime;
                }
                else
                {
                    process.Dispose();
                }
            }
            catch
            {
                process.Dispose();
            }
        }

        return selected;
    }

    private static List<MemorySeedChunk> CaptureSeeds(
        SafeProcessHandle process,
        Stopwatch stopwatch,
        long maximumPositionMicroseconds)
    {
        List<MemorySeedChunk> chunks = [];
        long scannedBytes = 0;
        int seedCount = 0;
        int doubleSeedCount = 0;

        foreach (MemoryRegion region in EnumerateReadablePrivateRegions(process))
        {
            for (long offset = 0; offset < region.Size; offset += ScanChunkBytes)
            {
                int length = checked((int)Math.Min(ScanChunkBytes, region.Size - offset));
                scannedBytes += length;
                if (scannedBytes > MaximumScanBytes)
                {
                    return chunks;
                }

                long address = region.Address + offset;
                byte[] buffer = new byte[length];
                if (!TryReadBuffer(process, address, buffer))
                {
                    continue;
                }

                long capturedAt = stopwatch.ElapsedTicks;
                List<MemorySeed> seeds = [];
                List<MemoryDoubleSeed> doubleSeeds = [];
                for (int index = 0; index <= buffer.Length - sizeof(long); index += sizeof(long))
                {
                    long value = BitConverter.ToInt64(buffer, index);
                    if (value >= MinimumPositionMicroseconds && value <= maximumPositionMicroseconds)
                    {
                        seeds.Add(new MemorySeed(index, value));
                        seedCount++;
                        if (seedCount > MaximumSeedCount)
                        {
                            return [];
                        }
                    }

                    if (doubleSeedCount < MaximumDoubleSeedCount &&
                        TryNormalizeDoubleSeconds(
                            BitConverter.ToDouble(buffer, index),
                            maximumPositionMicroseconds,
                            out long doublePositionMicroseconds))
                    {
                        doubleSeeds.Add(new MemoryDoubleSeed(index, doublePositionMicroseconds));
                        doubleSeedCount++;
                    }
                }

                if (seeds.Count > 0 || doubleSeeds.Count > 0)
                {
                    chunks.Add(new MemorySeedChunk(
                        address,
                        region.AllocationBase,
                        length,
                        capturedAt,
                        seeds,
                        doubleSeeds));
                }
            }
        }

        return chunks;
    }

    private static List<MemoryPassCandidate> CaptureAdvancingCandidates(
        SafeProcessHandle process,
        Stopwatch stopwatch,
        IReadOnlyList<MemorySeedChunk> chunks,
        long maximumPositionMicroseconds,
        out List<MemoryDoublePassCandidate> doublePassCandidates)
    {
        List<MemoryPassCandidate> candidates = [];
        doublePassCandidates = [];
        foreach (MemorySeedChunk chunk in chunks)
        {
            byte[] buffer = new byte[chunk.Length];
            if (!TryReadBuffer(process, chunk.Address, buffer))
            {
                continue;
            }

            long capturedAt = stopwatch.ElapsedTicks;
            long expectedDelta = StopwatchTicksToMicroseconds(capturedAt - chunk.CapturedAt);
            foreach (MemorySeed seed in chunk.Seeds)
            {
                long value = BitConverter.ToInt64(buffer, seed.Offset);
                long delta = value - seed.Value;
                if (value < 0 ||
                    value > maximumPositionMicroseconds ||
                    !IsAdvancingAtClockRate(delta, expectedDelta, 450_000, 550_000))
                {
                    continue;
                }

                candidates.Add(new MemoryPassCandidate(
                    chunk.Address,
                    chunk.AllocationBase,
                    chunk.Length,
                    seed.Offset,
                    value,
                    capturedAt));
                if (candidates.Count > MaximumPassCandidateCount)
                {
                    return [];
                }
            }

            foreach (MemoryDoubleSeed seed in chunk.DoubleSeeds)
            {
                if (!TryNormalizeDoubleSeconds(
                        BitConverter.ToDouble(buffer, seed.Offset),
                        maximumPositionMicroseconds,
                        out long value))
                {
                    continue;
                }

                long delta = value - seed.PositionMicroseconds;
                if (IsAdvancingAtClockRate(delta, expectedDelta, 450_000, 550_000))
                {
                    doublePassCandidates.Add(new MemoryDoublePassCandidate(
                        chunk.Address,
                        chunk.AllocationBase,
                        chunk.Length,
                        seed.Offset,
                        value,
                        capturedAt));
                }
            }
        }

        return candidates;
    }

    private static List<NeteaseMemoryClockCandidate> CaptureFinalCandidates(
        SafeProcessHandle process,
        Stopwatch stopwatch,
        IReadOnlyList<MemoryPassCandidate> passCandidates,
        long maximumPositionMicroseconds)
    {
        List<RawFinalCandidate> rawCandidates = [];
        foreach (IGrouping<long, MemoryPassCandidate> chunkGroup in
                 passCandidates.GroupBy(candidate => candidate.ChunkAddress))
        {
            MemoryPassCandidate first = chunkGroup.First();
            byte[] buffer = new byte[first.ChunkLength];
            if (!TryReadBuffer(process, first.ChunkAddress, buffer))
            {
                continue;
            }

            long capturedAt = stopwatch.ElapsedTicks;
            foreach (MemoryPassCandidate candidate in chunkGroup)
            {
                long value = BitConverter.ToInt64(buffer, candidate.Offset);
                long delta = value - candidate.Value;
                long expectedDelta = StopwatchTicksToMicroseconds(capturedAt - candidate.CapturedAt);
                if (value < 0 ||
                    value > maximumPositionMicroseconds ||
                    !IsAdvancingAtClockRate(delta, expectedDelta, 350_000, 450_000))
                {
                    continue;
                }

                rawCandidates.Add(new RawFinalCandidate(
                    candidate.ChunkAddress + candidate.Offset,
                    candidate.AllocationBase,
                    value,
                    capturedAt,
                    Math.Abs(delta - expectedDelta)));
            }
        }

        long referenceTimestamp = stopwatch.ElapsedTicks;
        return rawCandidates
            .Select(candidate => new NeteaseMemoryClockCandidate(
                candidate.Address,
                candidate.AllocationBase,
                candidate.Value + StopwatchTicksToMicroseconds(referenceTimestamp - candidate.CapturedAt),
                candidate.DriftMicroseconds))
            .ToList();
    }

    private static List<NeteaseMemoryClockCandidate> CaptureFinalDoubleCandidates(
        SafeProcessHandle process,
        Stopwatch stopwatch,
        IReadOnlyList<MemoryDoublePassCandidate> passCandidates,
        long maximumPositionMicroseconds)
    {
        List<RawFinalCandidate> rawCandidates = [];
        foreach (IGrouping<long, MemoryDoublePassCandidate> chunkGroup in
                 passCandidates.GroupBy(candidate => candidate.ChunkAddress))
        {
            MemoryDoublePassCandidate first = chunkGroup.First();
            byte[] buffer = new byte[first.ChunkLength];
            if (!TryReadBuffer(process, first.ChunkAddress, buffer))
            {
                continue;
            }

            long capturedAt = stopwatch.ElapsedTicks;
            foreach (MemoryDoublePassCandidate candidate in chunkGroup)
            {
                if (!TryNormalizeDoubleSeconds(
                        BitConverter.ToDouble(buffer, candidate.Offset),
                        maximumPositionMicroseconds,
                        out long value))
                {
                    continue;
                }

                long delta = value - candidate.Value;
                long expectedDelta = StopwatchTicksToMicroseconds(
                    capturedAt - candidate.CapturedAt);
                if (!IsAdvancingAtClockRate(delta, expectedDelta, 350_000, 450_000))
                {
                    continue;
                }

                rawCandidates.Add(new RawFinalCandidate(
                    candidate.ChunkAddress + candidate.Offset,
                    candidate.AllocationBase,
                    value,
                    capturedAt,
                    Math.Abs(delta - expectedDelta)));
            }
        }

        long referenceTimestamp = stopwatch.ElapsedTicks;
        return rawCandidates
            .Select(candidate => new NeteaseMemoryClockCandidate(
                candidate.Address,
                candidate.AllocationBase,
                candidate.Value + StopwatchTicksToMicroseconds(
                    referenceTimestamp - candidate.CapturedAt),
                candidate.DriftMicroseconds))
            .ToList();
    }

    private static List<NeteaseMemoryClockCandidate> CaptureSustainedCandidates(
        SafeProcessHandle process,
        Stopwatch stopwatch,
        IReadOnlyList<NeteaseMemoryClockCandidate> candidates,
        long validationStartedAt,
        long maximumPositionMicroseconds)
    {
        long capturedAt = stopwatch.ElapsedTicks;
        long expectedDelta = StopwatchTicksToMicroseconds(capturedAt - validationStartedAt);
        List<NeteaseMemoryClockCandidate> sustained = [];

        foreach (NeteaseMemoryClockCandidate candidate in candidates)
        {
            if (!TryReadInt64(process, candidate.Address, out long value))
            {
                continue;
            }

            long delta = value - candidate.PositionMicroseconds;
            if (value < 0 ||
                value > maximumPositionMicroseconds ||
                !IsAdvancingAtClockRate(delta, expectedDelta, 350_000, 450_000))
            {
                continue;
            }

            sustained.Add(new NeteaseMemoryClockCandidate(
                candidate.Address,
                candidate.AllocationBase,
                value,
                candidate.DriftMicroseconds + Math.Abs(delta - expectedDelta)));
        }

        return sustained;
    }

    private static List<NeteaseMemoryClockCandidate> CaptureCurrentIntCandidates(
        SafeProcessHandle process,
        IReadOnlyList<MemoryClockLocation> locations,
        long maximumPositionMicroseconds)
    {
        List<NeteaseMemoryClockCandidate> current = [];
        foreach (MemoryClockLocation location in locations)
        {
            if (!TryReadInt64(process, location.Address, out long value) ||
                value < 0 ||
                value > maximumPositionMicroseconds)
            {
                continue;
            }

            current.Add(new NeteaseMemoryClockCandidate(
                location.Address,
                location.AllocationBase,
                value,
                0));
        }

        return current;
    }

    private static List<NeteaseMemoryClockCandidate> CaptureCurrentDoubleCandidates(
        SafeProcessHandle process,
        IReadOnlyList<MemoryClockLocation> locations,
        long maximumPositionMicroseconds)
    {
        List<NeteaseMemoryClockCandidate> current = [];
        foreach (MemoryClockLocation location in locations)
        {
            if (!TryReadDoubleSeconds(process, location.Address, out long value) ||
                value < 0 ||
                value > maximumPositionMicroseconds)
            {
                continue;
            }

            current.Add(new NeteaseMemoryClockCandidate(
                location.Address,
                location.AllocationBase,
                value,
                0));
        }

        return current;
    }

    private static IEnumerable<MemoryRegion> EnumerateReadablePrivateRegions(
        SafeProcessHandle process)
    {
        long address = 0;
        long maximumAddress = nint.MaxValue;
        nuint infoSize = (nuint)Marshal.SizeOf<MemoryBasicInformation>();

        while (address >= 0 && address < maximumAddress)
        {
            nuint result = VirtualQueryEx(
                process,
                (nint)address,
                out MemoryBasicInformation info,
                infoSize);
            if (result == 0)
            {
                yield break;
            }

            long regionAddress = info.BaseAddress;
            long regionSize = checked((long)info.RegionSize);
            if (regionSize <= 0)
            {
                yield break;
            }

            if (info.State == MemCommit &&
                info.Type == MemPrivate &&
                IsWritable(info.Protect) &&
                (info.Protect & (PageGuard | PageNoAccess)) == 0)
            {
                yield return new MemoryRegion(regionAddress, info.AllocationBase, regionSize);
            }

            long next = regionAddress + regionSize;
            if (next <= address)
            {
                yield break;
            }

            address = next;
        }
    }

    private static bool TryGetReadablePrivateRegion(
        SafeProcessHandle process,
        long address,
        out MemoryRegion region)
    {
        region = default;
        nuint result = VirtualQueryEx(
            process,
            (nint)address,
            out MemoryBasicInformation info,
            (nuint)Marshal.SizeOf<MemoryBasicInformation>());
        if (result == 0 ||
            info.State != MemCommit ||
            info.Type != MemPrivate ||
            !IsWritable(info.Protect) ||
            (info.Protect & (PageGuard | PageNoAccess)) != 0)
        {
            return false;
        }

        long regionSize = checked((long)info.RegionSize);
        if (regionSize <= 0)
        {
            return false;
        }

        region = new MemoryRegion(info.BaseAddress, info.AllocationBase, regionSize);
        return true;
    }

    private static bool IsWritable(uint protection)
    {
        uint baseProtection = protection & 0xFF;
        return baseProtection is PageReadWrite or PageWriteCopy or
            PageExecuteReadWrite or PageExecuteWriteCopy;
    }

    private static bool IsAdvancingAtClockRate(
        long delta,
        long expectedDelta,
        long lowerTolerance,
        long upperTolerance)
    {
        return delta >= Math.Max(100_000, expectedDelta - lowerTolerance) &&
               delta <= expectedDelta + upperTolerance;
    }

    private static bool TryNormalizeDoubleSeconds(
        double seconds,
        long maximumPositionMicroseconds,
        out long positionMicroseconds)
    {
        positionMicroseconds = 0;
        if (!double.IsFinite(seconds) || seconds < 0.25)
        {
            return false;
        }

        double milliseconds = seconds * 1_000;
        if (Math.Abs(milliseconds - Math.Round(milliseconds)) > 0.001)
        {
            return false;
        }

        double microseconds = seconds * 1_000_000;
        if (microseconds > maximumPositionMicroseconds)
        {
            return false;
        }

        positionMicroseconds = checked((long)Math.Round(microseconds));
        return positionMicroseconds <= maximumPositionMicroseconds;
    }

    private static long StopwatchTicksToMicroseconds(long ticks)
    {
        return checked((long)Math.Round(ticks * 1_000_000.0 / Stopwatch.Frequency));
    }

    private static long Median(IEnumerable<long> values)
    {
        long[] ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            return 0;
        }

        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2;
    }

    private static string DescribeCandidateGroups(
        IReadOnlyList<NeteaseMemoryClockCandidate> candidates)
    {
        return string.Join(
            ";",
            candidates
                .GroupBy(candidate => candidate.PositionMicroseconds / 100_000)
                .Select(group => new
                {
                    Position = Median(group.Select(candidate => candidate.PositionMicroseconds)),
                    AllocationCount = group.Select(candidate => candidate.AllocationBase).Distinct().Count(),
                    CandidateCount = group.Count()
                })
                .Where(group => group.AllocationCount >= 2)
                .OrderBy(group => group.Position)
                .Select(group =>
                    $"{group.Position / 1_000_000.0:F3}s/{group.AllocationCount}a/{group.CandidateCount}c"));
    }

    private static string DescribeNearbyClockValues(
        SafeProcessHandle process,
        long address,
        long maximumPositionMicroseconds)
    {
        const int radiusBytes = 64;
        byte[] buffer = new byte[radiusBytes * 2 + sizeof(long)];
        if (!TryReadBuffer(process, address - radiusBytes, buffer))
        {
            return "nearby=<unreadable>";
        }

        List<string> values = [];
        for (int offset = 0; offset <= buffer.Length - sizeof(long); offset += sizeof(long))
        {
            long value = BitConverter.ToInt64(buffer, offset);
            if (value < 0 || value > maximumPositionMicroseconds)
            {
                continue;
            }

            int relativeOffset = offset - radiusBytes;
            values.Add($"{relativeOffset:+#;-#;0}:{value / 1_000_000.0:F3}");
        }

        return values.Count == 0 ? "nearby=<none>" : $"nearby=[{string.Join(",", values)}]";
    }

    private static bool TryReadInt64(
        SafeProcessHandle process,
        long address,
        out long value)
    {
        byte[] buffer = new byte[sizeof(long)];
        if (!TryReadBuffer(process, address, buffer))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToInt64(buffer);
        return true;
    }

    private static bool TryReadDoubleSeconds(
        SafeProcessHandle process,
        long address,
        out long valueMicroseconds)
    {
        byte[] buffer = new byte[sizeof(double)];
        if (!TryReadBuffer(process, address, buffer))
        {
            valueMicroseconds = 0;
            return false;
        }

        return NeteaseMemoryClockValuePolicy.TryConvertSeconds(
            BitConverter.ToDouble(buffer),
            NeteaseMemoryClockValuePolicy.MaximumPositionMicroseconds,
            out valueMicroseconds);
    }

    private static bool TryReadBuffer(
        SafeProcessHandle process,
        long address,
        byte[] buffer)
    {
        return ReadProcessMemory(
                   process,
                   (nint)address,
                   buffer,
                   (nuint)buffer.Length,
                   out nuint bytesRead) &&
               bytesRead == (nuint)buffer.Length;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        SafeProcessHandle process,
        nint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQueryEx(
        SafeProcessHandle process,
        nint address,
        out MemoryBasicInformation buffer,
        nuint length);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    private readonly record struct MemoryRegion(
        long Address,
        long AllocationBase,
        long Size);

    private readonly record struct MemorySeed(int Offset, long Value);

    private readonly record struct MemoryDoubleSeed(int Offset, long PositionMicroseconds);

    private sealed record MemorySeedChunk(
        long Address,
        long AllocationBase,
        int Length,
        long CapturedAt,
        IReadOnlyList<MemorySeed> Seeds,
        IReadOnlyList<MemoryDoubleSeed> DoubleSeeds);

    private readonly record struct MemoryPassCandidate(
        long ChunkAddress,
        long AllocationBase,
        int ChunkLength,
        int Offset,
        long Value,
        long CapturedAt);

    private readonly record struct MemoryDoublePassCandidate(
        long ChunkAddress,
        long AllocationBase,
        int ChunkLength,
        int Offset,
        long Value,
        long CapturedAt);

    private readonly record struct RawFinalCandidate(
        long Address,
        long AllocationBase,
        long Value,
        long CapturedAt,
        long DriftMicroseconds);

    private readonly record struct MemoryClockLocation(
        long Address,
        long AllocationBase);

    private readonly record struct MemoryScanHint(
        long Address,
        long AllocationBase,
        int Length);

    private sealed record FastAbsoluteClockDiscovery(
        long PositionMicroseconds,
        MemoryClockLocation[] Locations,
        MemoryScanHint[] Hints);

    private sealed record MemoryClockDiscovery(
        int ProcessId,
        string ProductVersion,
        MemoryClockLocation[] Locations,
        long PositionMicroseconds,
        long BaseOffsetMicroseconds,
        long? SegmentPositionMicroseconds,
        MemoryClockLocation[] AbsoluteClockLocations,
        MemoryScanHint[] AbsoluteClockHints);
}
