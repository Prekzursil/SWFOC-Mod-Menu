using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelQuickActionHelpers
{
    internal static void PopulateActiveFreezes(
        ObservableCollection<string> activeFreezes,
        IEnumerable<string> frozenSymbols,
        IEnumerable<string> activeToggles)
    {
        activeFreezes.Clear();
        foreach (var symbol in frozenSymbols)
        {
            activeFreezes.Add($"❄️ {symbol}");
        }

        foreach (var toggle in activeToggles)
        {
            activeFreezes.Add($"🔒 {toggle}");
        }

        if (activeFreezes.Count == 0)
        {
            activeFreezes.Add("(none)");
        }
    }
}
