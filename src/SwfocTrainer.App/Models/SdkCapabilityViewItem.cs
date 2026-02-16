namespace SwfocTrainer.App.Models;

/// <summary>
/// UI model for displaying experimental SDK capability diagnostics.
/// </summary>
public sealed record SdkCapabilityViewItem(
    string OperationId,
    string State,
    string ReasonCode);
