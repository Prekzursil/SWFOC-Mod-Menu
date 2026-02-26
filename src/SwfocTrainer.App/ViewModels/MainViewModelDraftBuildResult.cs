using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

internal sealed record DraftBuildResult(bool Succeeded, string Message, SelectedUnitDraft? Draft)
{
    internal static DraftBuildResult Failed(string message)
    {
        return new DraftBuildResult(false, message, null);
    }

    internal static DraftBuildResult FromDraft(SelectedUnitDraft draft)
    {
        return new DraftBuildResult(true, "ok", draft);
    }
}
