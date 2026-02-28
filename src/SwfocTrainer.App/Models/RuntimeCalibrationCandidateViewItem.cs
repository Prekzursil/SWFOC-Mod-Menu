namespace SwfocTrainer.App.Models;

/// <summary>
/// UI model for displaying runtime calibration scan candidates.
/// </summary>
public sealed record RuntimeCalibrationCandidateViewItem(
    string SuggestedPattern,
    int Offset,
    string AddressMode,
    string ValueType,
    string InstructionRva,
    int ReferenceCount,
    string Snippet);
