using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelSpawnHelpers
{
    internal static bool TryBuildBatchInputs(
        string? selectedProfileId,
        SpawnPresetViewItem? selectedSpawnPreset,
        RuntimeMode runtimeMode,
        string spawnQuantity,
        string spawnDelayMs,
        out string profileId,
        out SpawnPresetViewItem selectedPreset,
        out int quantity,
        out int delayMs,
        out string failureStatus)
    {
        profileId = selectedProfileId ?? string.Empty;
        selectedPreset = selectedSpawnPreset!;
        quantity = 0;
        delayMs = 0;
        failureStatus = string.Empty;

        if (selectedProfileId is null || selectedSpawnPreset is null)
        {
            failureStatus = "✗ Spawn batch blocked: select profile and preset.";
            return false;
        }

        if (runtimeMode == RuntimeMode.Unknown)
        {
            failureStatus = "✗ Spawn batch blocked: runtime mode is unknown.";
            return false;
        }

        if (!int.TryParse(spawnQuantity, out quantity) || quantity <= 0)
        {
            failureStatus = "✗ Invalid spawn quantity.";
            return false;
        }

        if (!int.TryParse(spawnDelayMs, out delayMs) || delayMs < 0)
        {
            failureStatus = "✗ Invalid spawn delay (ms).";
            return false;
        }

        return true;
    }
}
