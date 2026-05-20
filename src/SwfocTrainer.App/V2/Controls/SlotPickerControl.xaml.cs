using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwfocTrainer.App.V2.ViewModels;

namespace SwfocTrainer.App.V2.Controls;

/// <summary>
/// v1.1.0 reusable slot picker. Replaces the typed-integer "Slot: -1" TextBox
/// pattern across 8+ V2 tabs with a ComboBox bound to PlayerSlotEntry items.
///
/// See <see cref="PlayerSlotEntry"/> for the item model. The host tab provides
/// the items via the <see cref="ItemsSource"/> DependencyProperty and optionally
/// a <see cref="RefreshCommand"/> that re-reads slot → faction labels from the
/// current game state (typically via SWFOC_GetAllPlayers).
/// </summary>
public partial class SlotPickerControl : UserControl
{
    public SlotPickerControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(SlotPickerControl),
            new PropertyMetadata(null));

    /// <summary>The collection of <see cref="PlayerSlotEntry"/> items to display.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedSlotProperty =
        DependencyProperty.Register(
            nameof(SelectedSlot),
            typeof(PlayerSlotEntry),
            typeof(SlotPickerControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>The currently selected slot. Two-way by default.</summary>
    public PlayerSlotEntry? SelectedSlot
    {
        get => (PlayerSlotEntry?)GetValue(SelectedSlotProperty);
        set => SetValue(SelectedSlotProperty, value);
    }

    public static readonly DependencyProperty RefreshCommandProperty =
        DependencyProperty.Register(
            nameof(RefreshCommand),
            typeof(ICommand),
            typeof(SlotPickerControl),
            new PropertyMetadata(null, OnRefreshCommandChanged));

    /// <summary>Command fired when the operator clicks the refresh ↻ button.</summary>
    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    public static readonly DependencyProperty RefreshButtonVisibilityProperty =
        DependencyProperty.Register(
            nameof(RefreshButtonVisibility),
            typeof(Visibility),
            typeof(SlotPickerControl),
            new PropertyMetadata(Visibility.Collapsed));

    /// <summary>
    /// Visibility of the refresh button. Auto-set to Visible when RefreshCommand
    /// is non-null; can also be controlled explicitly by the host tab.
    /// </summary>
    public Visibility RefreshButtonVisibility
    {
        get => (Visibility)GetValue(RefreshButtonVisibilityProperty);
        set => SetValue(RefreshButtonVisibilityProperty, value);
    }

    private static void OnRefreshCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SlotPickerControl c)
        {
            c.RefreshButtonVisibility = e.NewValue is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
