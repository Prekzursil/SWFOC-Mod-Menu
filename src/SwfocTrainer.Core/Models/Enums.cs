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
    TacticalLand,
    TacticalSpace,
    AnyTactical,
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

public enum ProcessHostRole
{
    Unknown = 0,
    Launcher,
    GameHost
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

/// <summary>
/// Supported patch operation kinds for Save Lab patch-pack v1.
/// </summary>
public enum SavePatchOperationKind
{
    SetValue = 0
}

/// <summary>
/// High-level outcome classification for patch-pack apply attempts.
/// </summary>
public enum SavePatchApplyClassification
{
    Applied = 0,
    ValidationFailed = 1,
    CompatibilityFailed = 2,
    WriteFailed = 3,
    RolledBack = 4
}
