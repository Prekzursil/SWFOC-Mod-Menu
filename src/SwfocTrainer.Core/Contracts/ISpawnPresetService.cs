using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Loads profile-scoped spawn presets and executes batch spawn plans.
/// </summary>
public interface ISpawnPresetService
{
    /// <summary>
    /// Loads presets for the requested profile.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Preset definitions resolved from profile preset files.</returns>
    Task<IReadOnlyList<SpawnPreset>> LoadPresetsAsync(string profileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SpawnPreset>> LoadPresetsAsync(string profileId)
    {
        return LoadPresetsAsync(profileId, CancellationToken.None);
    }

    /// <summary>
    /// Expands a preset and run options into an executable batch plan.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="preset">Selected spawn preset.</param>
    /// <param name="quantity">Number of items to enqueue.</param>
    /// <param name="delayMs">Delay between items in milliseconds.</param>
    /// <param name="factionOverride">Optional faction override.</param>
    /// <param name="entryMarkerOverride">Optional map marker override.</param>
    /// <param name="stopOnFailure">Whether execution should halt on first failed item.</param>
    /// <returns>Expanded batch plan.</returns>
    SpawnBatchPlan BuildBatchPlan(
        string profileId,
        SpawnPreset preset,
        int quantity,
        int delayMs,
        string? factionOverride,
        string? entryMarkerOverride,
        bool stopOnFailure);

    /// <summary>
    /// Executes a spawn batch plan via profile actions.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="plan">Plan to execute.</param>
    /// <param name="runtimeMode">Current runtime mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Batch execution summary and per-item results.</returns>
    Task<SpawnBatchExecutionResult> ExecuteBatchAsync(
        string profileId,
        SpawnBatchPlan plan,
        RuntimeMode runtimeMode,
        CancellationToken cancellationToken);

    Task<SpawnBatchExecutionResult> ExecuteBatchAsync(
        string profileId,
        SpawnBatchPlan plan,
        RuntimeMode runtimeMode)
    {
        return ExecuteBatchAsync(profileId, plan, runtimeMode, CancellationToken.None);
    }
}
