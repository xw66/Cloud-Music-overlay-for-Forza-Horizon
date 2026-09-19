using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

[SupportedOSPlatform("windows")]
public sealed class NeteaseMemoryPlaybackProbe : ITrackAwarePlaybackStateProvider
{
    private const long DefaultMaximumPositionMicroseconds = 20L * 60 * 1_000_000;
    private const long ScanCooldownMilliseconds = 4_000;
    private const long MovementThresholdMicroseconds = 20_000;
    private const int PauseDetectionMilliseconds = 350;

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
    private Task<(TimeSpan Position, bool IsPlaying)?>? _discoveryTask;

    public NeteaseMemoryPlaybackProbe(DiagnosticService diagnostic)
    {
        _diagnostic = diagnostic;
    }

    public void SetTrackContext(TrackInfo? track)
    {
        string trackKey = track == null
            ? string.Empty
            : $"meta:{TrackIdentity.BuildNeteaseTrackKey(track)}";
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

    public Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync()
    {
        if (!Environment.Is64BitProcess)
        {
            return Task.FromResult<(TimeSpan Position, bool IsPlaying)?>(null);
        }

        if (TryReadCachedStateSafely(out (TimeSpan Position, bool IsPlaying) cachedState))
        {
            return Task.FromResult<(TimeSpan Position, bool IsPlaying)?>(cachedState);
        }

        lock (_stateGate)
        {
            if (_discoveryTask is { IsCompletedSuccessfully: true })
            {
                (TimeSpan Position, bool IsPlaying)? completedState =
                    _discoveryTask.GetAwaiter().GetResult();
                _discoveryTask = null;
                if (completedState.HasValue)
                {
                    return Task.FromResult(completedState);
                }
            }

            if (_discoveryTask is null || _discoveryTask.IsCompleted)
            {
                _discoveryTask = Task.Run(DiscoverPlaybackStateAsync);
            }
        }

        return Task.FromResult<(TimeSpan Position, bool IsPlaying)?>(null);
    }

    private async Task<(TimeSpan Position, bool IsPlaying)?> DiscoverPlaybackStateAsync()
    {
        await _scanGate.WaitAsync();
        try
        {
            if (TryReadCachedStateSafely(
                    out (TimeSpan Position, bool IsPlaying) cachedState))
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

            MemoryClockScanResult scanResult = await Task.Run(() =>
            {
                using Process? process = FindMainNeteaseProcess();
                if (process == null)
                {
                    return new MemoryClockScanResult(null, null);
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

                using SafeProcessHandle processHandle = NeteaseMemoryNativeReader.OpenProcess(
                    NeteaseMemoryNativeReader.ProcessVmRead | NeteaseMemoryNativeReader.ProcessQueryLimitedInformation,
                    false,
                    process.Id);
                if (processHandle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                MemoryClockDiscovery? result = NeteaseMemoryClockScanner.DiscoverClock(
                    _diagnostic,
                    process,
                    processHandle,
                    productVersion,
                    maximumPositionMicroseconds,
                    knownSegmentLocations,
                    knownAbsoluteClockHints,
                    preferredPositionMicroseconds,
                    out bool? isPlayingObservation);
                return new MemoryClockScanResult(result, isPlayingObservation);
            });

            MemoryClockDiscovery? discovery = scanResult.Discovery;
            if (discovery == null)
            {
                return scanResult.IsPlayingObservation is { } isPlayingObservation
                    ? CreateActivityState(generation, isPlayingObservation)
                    : null;
            }

            long absolutePositionMicroseconds = discovery.PositionMicroseconds;
            bool isPlaying;
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
                isPlaying = UpdateMovementStateLocked(
                    absolutePositionMicroseconds,
                    DateTime.UtcNow);
                _pendingRebase = false;
            }

            _diagnostic.Info(
                $"Netease read-only memory clock discovered: version={discovery.ProductVersion}, " +
                $"kind={(discovery.Locations.Length >= 2 ? "paired-segment" : "absolute-double-anchor")}, " +
                $"locations={discovery.Locations.Length}, " +
                $"base={discovery.BaseOffsetMicroseconds / 1_000_000.0:F3}s, " +
                $"position={absolutePositionMicroseconds / 1_000_000.0:F3}s, " +
                $"isPlaying={isPlaying}");
            return (TimeSpan.FromTicks(absolutePositionMicroseconds * 10), isPlaying);
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

        using SafeProcessHandle process = NeteaseMemoryNativeReader.OpenProcess(
            NeteaseMemoryNativeReader.ProcessVmRead | NeteaseMemoryNativeReader.ProcessQueryLimitedInformation,
            false,
            processId.Value);
        if (process.IsInvalid)
        {
            InvalidateLocations();
            return false;
        }

        List<NeteaseMemoryClockCandidate> segmentSamples =
            NeteaseMemoryClockScanner.CaptureCurrentIntCandidates(
                process,
                segmentLocations,
                maximumPositionMicroseconds);
        IReadOnlyList<NeteaseMemoryClockCandidate> segmentConsensus =
            NeteaseMemoryClockSelectionPolicy.SelectConsensus(segmentSamples);
        List<NeteaseMemoryClockCandidate> absoluteSamples =
            NeteaseMemoryClockScanner.CaptureCurrentDoubleCandidates(
                process,
                absoluteLocations,
                maximumPositionMicroseconds);
        IReadOnlyList<NeteaseMemoryClockCandidate> absoluteConsensus =
            NeteaseMemoryClockSelectionPolicy.SelectConsensus(
                absoluteSamples,
                preferredPositionMicroseconds);
        DateTime nowUtc = DateTime.UtcNow;
        bool isPlaying;
        long absolutePositionMicroseconds;

        if (segmentConsensus.Count == 0)
        {
            if (absoluteConsensus.Count == 0)
            {
                if (_diagnostic.Enabled)
                {
                    _diagnostic.Info(
                        $"Netease cached memory clock miss: " +
                        $"pendingRebase={pendingRebase}, " +
                        $"segmentGroups={DescribeCandidateGroups(segmentSamples)}, " +
                        $"absoluteGroups={DescribeCandidateGroups(absoluteSamples)}");
                }
                MarkLocationsForRebase();
                return false;
            }

            absolutePositionMicroseconds = NeteaseMemoryClockScanner.Median(
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

        long segmentPositionMicroseconds = NeteaseMemoryClockScanner.Median(
            segmentConsensus.Select(sample => sample.PositionMicroseconds));
        lock (_stateGate)
        {
            if (absoluteConsensus.Count > 0)
            {
                long directAbsolutePositionMicroseconds = NeteaseMemoryClockScanner.Median(
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
                    if (_diagnostic.Enabled)
                    {
                        _diagnostic.Info(
                            $"Netease cached memory clock awaiting rebase: " +
                            $"segment={segmentPositionMicroseconds / 1_000_000.0:F3}s, " +
                            $"absoluteGroups={DescribeCandidateGroups(absoluteSamples)}");
                    }
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

    private (TimeSpan Position, bool IsPlaying)? CreateActivityState(
        int generation,
        bool isPlaying)
    {
        lock (_stateGate)
        {
            if (generation != _trackGeneration ||
                !_lastPositionMicroseconds.HasValue)
            {
                return null;
            }

            DateTime nowUtc = DateTime.UtcNow;
            long positionMicroseconds =
                NeteaseMemoryActivityPolicy.ExtrapolatePosition(
                    _lastPositionMicroseconds.Value,
                    _lastSampleUtc,
                    _lastIsPlaying,
                    nowUtc,
                    _maximumPositionMicroseconds);
            _lastPositionMicroseconds = positionMicroseconds;
            _lastSampleUtc = nowUtc;
            _lastIsPlaying = isPlaying;
            _diagnostic.Info(
                $"Netease read-only memory activity: " +
                $"isPlaying={isPlaying}, " +
                $"position={positionMicroseconds / 1_000_000.0:F3}s");
            return (
                TimeSpan.FromTicks(positionMicroseconds * 10),
                isPlaying);
        }
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

    private static string DescribeCandidateGroups(
        IReadOnlyList<NeteaseMemoryClockCandidate> candidates)
    {
        return string.Join(
            ";",
            candidates
                .GroupBy(candidate => candidate.PositionMicroseconds / 100_000)
                .Select(group => new
                {
                    Position = NeteaseMemoryClockScanner.Median(group.Select(candidate => candidate.PositionMicroseconds)),
                    AllocationCount = group.Select(candidate => candidate.AllocationBase).Distinct().Count(),
                    CandidateCount = group.Count()
                })
                .Where(group => group.AllocationCount >= 2)
                .OrderBy(group => group.Position)
                .Select(group =>
                    $"{group.Position / 1_000_000.0:F3}s/{group.AllocationCount}a/{group.CandidateCount}c"));
    }
}
