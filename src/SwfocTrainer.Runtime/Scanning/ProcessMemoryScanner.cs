using System.Runtime.InteropServices;
using SwfocTrainer.Runtime.Interop;

namespace SwfocTrainer.Runtime.Scanning;

internal static class ProcessMemoryScanner
{
    public static IReadOnlyList<nint> ScanInt32(
        int processId,
        int value,
        bool writableOnly,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (maxResults <= 0)
        {
            return Array.Empty<nint>();
        }

        var handle = OpenReadHandle(processId);

        if (handle == nint.Zero)
        {
            throw new InvalidOperationException($"Failed to open process {processId} for scan. Win32={Marshal.GetLastWin32Error()}");
        }

        try
        {
            var results = new List<nint>(capacity: Math.Min(maxResults, 256));
            var mbiSize = (nuint)Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();

            var address = nint.Zero;
            var lastAddress = nint.MinValue;

            while (results.Count < maxResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (address == lastAddress)
                {
                    // Avoid infinite loops if something goes wrong with address advancement.
                    break;
                }
                lastAddress = address;

                var queried = NativeMethods.VirtualQueryEx(handle, address, out var mbi, mbiSize);
                if (queried == 0)
                {
                    break;
                }

                var regionSize = (long)mbi.RegionSize;
                if (regionSize <= 0)
                {
                    // Ensure we always make forward progress.
                    address += 0x1000;
                    continue;
                }

                if (mbi.State == NativeMethods.MemCommit && IsReadable(mbi.Protect) && (!writableOnly || IsWritable(mbi.Protect)))
                {
                    ScanRegion(handle, mbi.BaseAddress, regionSize, value, results, maxResults, cancellationToken);
                }

                try
                {
                    address = mbi.BaseAddress + (nint)regionSize;
                }
                catch
                {
                    break;
                }
            }

            return results;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public static IReadOnlyList<nint> ScanFloatApprox(
        int processId,
        float value,
        float tolerance,
        bool writableOnly,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (maxResults <= 0)
        {
            return Array.Empty<nint>();
        }

        if (tolerance < 0)
        {
            tolerance = 0;
        }

        var handle = OpenReadHandle(processId);

        if (handle == nint.Zero)
        {
            throw new InvalidOperationException($"Failed to open process {processId} for float scan. Win32={Marshal.GetLastWin32Error()}");
        }

        try
        {
            var results = new List<nint>(capacity: Math.Min(maxResults, 256));
            var mbiSize = (nuint)Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();

            var address = nint.Zero;
            var lastAddress = nint.MinValue;

            while (results.Count < maxResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (address == lastAddress)
                {
                    break;
                }
                lastAddress = address;

                var queried = NativeMethods.VirtualQueryEx(handle, address, out var mbi, mbiSize);
                if (queried == 0)
                {
                    break;
                }

                var regionSize = (long)mbi.RegionSize;
                if (regionSize <= 0)
                {
                    address += 0x1000;
                    continue;
                }

                if (mbi.State == NativeMethods.MemCommit && IsReadable(mbi.Protect) && (!writableOnly || IsWritable(mbi.Protect)))
                {
                    ScanRegionFloatApprox(handle, mbi.BaseAddress, regionSize, value, tolerance, results, maxResults, cancellationToken);
                }

                try
                {
                    address = mbi.BaseAddress + (nint)regionSize;
                }
                catch
                {
                    break;
                }
            }

            return results;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static void ScanRegion(
        nint handle,
        nint regionBase,
        long regionSize,
        int value,
        List<nint> results,
        int maxResults,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 64 * 1024;
        var buffer = new byte[chunkSize];

        for (long offset = 0; offset < regionSize && results.Count < maxResults; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var toReadLong = Math.Min(chunkSize, regionSize - offset);
            if (toReadLong <= 0)
            {
                break;
            }

            var toRead = (int)toReadLong;

            // Ensure buffer is large enough for last partial read.
            if (buffer.Length != toRead)
            {
                buffer = new byte[toRead];
            }

            if (!NativeMethods.ReadProcessMemory(handle, regionBase + (nint)offset, buffer, toRead, out var readRaw))
            {
                continue;
            }

            var read = (int)readRaw;
            if (read < 4)
            {
                continue;
            }

            // Scan unaligned; it's slower but more reliable.
            for (var i = 0; i <= read - 4 && results.Count < maxResults; i++)
            {
                if (BitConverter.ToInt32(buffer, i) == value)
                {
                    results.Add(regionBase + (nint)offset + i);
                }
            }
        }
    }

    private static void ScanRegionFloatApprox(
        nint handle,
        nint regionBase,
        long regionSize,
        float value,
        float tolerance,
        List<nint> results,
        int maxResults,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 64 * 1024;
        var buffer = new byte[chunkSize];

        for (long offset = 0; offset < regionSize && results.Count < maxResults; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var toReadLong = Math.Min(chunkSize, regionSize - offset);
            if (toReadLong <= 0)
            {
                break;
            }

            var toRead = (int)toReadLong;
            if (buffer.Length != toRead)
            {
                buffer = new byte[toRead];
            }

            if (!NativeMethods.ReadProcessMemory(handle, regionBase + (nint)offset, buffer, toRead, out var readRaw))
            {
                continue;
            }

            var read = (int)readRaw;
            if (read < 4)
            {
                continue;
            }

            // Step by 4 bytes for float scanning. Fast and sufficient for typical game float fields.
            for (var i = 0; i <= read - 4 && results.Count < maxResults; i += 4)
            {
                var candidate = BitConverter.ToSingle(buffer, i);
                if (!float.IsFinite(candidate))
                {
                    continue;
                }

                if (MathF.Abs(candidate - value) <= tolerance)
                {
                    results.Add(regionBase + (nint)offset + i);
                }
            }
        }
    }

    private static bool IsReadable(uint protect)
    {
        if ((protect & NativeMethods.PageGuard) != 0)
        {
            return false;
        }

        if ((protect & NativeMethods.PageNoAccess) != 0)
        {
            return false;
        }

        return (protect & (NativeMethods.PageReadOnly |
                           NativeMethods.PageReadWrite |
                           NativeMethods.PageWriteCopy |
                           NativeMethods.PageExecuteRead |
                           NativeMethods.PageExecuteReadWrite |
                           NativeMethods.PageExecuteWriteCopy)) != 0;
    }

    private static bool IsWritable(uint protect)
    {
        return (protect & (NativeMethods.PageReadWrite |
                           NativeMethods.PageWriteCopy |
                           NativeMethods.PageExecuteReadWrite |
                           NativeMethods.PageExecuteWriteCopy)) != 0;
    }

    private static nint OpenReadHandle(int processId)
    {
        return NativeMethods.OpenProcess(
            (NativeMethods.ProcessAccess)(
                (uint)NativeMethods.ProcessAccess.QueryInformation |
                (uint)NativeMethods.ProcessAccess.VmRead),
            false,
            processId);
    }
}
