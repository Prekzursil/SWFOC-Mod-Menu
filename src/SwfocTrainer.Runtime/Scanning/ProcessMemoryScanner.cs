using System.Runtime.InteropServices;
using SwfocTrainer.Runtime.Interop;

namespace SwfocTrainer.Runtime.Scanning;

internal static class ProcessMemoryScanner
{
    private readonly record struct FloatScanCriteria(float Value, float Tolerance);

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

        return ScanReadableRegions(
            processId,
            writableOnly,
            maxResults,
            cancellationToken,
            (handle, regionBase, regionSize, results, max, token) =>
                ScanRegion(handle, regionBase, regionSize, value, results, max, token));
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

        var criteria = new FloatScanCriteria(value, tolerance);
        return ScanReadableRegions(
            processId,
            writableOnly,
            maxResults,
            cancellationToken,
            (handle, regionBase, regionSize, results, max, token) =>
                ScanRegionFloatApprox(handle, regionBase, regionSize, criteria, results, max, token));
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
        ScanRegionChunks(
            handle,
            regionBase,
            regionSize,
            results,
            maxResults,
            cancellationToken,
            (buffer, read, chunkBase) =>
            {
                for (var i = 0; i <= read - 4 && results.Count < maxResults; i++)
                {
                    if (BitConverter.ToInt32(buffer, i) == value)
                    {
                        results.Add(chunkBase + i);
                    }
                }
            });
    }

    private static void ScanRegionFloatApprox(
        nint handle,
        nint regionBase,
        long regionSize,
        FloatScanCriteria criteria,
        List<nint> results,
        int maxResults,
        CancellationToken cancellationToken)
    {
        ScanRegionChunks(
            handle,
            regionBase,
            regionSize,
            results,
            maxResults,
            cancellationToken,
            (buffer, read, chunkBase) =>
            {
                for (var i = 0; i <= read - 4 && results.Count < maxResults; i += 4)
                {
                    var candidate = BitConverter.ToSingle(buffer, i);
                    if (!float.IsFinite(candidate))
                    {
                        continue;
                    }

                    if (MathF.Abs(candidate - criteria.Value) <= criteria.Tolerance)
                    {
                        results.Add(chunkBase + i);
                    }
                }
            });
    }

    private static void ScanRegionChunks(
        nint handle,
        nint regionBase,
        long regionSize,
        List<nint> results,
        int maxResults,
        CancellationToken cancellationToken,
        Action<byte[], int, nint> chunkScanner)
    {
        const int chunkSize = 64 * 1024;
        var buffer = new byte[chunkSize];

        for (long offset = 0; offset < regionSize && results.Count < maxResults; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toRead = (int)Math.Min(chunkSize, regionSize - offset);
            if (toRead <= 0)
            {
                break;
            }

            buffer = EnsureBufferSize(buffer, toRead);
            if (!TryReadChunk(handle, regionBase, offset, buffer, toRead, out var read))
            {
                continue;
            }

            chunkScanner(buffer, read, regionBase + (nint)offset);
        }
    }

    private static byte[] EnsureBufferSize(byte[] buffer, int requiredLength)
    {
        return buffer.Length == requiredLength ? buffer : new byte[requiredLength];
    }

    private static bool TryReadChunk(
        nint handle,
        nint regionBase,
        long offset,
        byte[] buffer,
        int toRead,
        out int read)
    {
        read = 0;
        if (!NativeMethods.ReadProcessMemory(handle, regionBase + (nint)offset, buffer, toRead, out var readRaw))
        {
            return false;
        }

        read = (int)readRaw;
        return read >= 4;
    }

    private static IReadOnlyList<nint> ScanReadableRegions(
        int processId,
        bool writableOnly,
        int maxResults,
        CancellationToken cancellationToken,
        Action<nint, nint, long, List<nint>, int, CancellationToken> regionScanner)
    {
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

                if (mbi.State == NativeMethods.MemCommit &&
                    IsReadable(mbi.Protect) &&
                    (!writableOnly || IsWritable(mbi.Protect)))
                {
                    regionScanner(handle, mbi.BaseAddress, regionSize, results, maxResults, cancellationToken);
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
