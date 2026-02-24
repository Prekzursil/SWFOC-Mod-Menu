using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Manages selected-unit snapshots and transactional edits for tactical workflows.
/// </summary>
public interface ISelectedUnitTransactionService
{
    /// <summary>
    /// Gets the first captured baseline snapshot for the current attach lifecycle.
    /// </summary>
    SelectedUnitSnapshot? Baseline { get; }

    /// <summary>
    /// Gets transaction history entries in execution order.
    /// </summary>
    IReadOnlyList<SelectedUnitTransactionRecord> History { get; }

    /// <summary>
    /// Captures the current selected-unit values from runtime symbols.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current selected-unit snapshot.</returns>
    Task<SelectedUnitSnapshot> CaptureAsync(CancellationToken cancellationToken);

    Task<SelectedUnitSnapshot> CaptureAsync()
    {
        return CaptureAsync(CancellationToken.None);
    }

    /// <summary>
    /// Applies a selected-unit draft as an ordered transaction with rollback-on-failure behavior.
    /// </summary>
    /// <param name="profileId">Active profile identifier.</param>
    /// <param name="draft">Draft values to apply.</param>
    /// <param name="runtimeMode">Current runtime mode used for strict gating.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transaction execution result.</returns>
    Task<SelectedUnitTransactionResult> ApplyAsync(
        string profileId,
        SelectedUnitDraft draft,
        RuntimeMode runtimeMode,
        CancellationToken cancellationToken);

    Task<SelectedUnitTransactionResult> ApplyAsync(
        string profileId,
        SelectedUnitDraft draft,
        RuntimeMode runtimeMode)
    {
        return ApplyAsync(profileId, draft, runtimeMode, CancellationToken.None);
    }

    /// <summary>
    /// Reverts the most recent committed transaction.
    /// </summary>
    /// <param name="profileId">Active profile identifier.</param>
    /// <param name="runtimeMode">Current runtime mode used for strict gating.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Revert execution result.</returns>
    Task<SelectedUnitTransactionResult> RevertLastAsync(
        string profileId,
        RuntimeMode runtimeMode,
        CancellationToken cancellationToken);

    Task<SelectedUnitTransactionResult> RevertLastAsync(
        string profileId,
        RuntimeMode runtimeMode)
    {
        return RevertLastAsync(profileId, runtimeMode, CancellationToken.None);
    }

    /// <summary>
    /// Restores selected-unit values back to the captured baseline snapshot.
    /// </summary>
    /// <param name="profileId">Active profile identifier.</param>
    /// <param name="runtimeMode">Current runtime mode used for strict gating.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Baseline restore result.</returns>
    Task<SelectedUnitTransactionResult> RestoreBaselineAsync(
        string profileId,
        RuntimeMode runtimeMode,
        CancellationToken cancellationToken);

    Task<SelectedUnitTransactionResult> RestoreBaselineAsync(
        string profileId,
        RuntimeMode runtimeMode)
    {
        return RestoreBaselineAsync(profileId, runtimeMode, CancellationToken.None);
    }
}
