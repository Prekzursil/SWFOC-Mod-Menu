using System.Runtime.InteropServices;
using SwfocTrainer.Runtime.Interop;

namespace SwfocTrainer.Tests.Runtime.Fakes;

/// <summary>
/// In-memory dictionary-based implementation of <see cref="IProcessMemory"/>
/// for unit testing. Stores bytes by address so that Read/Write round-trip
/// without any Win32 P/Invoke.
/// </summary>
internal sealed class FakeProcessMemory : IProcessMemory
{
    private readonly Dictionary<nint, byte[]> _pages = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Next address returned by <see cref="Allocate"/>.
    /// Incremented to simulate distinct allocations.
    /// </summary>
    private nint _nextAllocation = (nint)0x7FFE_0000;

    /// <summary>
    /// If set to true, all read/write operations will throw to simulate
    /// a dead process handle.
    /// </summary>
    public bool SimulateInvalid { get; set; }

    /// <summary>
    /// If set to true, <see cref="Free"/> returns false to simulate failure.
    /// </summary>
    public bool SimulateFreeFail { get; set; }

    /// <summary>
    /// Tracks the number of write operations performed.
    /// </summary>
    public int WriteCount { get; private set; }

    /// <summary>
    /// Tracks the number of read operations performed.
    /// </summary>
    public int ReadCount { get; private set; }

    public bool IsValid => !_disposed && !SimulateInvalid;

    public byte[] ReadBytes(nint address, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SimulateInvalid)
        {
            throw new InvalidOperationException("Simulated invalid process handle.");
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Read count must be non-negative.");
        }

        ReadCount++;
        var result = new byte[count];
        lock (_lock)
        {
            for (var i = 0; i < count; i++)
            {
                var addr = address + i;
                if (_pages.TryGetValue(addr, out var page))
                {
                    result[i] = page[0];
                }
                // Uninitialized memory returns 0 (default byte[])
            }
        }

        return result;
    }

    public T Read<T>(nint address) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        var bytes = ReadBytes(address, size);
        return MemoryMarshal.Read<T>(bytes);
    }

    public void Write<T>(nint address, T value) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        var buffer = new byte[size];
        MemoryMarshal.Write(buffer.AsSpan(), in value);
        WriteBytes(address, buffer, executablePatch: false);
    }

    public void WriteBytes(nint address, byte[] buffer, bool executablePatch)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SimulateInvalid)
        {
            throw new InvalidOperationException("Simulated invalid process handle.");
        }

        if (buffer.Length == 0)
        {
            return;
        }

        WriteCount++;
        lock (_lock)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                _pages[address + i] = [buffer[i]];
            }
        }
    }

    public nint Allocate(nuint size, bool executable, nint preferredAddress = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SimulateInvalid)
        {
            return nint.Zero;
        }

        nint allocated;
        lock (_lock)
        {
            allocated = preferredAddress != default ? preferredAddress : _nextAllocation;
            _nextAllocation = allocated + (nint)size + 0x1000;
        }

        return allocated;
    }

    public bool Free(nint address)
    {
        if (address == nint.Zero)
        {
            return true;
        }

        if (SimulateFreeFail)
        {
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    /// <summary>
    /// Seeds the fake memory with raw bytes at a specific address,
    /// useful for setting up test scenarios.
    /// </summary>
    public void Seed(nint address, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_lock)
        {
            for (var i = 0; i < data.Length; i++)
            {
                _pages[address + i] = [data[i]];
            }
        }
    }

    /// <summary>
    /// Seeds a typed value at a specific address.
    /// </summary>
    public void Seed<T>(nint address, T value) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        var buffer = new byte[size];
        MemoryMarshal.Write(buffer.AsSpan(), in value);
        Seed(address, buffer);
    }
}
