namespace SwfocTrainer.App.Models;

/// <summary>
/// UI model for displaying action reliability diagnostics in Live Ops.
/// </summary>
public sealed record ActionReliabilityViewItem(
    string ActionId,
    string State,
    string ReasonCode,
    double Confidence,
    string Detail);
