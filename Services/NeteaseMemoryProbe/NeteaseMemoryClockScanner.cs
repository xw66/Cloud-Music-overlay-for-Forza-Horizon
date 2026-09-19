using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace HorizonRadioOverlay.Services;

internal readonly record struct MemorySeed(int Offset, long Value);

internal readonly record struct MemoryDoubleSeed(int Offset, long PositionMicroseconds);

internal sealed record MemorySeedChunk(
    long Address,
    long AllocationBase,
    int Length,
    long CapturedAt,
    IReadOnlyList<MemorySeed> Seeds,
    IReadOnlyList<MemoryDoubleSeed> DoubleSeeds);

internal readonly record struct MemoryPassCandidate(
    long ChunkAddress,
    long AllocationBase,
    int ChunkLength,
    int Offset,
    long Value,
    long CapturedAt);

internal readonly record struct MemoryDoublePassCandidate(
    long ChunkAddress,
    long AllocationBase,
    int ChunkLength,
    int Offset,
    long Value,
    long CapturedAt);

internal readonly record struct RawFinalCandidate(
    long Address,
    long AllocationBase,
    long Value,
    long CapturedAt,
    long DriftMicroseconds);

internal readonly record struct MemoryClockLocation(
    long Address,
    long AllocationBase);

internal readonly record struct MemoryScanHint(
    long Address,
    long AllocationBase,
    int Length);

internal sealed record FastAbsoluteClockDiscovery(
    long PositionMicroseconds,
    MemoryClockLocation[] Locations,
    MemoryScanHint[] Hints);

internal sealed record MemoryClockDiscovery(
    int ProcessId,
    string ProductVersion,
    MemoryClockLocation[] Locations,
    long PositionMicroseconds,
    long BaseOffsetMicroseconds,
    long? SegmentPositionMicroseconds,
    MemoryClockLocation[] AbsoluteClockLocations,
    MemoryScanHint[] AbsoluteClockHints);

internal sealed record MemoryClockScanResult(
    MemoryClockDiscovery? Discovery,
    bool? IsPlayingObservation);

[SupportedOSPlatform("windows")]
internal static class NeteaseMemoryClockScanner
{
    private const int ScanChunkBytes = 1024 * 1024;
    private const long MaximumScanBytes = 768L * 1024 * 1024;
    private const long MaximumFastRebaseBytesPerAllocation = 32L * 1024 * 1024;
    private const int MaximumSeedCount = 1_000_000;
    private const int MaximumDoubleSeedCount = 500_000;
    private const int MaximumPassCandidateCount = 8_192;
    private const long MinimumPositionMicroseconds = 250_000;
    private const int FastRebasePassDelayMilliseconds = 120;
    private const int FastRebaseFinalDelayMilliseconds = 160;

