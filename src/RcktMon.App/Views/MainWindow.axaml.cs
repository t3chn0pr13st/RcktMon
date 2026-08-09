using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RcktMon.App.ViewModels;
using RcktMon.Client.Core.Settings;

namespace RcktMon.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Opened += (_, _) => RestoreGeometry();
        Closing += (_, _) => CaptureGeometry();
    }

    private MainWindowViewModel? Model => DataContext as MainWindowViewModel;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl/Cmd+K and "/" both jump to search — the two conventions users arrive with.
        bool commandK = e.Key == Key.K &&
            e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

        bool slash = e.Key == Key.Oem2 &&
            e.KeyModifiers == KeyModifiers.None &&
            FocusManager?.GetFocusedElement() is not TextBox;

        if (commandK || slash)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        // Digits switch pages, the way every tabbed app the audience already uses does.
        if (e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)
            && e.Key is >= Key.D1 and <= Key.D5
            && Model is { } model)
        {
            int index = e.Key - Key.D1;
            if (index < model.Pages.Count)
            {
                model.CurrentPage = model.Pages[index];
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    /// <summary>Switches to the monitor and puts the caret in its search box.</summary>
    private void FocusSearch()
    {
        Model?.Navigate("monitor");

        // The page may not be realised yet when navigation has just happened, so this
        // runs after the next layout pass rather than immediately.
        Dispatcher.UIThread.Post(
            () => this.GetVisualDescendants().OfType<MonitorView>().FirstOrDefault()?.FocusSearch(),
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Restores the last window position, but only somewhere a screen actually exists.
    /// </summary>
    /// <remarks>
    /// The pre-rewrite build validated against <c>SystemParameters.VirtualScreen*</c>, so
    /// unplugging the monitor a window was last on reopened it off-screen with no way to
    /// drag it back.
    /// </remarks>
    private void RestoreGeometry()
    {
        if (Model?.Settings.Window is not { HasPosition: true } layout)
        {
            return;
        }

        Width = Math.Max(MinWidth, layout.Width);
        Height = Math.Max(MinHeight, layout.Height);

        var position = new PixelPoint((int)layout.X, (int)layout.Y);

        bool onScreen = Screens.All.Any(screen => screen.Bounds.Contains(position));
        if (onScreen)
        {
            Position = position;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (layout.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void CaptureGeometry()
    {
        if (Model is not { } model)
        {
            return;
        }

        // Normal size and position only: capturing the maximised bounds would restore a
        // window that cannot be un-maximised back to anything sensible.
        model.Settings.Window = new WindowLayout
        {
            X = Position.X,
            Y = Position.Y,
            Width = WindowState == WindowState.Normal ? Width : model.Settings.Window.Width,
            Height = WindowState == WindowState.Normal ? Height : model.Settings.Window.Height,
            Maximized = WindowState == WindowState.Maximized,
            HasPosition = true,
        };

        model.Persist();
    }
}
