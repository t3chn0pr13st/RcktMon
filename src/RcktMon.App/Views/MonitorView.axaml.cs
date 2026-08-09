using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RcktMon.App.ViewModels;
using RcktMon.Client.Core.Market;

namespace RcktMon.App.Views;

/// <summary>
/// The monitor grid's view.
/// </summary>
/// <remarks>
/// Column state lives here rather than in XAML bindings because Avalonia's
/// <c>DataGridColumn</c> is not part of the visual tree and therefore inherits no
/// <c>DataContext</c> — there is nothing for a binding on a column to bind against. The
/// column list in the view model and the columns declared in the markup are matched
/// positionally, and the constructor asserts the two agree so a mismatch fails loudly
/// instead of silently mislabelling a column.
/// </remarks>
public partial class MonitorView : UserControl
{
    private MonitorColumnState[] _columns = [];

    public MonitorView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => BindColumns();

        Grid.ColumnReordered += (_, _) => CaptureLayout();
        Grid.PointerReleased += OnGridPointerReleased;
        Grid.AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel);

        DetachedFromVisualTree += (_, _) => CaptureLayout();
    }

    private MonitorViewModel? Model => DataContext as MonitorViewModel;

    /// <summary>Puts the caret in the search box. Called by the window's Ctrl+K handler.</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Escape when SearchBox.IsFocused:
                SearchBox.Clear();
                e.Handled = true;
                return;

            case Key.Escape when model.Inspector.IsOpen:
                model.SelectedRow = null;
                e.Handled = true;
                return;

            case Key.Space when Grid.IsKeyboardFocusWithin:
                model.ToggleWatch(model.SelectedRow);
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    private void BindColumns()
    {
        if (Model is not { } model)
        {
            return;
        }

        _columns = [.. model.Columns];

        if (_columns.Length != Grid.Columns.Count)
        {
            throw new InvalidOperationException(
                $"MonitorColumns declares {_columns.Length} columns but MonitorView.axaml has " +
                $"{Grid.Columns.Count}. The two lists are matched positionally and must agree.");
        }

        for (int i = 0; i < _columns.Length; i++)
        {
            MonitorColumnState state = _columns[i];
            DataGridColumn column = Grid.Columns[i];

            column.Header = state.DisplayHeader;
            column.IsVisible = state.IsVisible;
            column.Width = new DataGridLength(state.Width);
            column.CanUserResize = true;

            // The header text carries the sort arrow, so it has to be refreshed whenever
            // the view model changes either.
            MonitorColumnState captured = state;
            DataGridColumn target = column;

            state.PropertyChanged += (_, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(MonitorColumnState.IsVisible):
                        target.IsVisible = captured.IsVisible;
                        ApplyStretch();
                        break;
                    case nameof(MonitorColumnState.SortIndicator):
                        target.Header = captured.DisplayHeader;
                        break;
                    case nameof(MonitorColumnState.Width):
                        target.Width = new DataGridLength(captured.Width);
                        break;
                }
            };
        }

        ApplyOrder();
        ApplyStretch();
    }

    /// <summary>
    /// Lets the rightmost visible column take the leftover width.
    /// </summary>
    /// <remarks>
    /// Purely cosmetic, but the alternative is a band of empty grid to the right of the
    /// last column that reads as a rendering fault rather than as spare space.
    /// </remarks>
    private void ApplyStretch()
    {
        DataGridColumn? last = null;

        foreach (DataGridColumn column in Grid.Columns)
        {
            if (!column.IsVisible)
            {
                continue;
            }

            int index = Grid.Columns.IndexOf(column);
            if (index >= 0 && index < _columns.Length)
            {
                column.Width = new DataGridLength(_columns[index].Width);
            }

            last = column;
        }

        if (last is not null)
        {
            last.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        }
    }

    private void ApplyOrder()
    {
        // Order 0 everywhere means "never customised", so leave the declaration order.
        if (_columns.All(static c => c.Order == 0))
        {
            return;
        }

        foreach ((MonitorColumnState state, int index) in _columns
                     .Select((state, index) => (state, index))
                     .OrderBy(pair => pair.state.Order))
        {
            Grid.Columns[index].DisplayIndex = Math.Clamp(state.Order, 0, Grid.Columns.Count - 1);
        }
    }

    /// <summary>Reads widths and display order back out of the grid into the view model.</summary>
    private void CaptureLayout()
    {
        for (int i = 0; i < _columns.Length && i < Grid.Columns.Count; i++)
        {
            DataGridColumn column = Grid.Columns[i];

            if (column.ActualWidth > 20)
            {
                _columns[i].Width = column.ActualWidth;
            }

            _columns[i].Order = column.DisplayIndex;
        }
    }

    /// <summary>
    /// Routes a header click to the view model's sort command.
    /// </summary>
    /// <remarks>
    /// Tunnelled, and the grid's own sorting stays off: the ordering is produced by the
    /// DynamicData pipeline, and letting the grid also sort would fight it.
    /// </remarks>
    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Model is not { } model || e.Source is not Visual source)
        {
            return;
        }

        DataGridColumnHeader? header = source as DataGridColumnHeader
            ?? source.FindAncestorOfType<DataGridColumnHeader>();

        // Matched on the rendered header text, which is unique across the column set and
        // is the only identity a DataGridColumnHeader exposes publicly.
        if (header?.Content is not string text)
        {
            return;
        }

        MonitorColumnState? state = Array.Find(_columns, c => c.DisplayHeader == text);

        if (state?.Sort is { } column)
        {
            model.SortBy(column);
        }
    }

    private void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e) => CaptureLayout();

    private void OnWatchClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: InstrumentRow row })
        {
            Model?.ToggleWatch(row);
            e.Handled = true;
        }
    }

    /// <summary>Chip toggles are two-way bound; this only reapplies the filter.</summary>
    private void OnChipToggled(object? sender, RoutedEventArgs e) => Model?.ReapplyFilter();
}