    public static MemoryClockDiscovery? DiscoverClock(
        DiagnosticService diagnostic,
        Process process,
        SafeProcessHandle processHandle,
        string productVersion,
        long maximumPositionMicroseconds,
        IReadOnlyList<MemoryClockLocation> knownSegmentLocations,
        IReadOnlyList<MemoryScanHint> knownAbsoluteClockHints,
        long? preferredPositionMicroseconds,
        out bool? isPlayingObservation)
    {
        isPlayingObservation = null;

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
                    NeteaseMemoryClockSelectionPolicy.SelectConsensus(
                        knownSegmentCandidates);
                if (knownSegmentConsensus.Count >= 2)
                {
                    long fastSegmentPositionMicroseconds = Median(
                        knownSegmentConsensus.Select(candidate => candidate.PositionMicroseconds));
                    long? baseOffsetMicroseconds = NeteaseMemoryClockOffsetPolicy.ResolveBaseOffset(
                        fastSegmentPositionMicroseconds,
                        fastAbsoluteClock.PositionMicroseconds);
                    if (baseOffsetMicroseconds.HasValue)
                    {
                        if (!NeteaseMemoryClockOffsetPolicy.IsPlausibleSeekRebase(
                                preferredPositionMicroseconds,
                                baseOffsetMicroseconds.Value))
                        {
                            diagnostic.Info(
                                $"Netease read-only memory fast rebase rejected near-zero offset: " +
                                $"previous={preferredPositionMicroseconds / 1_000_000.0:F3}s, " +
                                $"base={baseOffsetMicroseconds.Value / 1_000_000.0:F3}s");
                            return null;
                        }

                        diagnostic.Info(
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
            diagnostic.Info("Netease read-only memory clock scan: no seed chunks.");
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
            isPlayingObservation = false;
            diagnostic.Info($"Netease read-only memory clock scan: no advancing candidates (seedChunks={chunks.Count}).");
            return null;
        }

        isPlayingObservation = true;

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
        if (diagnostic.Enabled)
        {
            diagnostic.Info(
                $"Netease read-only memory clock scan: int64Groups={DescribeCandidateGroups(finalCandidates)}, " +
                $"doubleGroups={DescribeCandidateGroups(finalDoubleCandidates)}");
        }
        IReadOnlyList<NeteaseMemoryClockCandidate> finalDoubleConsensus =
            NeteaseMemoryClockSelectionPolicy.SelectConsensus(
                finalDoubleCandidates,
                preferredPositionMicroseconds);
        if (finalDoubleConsensus.Count < 2)
        {
            diagnostic.Info(
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
            finalDoubleCandidates,
            doublePassCandidates);

        if (knownSegmentLocations.Count >= 2)
        {
            List<NeteaseMemoryClockCandidate> knownSegmentCandidates =
                CaptureCurrentIntCandidates(
                    processHandle,
                    knownSegmentLocations,
                    maximumPositionMicroseconds);
            IReadOnlyList<NeteaseMemoryClockCandidate> knownSegmentConsensus =
                NeteaseMemoryClockSelectionPolicy.SelectConsensus(
                    knownSegmentCandidates);
            if (knownSegmentConsensus.Count < 2)
            {
                diagnostic.Info(
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
                diagnostic.Info(
                    $"Netease read-only memory rebase rejected: " +
                    $"segment={rebasedSegmentPositionMicroseconds / 1_000_000.0:F3}s, " +
                    $"absolute={absolutePositionMicroseconds / 1_000_000.0:F3}s");
                return null;
            }

            if (!NeteaseMemoryClockOffsetPolicy.IsPlausibleSeekRebase(
                    preferredPositionMicroseconds,
                    baseOffsetMicroseconds.Value))
            {
                diagnostic.Info(
                    $"Netease read-only memory rebase rejected near-zero offset: " +
                    $"previous={preferredPositionMicroseconds / 1_000_000.0:F3}s, " +
                    $"base={baseOffsetMicroseconds.Value / 1_000_000.0:F3}s");
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
        if (diagnostic.Enabled)
        {
            diagnostic.Info(
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
        if (diagnostic.Enabled)
        {
            diagnostic.Info(
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

    public static FastAbsoluteClockDiscovery? TryDiscoverAbsoluteClockFromHints(
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
            SelectScanHints(finalDoubleCandidates, doublePassCandidates));
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
            if (!NeteaseMemoryNativeReader.TryReadBuffer(process, hint.Address, buffer))
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
                    !NeteaseMemoryNativeReader.TryGetReadablePrivateRegion(process, address, out MemoryRegion region) ||
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
            foreach (MemoryRegion region in NeteaseMemoryNativeReader.EnumerateReadablePrivateRegions(process)
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

    private static List<MemorySeedChunk> CaptureSeeds(
        SafeProcessHandle process,
        Stopwatch stopwatch,
        long maximumPositionMicroseconds)
    {
        List<MemorySeedChunk> chunks = [];
        long scannedBytes = 0;
        int seedCount = 0;
        int doubleSeedCount = 0;

        foreach (MemoryRegion region in NeteaseMemoryNativeReader.EnumerateReadablePrivateRegions(process))
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
                if (!NeteaseMemoryNativeReader.TryReadBuffer(process, address, buffer))
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
            if (!NeteaseMemoryNativeReader.TryReadBuffer(process, chunk.Address, buffer))
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
            if (!NeteaseMemoryNativeReader.TryReadBuffer(process, first.ChunkAddress, buffer))
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
            if (!NeteaseMemoryNativeReader.TryReadBuffer(process, first.ChunkAddress, buffer))
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
            if (!NeteaseMemoryNativeReader.TryReadInt64(process, candidate.Address, out long value))
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

    public static List<NeteaseMemoryClockCandidate> CaptureCurrentIntCandidates(
        SafeProcessHandle process,
        IReadOnlyList<MemoryClockLocation> locations,
        long maximumPositionMicroseconds)
    {
        List<NeteaseMemoryClockCandidate> current = [];
        foreach (MemoryClockLocation location in locations)
        {
            if (!NeteaseMemoryNativeReader.TryReadInt64(process, location.Address, out long value) ||
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

    public static List<NeteaseMemoryClockCandidate> CaptureCurrentDoubleCandidates(
        SafeProcessHandle process,
        IReadOnlyList<MemoryClockLocation> locations,
        long maximumPositionMicroseconds)
    {
        List<NeteaseMemoryClockCandidate> current = [];
        foreach (MemoryClockLocation location in locations)
        {
            if (!NeteaseMemoryNativeReader.TryReadDoubleSeconds(process, location.Address, out long value) ||
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

    public static long Median(IEnumerable<long> values)
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
        if (!NeteaseMemoryNativeReader.TryReadBuffer(process, address - radiusBytes, buffer))
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
}
