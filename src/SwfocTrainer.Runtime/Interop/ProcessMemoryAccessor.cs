using System.Runtime.InteropServices;

namespace SwfocTrainer.Runtime.Interop;

internal sealed class ProcessMemoryAccessor : IDisposable
{
    private nint _handle;

    public ProcessMemoryAccessor(int processId)
    {
        _handle = NativeMethods.OpenProcess(
            (NativeMethods.ProcessAccess)(
                (uint)NativeMethods.ProcessAccess.QueryInformation |
                (uint)NativeMethods.ProcessAccess.QueryLimitedInformation |
                (uint)NativeMethods.ProcessAccess.VmRead |
                (uint)NativeMethods.ProcessAccess.VmWrite |
                (uint)NativeMethods.ProcessAccess.VmOperation),
            false,
            processId);

        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException($"Failed to open process {processId}. Win32={Marshal.GetLastWin32Error()}");
        }
    }

    public T Read<T>(nint address) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        var buffer = new byte[size];

        if (!NativeMethods.ReadProcessMemory(_handle, address, buffer, size, out var read) || read.ToInt64() != size)
        {
            throw new InvalidOperationException($"ReadProcessMemory failed at 0x{address.ToInt64():X}. Win32={Marshal.GetLastWin32Error()}");
        }

        return MemoryMarshal.Read<T>(buffer);
    }

    public void Write<T>(nint address, T value) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        var buffer = new byte[size];
        MemoryMarshal.Write(buffer.AsSpan(), in value);

        if (!NativeMethods.WriteProcessMemory(_handle, address, buffer, size, out var written) || written.ToInt64() != size)
        {
            throw new InvalidOperationException($"WriteProcessMemory failed at 0x{address.ToInt64():X}. Win32={Marshal.GetLastWin32Error()}");
        }
    }

    public void WriteBytes(nint address, byte[] buffer, bool executablePatch)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        uint? oldProtect = null;
        try
        {
            if (executablePatch)
            {
                oldProtect = EnableWriteExecuteProtection(address, buffer.Length);
            }

            WriteProcessMemoryChecked(address, buffer);

            if (executablePatch)
            {
                NativeMethods.FlushInstructionCache(_handle, address, (nuint)buffer.Length);
            }
        }
        finally
        {
            if (oldProtect.HasValue)
            {
                RestoreProtection(address, buffer.Length, oldProtect.Value);
            }
        }
    }

    private uint EnableWriteExecuteProtection(nint address, int length)
    {
        if (NativeMethods.VirtualProtectEx(
                _handle,
                address,
                (nuint)length,
                NativeMethods.PageExecuteReadWrite,
                out var oldProtect))
        {
            return oldProtect;
        }

        throw new InvalidOperationException(
            $"VirtualProtectEx failed at 0x{address.ToInt64():X}. Win32={Marshal.GetLastWin32Error()}");
    }

    private void RestoreProtection(nint address, int length, uint oldProtect)
    {
        NativeMethods.VirtualProtectEx(
            _handle,
            address,
            (nuint)length,
            oldProtect,
            out _);
    }

    private void WriteProcessMemoryChecked(nint address, byte[] buffer)
    {
        if (!NativeMethods.WriteProcessMemory(_handle, address, buffer, buffer.Length, out var written) ||
            written.ToInt64() != buffer.Length)
        {
            throw new InvalidOperationException(
                $"WriteProcessMemory failed at 0x{address.ToInt64():X}. Win32={Marshal.GetLastWin32Error()}");
        }
    }

    public byte[] ReadBytes(nint address, int count)
    {
        var buffer = new byte[count];
        if (!NativeMethods.ReadProcessMemory(_handle, address, buffer, count, out var read) || read.ToInt64() != count)
        {
            throw new InvalidOperationException($"ReadProcessMemory byte block failed at 0x{address.ToInt64():X}. Win32={Marshal.GetLastWin32Error()}");
        }

        return buffer;
    }

    public nint Allocate(nuint size, bool executable, nint preferredAddress = default)
    {
        var protect = executable ? NativeMethods.PageExecuteReadWrite : NativeMethods.PageReadWrite;
        return NativeMethods.VirtualAllocEx(
            _handle,
            preferredAddress,
            size,
            NativeMethods.MemCommit | NativeMethods.MemReserve,
            protect);
    }

    public bool Free(nint address)
    {
        if (address == nint.Zero)
        {
            return true;
        }

        return NativeMethods.VirtualFreeEx(
            _handle,
            address,
            0,
            NativeMethods.MemRelease);
    }

    public void Dispose()
    {
        if (_handle != nint.Zero)
        {
            NativeMethods.CloseHandle(_handle);
            _handle = nint.Zero;
        }
    }
}
