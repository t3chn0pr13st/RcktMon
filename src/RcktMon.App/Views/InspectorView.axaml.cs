using Avalonia.Controls;
using Avalonia.Input.Platform;
using RcktMon.App.ViewModels;

namespace RcktMon.App.Views;

public partial class InspectorView : UserControl
{
    private void OpenTicket(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        (TopLevel.GetTopLevel(this) as MainWindow)?.ShowWorkspace(manualOrders: true);


    public InspectorView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => Attach();
    }

    private InspectorViewModel? _attached;

    /// <summary>
    /// Wires the clipboard, which only the view can reach.
    /// </summary>
    /// <remarks>
    /// <c>TopLevel.Clipboard</c> is the cross-platform API; the pre-rewrite code called
    /// <c>System.Windows.Clipboard.SetText</c> straight from a view model, which is both a
    /// layering violation and Windows-only.
    /// </remarks>
    private void Attach()
    {
        if (_attached is not null)
        {
            _attached.CopyRequested -= Copy;
        }

        _attached = DataContext as InspectorViewModel;

        if (_attached is not null)
        {
            _attached.CopyRequested += Copy;
        }
    }

    private void Copy(string text) =>
        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
}
