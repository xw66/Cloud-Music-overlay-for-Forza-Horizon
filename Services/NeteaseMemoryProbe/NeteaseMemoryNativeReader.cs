using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace HorizonRadioOverlay.Services;

internal readonly record struct MemoryRegion(
    long Address,
    long AllocationBase,
    long Size);

[SupportedOSPlatform("windows")]
internal static class NeteaseMemoryNativeReader
{
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint MemCommit = 0x1000;
    private const uint MemPrivate = 0x20000;
    private const uint PageNoAccess = 0x01;
    private const uint PageReadWrite = 0x04;
    private const uint PageWriteCopy = 0x08;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint PageExecuteWriteCopy = 0x80;
    private const uint PageGuard = 0x100;

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

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(
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

    public static IEnumerable<MemoryRegion> EnumerateReadablePrivateRegions(
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

    public static bool TryGetReadablePrivateRegion(
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

    public static bool IsWritable(uint protection)
    {
        uint baseProtection = protection & 0xFF;
        return baseProtection is PageReadWrite or PageWriteCopy or
            PageExecuteReadWrite or PageExecuteWriteCopy;
    }

    public static bool TryReadInt64(
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

    public static bool TryReadDoubleSeconds(
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

    public static bool TryReadBuffer(
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
}
