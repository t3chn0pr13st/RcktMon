using Avalonia.Controls;
namespace RcktMon.App.Views;

public sealed partial class WorkspaceWindow : Window
{
    public WorkspaceWindow()
    {
        InitializeComponent();
        Closed += (_, _) => Trading.Dispose();
    }
    public void ShowManualTicket() => Pages.SelectedIndex = 1;

    private void PageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Pages && Pages?.SelectedIndex != 1) Trading?.DismissReview();
    }
}
