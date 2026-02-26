using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelSpawnHelpers
{
    internal sealed record SpawnBatchInputRequest(
        string? SelectedProfileId,
        SpawnPresetViewItem? SelectedSpawnPreset,
        RuntimeMode RuntimeMode,
        string SpawnQuantity,
        string SpawnDelayMs);

    internal sealed record SpawnBatchInputResult(
        bool Succeeded,
        string ProfileId,
        SpawnPresetViewItem? SelectedPreset,
        int Quantity,
        int DelayMs,
        string FailureStatus);

    internal static SpawnBatchInputResult TryBuildBatchInputs(SpawnBatchInputRequest request)
    {
        if (request.SelectedProfileId is null || request.SelectedSpawnPreset is null)
        {
            return new SpawnBatchInputResult(
                Succeeded: false,
                ProfileId: string.Empty,
                SelectedPreset: null,
                Quantity: 0,
                DelayMs: 0,
                FailureStatus: "✗ Spawn batch blocked: select profile and preset.");
        }

        if (request.RuntimeMode == RuntimeMode.Unknown)
        {
            return new SpawnBatchInputResult(
                Succeeded: false,
                ProfileId: string.Empty,
                SelectedPreset: null,
                Quantity: 0,
                DelayMs: 0,
                FailureStatus: "✗ Spawn batch blocked: runtime mode is unknown.");
        }

        if (!int.TryParse(request.SpawnQuantity, out var quantity) || quantity <= 0)
        {
            return new SpawnBatchInputResult(
                Succeeded: false,
                ProfileId: string.Empty,
                SelectedPreset: null,
                Quantity: 0,
                DelayMs: 0,
                FailureStatus: "✗ Invalid spawn quantity.");
        }

        if (!int.TryParse(request.SpawnDelayMs, out var delayMs) || delayMs < 0)
        {
            return new SpawnBatchInputResult(
                Succeeded: false,
                ProfileId: string.Empty,
                SelectedPreset: null,
                Quantity: 0,
                DelayMs: 0,
                FailureStatus: "✗ Invalid spawn delay (ms).");
        }

        return new SpawnBatchInputResult(
            Succeeded: true,
            ProfileId: request.SelectedProfileId,
            SelectedPreset: request.SelectedSpawnPreset,
            Quantity: quantity,
            DelayMs: delayMs,
            FailureStatus: string.Empty);
    }
}
