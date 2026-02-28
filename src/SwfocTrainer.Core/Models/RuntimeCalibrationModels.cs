namespace SwfocTrainer.Core.Models;

public sealed record RuntimeCalibrationScanRequest(
    string TargetSymbol,
    int MaxCandidates = 12);

public sealed record RuntimeCalibrationCandidate(
    string SuggestedPattern,
    int Offset,
    SignatureAddressMode AddressMode,
    SymbolValueType ValueType,
    string InstructionRva,
    string Snippet,
    int ReferenceCount);

public sealed record RuntimeCalibrationScanResult(
    bool Succeeded,
    string ReasonCode,
    string Message,
    IReadOnlyList<RuntimeCalibrationCandidate> Candidates,
    string? ArtifactPath = null);
