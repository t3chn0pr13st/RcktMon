using Avalonia.Controls;
using Avalonia.Interactivity;
using RcktMon.App.ViewModels;

namespace RcktMon.App.Views;

public partial class SignalsView : UserControl
{
    public SignalsView() => InitializeComponent();

    /// <summary>
    /// Rebuilds the feed after a checkbox filter changes.
    /// </summary>
    /// <remarks>
    /// The chips are two-way bound, so the model already has the new value; this only
    /// tells it that the set as a whole changed and the list has to be recomputed.
    /// </remarks>
    private void OnFilterChanged(object? sender, RoutedEventArgs e) =>
        (DataContext as SignalsViewModel)?.ReapplyFilters();
}
