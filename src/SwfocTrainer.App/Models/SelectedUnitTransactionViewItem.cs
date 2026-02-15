namespace SwfocTrainer.App.Models;

/// <summary>
/// UI model for selected-unit transaction history rows.
/// </summary>
public sealed record SelectedUnitTransactionViewItem(
    string TransactionId,
    DateTimeOffset Timestamp,
    bool IsRollback,
    string Operation,
    string AppliedActions);
