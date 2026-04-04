namespace SwfocTrainer.Runtime.Interop;

/// <summary>
/// Abstraction over process memory operations, enabling testability
/// by decoupling business logic from Win32 P/Invoke calls.
/// </summary>
internal interface IProcessMemory : IDisposable
{
    /// <summary>
    /// Indicates whether the underlying process handle is still valid.
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    /// Reads a block of bytes from the target process memory.
    /// </summary>
    byte[] ReadBytes(nint address, int count);

    /// <summary>
    /// Reads an unmanaged value from the target process memory.
    /// </summary>
    T Read<T>(nint address) where T : unmanaged;

    /// <summary>
    /// Writes an unmanaged value to the target process memory.
    /// </summary>
    void Write<T>(nint address, T value) where T : unmanaged;

    /// <summary>
    /// Writes a byte array to the target process memory, optionally
    /// adjusting page protection for executable code patches.
    /// </summary>
    void WriteBytes(nint address, byte[] buffer, bool executablePatch);

    /// <summary>
    /// Allocates a block of memory in the target process.
    /// </summary>
    nint Allocate(nuint size, bool executable, nint preferredAddress = default);

    /// <summary>
    /// Frees a previously allocated block in the target process.
    /// </summary>
    bool Free(nint address);
}
