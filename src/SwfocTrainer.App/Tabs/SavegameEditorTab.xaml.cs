using System.Windows.Controls;

namespace SwfocTrainer.App.Tabs;

/// <summary>
/// 2026-05-22 (iter-289c, spec iter-289 savegame editor tab): the WPF view
/// over <c>SavegameEditorTabViewModel</c>. A self-contained
/// <see cref="UserControl"/> — it binds against its inherited
/// <see cref="System.Windows.FrameworkElement.DataContext"/>, so the host
/// (a <c>MainWindowV2.xaml</c> tab item) supplies the view-model when it
/// drops this control into the trainer's savegame-mode tab strip.
///
/// <para>
/// The host <c>TabItem</c> and the <c>MainViewModelV2</c> registration that
/// makes the tab visible are an editor-polish wiring step — those App-shell
/// files are outside the savegame hat's owned scope. This control, its
/// view-model and the engine behind it are complete and tested on their own.
/// </para>
/// </summary>
public partial class SavegameEditorTab : UserControl
{
    /// <summary>Creates the savegame editor view.</summary>
    public SavegameEditorTab()
    {
        InitializeComponent();
    }
}
