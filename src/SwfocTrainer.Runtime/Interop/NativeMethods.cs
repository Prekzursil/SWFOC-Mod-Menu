using System.Runtime.InteropServices;

namespace SwfocTrainer.Runtime.Interop;

internal static class NativeMethods
{
    [Flags]
    internal enum ProcessAccessFlags : uint
    {
        VmRead = 0x0010,
        VmWrite = 0x0020,
        VmOperation = 0x0008,
        QueryInformation = 0x0400,
        QueryLimitedInformation = 0x1000,
        CreateThread = 0x0002,
        Synchronize = 0x00100000,
        All = 0x001F0FFF
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(ProcessAccessFlags access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadProcessMemory(
        nint hProcess,
        nint lpBaseAddress,
        [Out] byte[] lpBuffer,
        nint nSize,
        out nint lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool WriteProcessMemory(
        nint hProcess,
        nint lpBaseAddress,
        byte[] lpBuffer,
        nint nSize,
        out nint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool VirtualProtectEx(
        nint hProcess,
        nint lpAddress,
        nuint dwSize,
        uint flNewProtect,
        out uint lpflOldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool FlushInstructionCache(
        nint hProcess,
        nint lpBaseAddress,
        nuint dwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint VirtualAllocEx(
        nint hProcess,
        nint lpAddress,
        nuint dwSize,
        uint flAllocationType,
        uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool VirtualFreeEx(
        nint hProcess,
        nint lpAddress,
        nuint dwSize,
        uint dwFreeType);

    internal const uint MemCommit = 0x1000;
    internal const uint MemReserve = 0x2000;
    internal const uint MemRelease = 0x8000;

    internal const uint PageNoAccess = 0x01;
    internal const uint PageReadOnly = 0x02;
    internal const uint PageReadWrite = 0x04;
    internal const uint PageWriteCopy = 0x08;
    internal const uint PageExecute = 0x10;
    internal const uint PageExecuteRead = 0x20;
    internal const uint PageExecuteReadWrite = 0x40;
    internal const uint PageExecuteWriteCopy = 0x80;
    internal const uint PageGuard = 0x100;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nuint VirtualQueryEx(
        nint hProcess,
        nint lpAddress,
        out MemoryBasicInformation lpBuffer,
        nuint dwLength);
}
