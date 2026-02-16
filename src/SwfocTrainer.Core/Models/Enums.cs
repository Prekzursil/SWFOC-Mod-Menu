namespace SwfocTrainer.Core.Models;

public enum ExeTarget
{
    Unknown = 0,
    Sweaw,
    Swfoc
}

public enum ExecutionKind
{
    Memory = 0,
    Helper,
    Save,
    CodePatch,
    Freeze,
    Sdk
}

public enum ActionCategory
{
    Global = 0,
    Economy,
    Tactical,
    Campaign,
    Unit,
    Hero,
    Save
}

public enum AddressSource
{
    None = 0,
    Signature,
    Fallback
}

public enum SymbolValueType
{
    Int32 = 0,
    Int64,
    Float,
    Double,
    Byte,
    Bool,
    Utf8String,
    Pointer
}

public enum SignatureAddressMode
{
    HitPlusOffset = 0,
    ReadAbsolute32AtOffset,
    ReadRipRelative32AtOffset
}

public enum RuntimeMode
{
    Unknown = 0,
    Galactic,
    Tactical,
    Menu,
    SaveEditor
}

public enum LaunchKind
{
    Unknown = 0,
    BaseGame,
    Workshop,
    LocalModPath,
    Mixed
}

public enum DependencyValidationStatus
{
    Pass = 0,
    SoftFail = 1,
    HardFail = 2
}

public enum SymbolHealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Unresolved = 2
}

public enum ActionReliabilityState
{
    Stable = 0,
    Experimental = 1,
    Unavailable = 2
}
