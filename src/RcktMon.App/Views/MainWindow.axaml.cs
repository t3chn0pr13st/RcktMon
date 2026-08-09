using Avalonia.Controls;
using Avalonia.Input;

namespace RcktMon.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl/Cmd+K and "/" both focus search — the two conventions users arrive with.
        bool commandK = e.Key == Key.K &&
            e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

        if (commandK || (e.Key == Key.Oem2 && e.KeyModifiers == KeyModifiers.None && !IsTextInputFocused()))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && SearchBox.IsFocused)
        {
            SearchBox.Clear();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private bool IsTextInputFocused() => FocusManager?.GetFocusedElement() is TextBox;
}
