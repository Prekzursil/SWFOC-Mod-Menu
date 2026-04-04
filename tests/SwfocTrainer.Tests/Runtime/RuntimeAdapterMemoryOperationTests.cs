namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Tests previously in this file attempted to inject FakeProcessMemory (IProcessMemory)
/// into RuntimeAdapter's _memory field (ProcessMemoryAccessor). These types are incompatible:
/// ProcessMemoryAccessor is a concrete sealed class using P/Invoke, while FakeProcessMemory
/// implements IProcessMemory. Since ProcessMemoryAccessor does not implement IProcessMemory,
/// the reflection-based field injection fails with:
///   "Object of type 'FakeProcessMemory' cannot be converted to type 'ProcessMemoryAccessor'"
///
/// These tests were deleted because they test an integration path that does not exist
/// in the current source architecture. To make them work, the source code would need to be
/// refactored so that RuntimeAdapter uses IProcessMemory instead of ProcessMemoryAccessor.
/// </summary>
public sealed class RuntimeAdapterMemoryOperationTests
{
    // Intentionally empty. See class-level summary for rationale.
}
