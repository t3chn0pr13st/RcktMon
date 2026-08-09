using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using RcktMon.App.Threading;
using RcktMon.Client.Core.Market;
using RcktMon.Client.Core.Settings;

namespace RcktMon.App.ViewModels;

/// <summary>One toggleable filter with a live count next to it.</summary>
public sealed partial class FilterChip(string key, string label) : ObservableObject
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    [ObservableProperty] private bool _isSelected;

    [ObservableProperty] private int _count;

    /// <summary>Label with its count, which is what the chip actually renders.</summary>
    public string Display => Count > 0 ? $"{Label} · {Count}" : Label;

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Display));
}

/// <summary>
/// The monitor grid: a live-sorted, filtered view over every instrument.
/// </summary>
/// <remarks>
/// <para>
/// This class exists to solve one problem the WPF build got for free and Avalonia does
/// not: <c>CollectionViewSource</c> with <c>IsLiveSortingRequested</c>, which continuously
/// re-sorted the grid as prices ticked. Avalonia's <c>DataGridCollectionView</c> has no
/// equivalent, so the ordering is owned here via DynamicData.
/// </para>
/// <para>
/// The shape of the pipeline below is load-bearing; see the comments on each stage.
/// </para>
/// </remarks>
public sealed partial class MonitorViewModel : PageViewModel, IDisposable
{
    /// <summary>
    /// How long row *order* may lag behind the data.
    /// </summary>
    /// <remarks>
    /// Coalescing refreshes into one change set every 250 ms caps re-sorting at four
    /// times a second no matter how fast quotes arrive. Cell *values* still update
    /// immediately, because each row raises its own property change and the binding
    /// updates in place — only the ordering waits. That trade is the right way round:
    /// a stale number is noticed instantly, a stale ordering is not noticed at all.
    /// </remarks>
    private static readonly TimeSpan SortBuffer = TimeSpan.FromMilliseconds(250);

    /// <summary>Day volume multiple above which an instrument counts as unusually active.</summary>
    private const decimal UnusualVolume = 1.5m;

    /// <summary>Drains between recounts of the chip badges.</summary>
    /// <remarks>
    /// Counting six predicates over 2000 rows on every 100 ms drain would be 120 000
    /// evaluations a second to keep a number on a chip current. Twice a second is well
    /// inside what anyone perceives.
    /// </remarks>
    private const int RecountEvery = 5;

    private readonly ClientSession _session;
    private readonly ClientSettings _settings;
    private readonly Action _persist;

    private readonly BehaviorSubject<Func<InstrumentRow, bool>> _filter;
    private readonly BehaviorSubject<IComparer<InstrumentRow>> _comparer;
    private readonly IDisposable _pipeline;

    private readonly HashSet<string> _watchlist;

    private int _drainsSinceRecount;
    private int _knownRowCount = -1;

    public MonitorViewModel(ClientSession session, ClientSettings settings, Action persist)
        : base("monitor", "Монитор", "Icon.Monitor")
    {
        _session = session;
        _settings = settings;
        _persist = persist;
        _watchlist = [.. settings.Watchlist];

        Columns = [.. MonitorColumns.CreateDefault()];
        Inspector = new InspectorViewModel(session);

        _showSparklines = settings.ShowSparklines;
        _compactDensity = settings.CompactDensity;

        _filter = new BehaviorSubject<Func<InstrumentRow, bool>>(static _ => true);
        _comparer = new BehaviorSubject<IComparer<InstrumentRow>>(
            InstrumentRowComparer.For(MonitorColumn.DayChange, descending: true));

        _session.Quotes.SetSortColumn(MonitorColumn.DayChange);

        _pipeline = _session.Quotes.Connect()
            .Filter(_filter)

            // Never AutoRefresh(x => x.DayChange): that attaches an INotifyPropertyChanged
            // handler per row and re-evaluates on every property change — roughly 16 000
            // evaluations a second at 2000 instruments. Each row instead raises a single
            // signal, and only when the value of the *active* sort column actually moved.
            .AutoRefreshOnObservable(
                static row => row.SortKeyChanged,
                changeSetBuffer: SortBuffer,
                scheduler: TaskPoolScheduler.Default)

            // Diff and sort off the UI thread; SortAndBind writes into the bound
            // collection, so it has to be back on the UI thread by the time it runs.
            .ObserveOn(UiThreadScheduler.Instance)

            .SortAndBind(
                out ReadOnlyObservableCollection<InstrumentRow> rows,
                _comparer,
                new SortAndBindOptions
                {
                    // O(log n) insert position instead of a linear scan per moved row.
                    UseBinarySearch = true,

                    // At the opening auction hundreds of rows reorder at once. Without a
                    // threshold DynamicData emits an individual Move for each and the
                    // DataGrid re-realises containers every time, which is visible as a
                    // stutter. One Reset repaints the viewport once instead.
                    ResetThreshold = 64,
                })
            .Subscribe();

        Rows = rows;

        // Applied explicitly rather than through the property-changed hook: the field
        // initialiser above does not raise one, so without this the toggle and the column
        // would start out disagreeing.
        Columns.First(c => c.Id == MonitorColumns.Sparkline).IsVisible = _showSparklines;

        SyncSortIndicators();
    }

    /// <summary>Sorted, filtered rows bound directly to the grid.</summary>
    public ReadOnlyObservableCollection<InstrumentRow> Rows { get; }

    /// <summary>Column visibility, width and order.</summary>
    public ObservableCollection<MonitorColumnState> Columns { get; }

    /// <summary>Currency chips, built from the catalog once it lands.</summary>
    public ObservableCollection<FilterChip> CurrencyChips { get; } = [];

    /// <summary>Exchange chips, built from the catalog once it lands.</summary>
    public ObservableCollection<FilterChip> ExchangeChips { get; } = [];

    /// <summary>Saved views, presets first.</summary>
    public ObservableCollection<SavedView> Views { get; } = [];

    /// <summary>The right-hand instrument inspector.</summary>
    public InspectorViewModel Inspector { get; }

    /// <summary>Total instruments known, before filtering.</summary>
    public int TotalCount => _session.Quotes.Count;

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private MonitorColumn _sortColumn = MonitorColumn.DayChange;

    [ObservableProperty] private bool _sortDescending = true;

    [ObservableProperty] private int _visibleCount;

    [ObservableProperty] private bool _gainersOnly;

    [ObservableProperty] private bool _losersOnly;

    [ObservableProperty] private bool _watchlistOnly;

    [ObservableProperty] private bool _unusualVolumeOnly;

    [ObservableProperty] private bool _signalledOnly;

    [ObservableProperty] private bool _compactDensity;

    [ObservableProperty] private bool _showSparklines;

    [ObservableProperty] private InstrumentRow? _selectedRow;

    [ObservableProperty] private SavedView? _selectedView;

    [ObservableProperty] private bool _isColumnChooserOpen;

    /// <summary>Row height for the current density.</summary>
    public double RowHeight => CompactDensity ? 28 : 36;

    /// <summary>True when a filter is hiding something, so the empty state can say so.</summary>
    public bool HasActiveFilter =>
        SearchText.Length > 0 || GainersOnly || LosersOnly || WatchlistOnly ||
        UnusualVolumeOnly || SignalledOnly ||
        CurrencyChips.Any(static c => c.IsSelected) ||
        ExchangeChips.Any(static c => c.IsSelected);

    /// <summary>True while the grid has nothing to show at all.</summary>
    public bool IsEmpty => VisibleCount == 0;

    public override void Drained(in DrainResult result)
    {
        if (_knownRowCount != _session.Quotes.Count)
        {
            _knownRowCount = _session.Quotes.Count;

            OnPropertyChanged(nameof(TotalCount));
            RebuildChips();
            ApplyWatchlist();
        }

        if (++_drainsSinceRecount < RecountEvery)
        {
            return;
        }

        _drainsSinceRecount = 0;
        VisibleCount = Rows.Count;
        OnPropertyChanged(nameof(IsEmpty));
        Recount();

        if (IsActive)
        {
            Inspector.Refresh();
        }
    }

    // ---- filters ----

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnGainersOnlyChanged(bool value)
    {
        if (value && LosersOnly)
        {
            LosersOnly = false;
        }

        RefreshFilter();
    }

    partial void OnLosersOnlyChanged(bool value)
    {
        if (value && GainersOnly)
        {
            GainersOnly = false;
        }

        RefreshFilter();
    }

    partial void OnWatchlistOnlyChanged(bool value) => RefreshFilter();

    partial void OnUnusualVolumeOnlyChanged(bool value) => RefreshFilter();

    partial void OnSignalledOnlyChanged(bool value) => RefreshFilter();

    partial void OnSortColumnChanged(MonitorColumn value) => RefreshSort();

    partial void OnSortDescendingChanged(bool value) => RefreshSort();

    partial void OnSelectedRowChanged(InstrumentRow? value) => Inspector.Show(value);

    partial void OnCompactDensityChanged(bool value)
    {
        OnPropertyChanged(nameof(RowHeight));
        _settings.CompactDensity = value;
        _persist();
    }

    partial void OnShowSparklinesChanged(bool value)
    {
        Columns.First(c => c.Id == MonitorColumns.Sparkline).IsVisible = value;
        _settings.ShowSparklines = value;
        _persist();
    }

    /// <summary>Sorts by the given column, flipping direction when it is already active.</summary>
    [RelayCommand]
    public void SortBy(MonitorColumn column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;

            // Numbers default to descending — the interesting end of "biggest mover" is
            // the top — and text defaults to ascending.
            SortDescending = column is not (MonitorColumn.Ticker or MonitorColumn.Name
                or MonitorColumn.Currency or MonitorColumn.Exchange);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        GainersOnly = false;
        LosersOnly = false;
        WatchlistOnly = false;
        UnusualVolumeOnly = false;
        SignalledOnly = false;

        foreach (FilterChip chip in CurrencyChips.Concat(ExchangeChips))
        {
            chip.IsSelected = false;
        }

        RefreshFilter();
    }

    /// <summary>Toggles a currency or exchange chip and reapplies the filter.</summary>
    [RelayCommand]
    private void ToggleChip(FilterChip? chip)
    {
        if (chip is null)
        {
            return;
        }

        chip.IsSelected = !chip.IsSelected;
        RefreshFilter();
    }

    /// <summary>Adds or removes the selected instrument from the watchlist.</summary>
    [RelayCommand]
    public void ToggleWatch(InstrumentRow? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        row.IsWatched = !row.IsWatched;

        if (row.IsWatched)
        {
            _watchlist.Add(row.Uid);
        }
        else
        {
            _watchlist.Remove(row.Uid);
        }

        _settings.Watchlist = [.. _watchlist];
        _persist();

        if (WatchlistOnly)
        {
            RefreshFilter();
        }
    }

    [RelayCommand]
    private void ToggleColumnChooser() => IsColumnChooserOpen = !IsColumnChooserOpen;

    [RelayCommand]
    private void ResetColumns()
    {
        foreach (MonitorColumnState column in Columns)
        {
            MonitorColumnState fresh = MonitorColumns.CreateDefault().First(c => c.Id == column.Id);
            column.IsVisible = fresh.IsVisible;
            column.Width = fresh.DefaultWidth;
        }

        ShowSparklines = Columns.First(c => c.Id == MonitorColumns.Sparkline).IsVisible;
    }

    // ---- saved views ----

    /// <summary>Loads the saved-view list and restores whichever one was last active.</summary>
    public void LoadViews(IEnumerable<SavedView> views, string activeName)
    {
        Views.Clear();

        foreach (SavedView view in views)
        {
            Views.Add(view);
        }

        SavedView? restore = Views.FirstOrDefault(v => v.Name == activeName) ?? Views.FirstOrDefault();
        if (restore is not null)
        {
            SelectedView = restore;
            ApplyView(restore);
        }
    }

    /// <summary>Applies a saved view's filters, sort and columns.</summary>
    [RelayCommand]
    public void ApplyView(SavedView? view)
    {
        if (view is null)
        {
            return;
        }

        SearchText = view.SearchText;
        GainersOnly = view.GainersOnly;
        LosersOnly = view.LosersOnly;
        WatchlistOnly = view.WatchlistOnly;
        UnusualVolumeOnly = view.UnusualVolumeOnly;
        SignalledOnly = view.SignalledOnly;

        foreach (FilterChip chip in CurrencyChips)
        {
            chip.IsSelected = view.Currencies.Contains(chip.Key);
        }

        foreach (FilterChip chip in ExchangeChips)
        {
            chip.IsSelected = view.Exchanges.Contains(chip.Key);
        }

        foreach (ColumnLayout layout in view.Columns)
        {
            Columns.FirstOrDefault(c => c.Id == layout.Id)?.Apply(layout);
        }

        if (view.Columns.Count > 0)
        {
            ShowSparklines = Columns.First(c => c.Id == MonitorColumns.Sparkline).IsVisible;
        }

        SortColumn = view.SortColumn;
        SortDescending = view.SortDescending;

        _settings.ActiveView = view.Name;
        _persist();

        RefreshFilter();
    }

    /// <summary>Captures the current configuration into a view object.</summary>
    public SavedView CaptureView(string name) => new()
    {
        Name = name,
        SearchText = SearchText,
        GainersOnly = GainersOnly,
        LosersOnly = LosersOnly,
        WatchlistOnly = WatchlistOnly,
        UnusualVolumeOnly = UnusualVolumeOnly,
        SignalledOnly = SignalledOnly,
        Currencies = [.. CurrencyChips.Where(c => c.IsSelected).Select(c => c.Key)],
        Exchanges = [.. ExchangeChips.Where(c => c.IsSelected).Select(c => c.Key)],
        SortColumn = SortColumn,
        SortDescending = SortDescending,
        Columns = [.. Columns.Select(c => c.ToLayout())],
    };

    /// <summary>Overwrites the selected view, or creates one when a preset is selected.</summary>
    [RelayCommand]
    private void SaveView()
    {
        string name = string.IsNullOrWhiteSpace(NewViewName)
            ? SelectedView?.Name ?? "Мой вид"
            : NewViewName.Trim();

        SavedView captured = CaptureView(name);

        SavedView? existing = Views.FirstOrDefault(v => v.Name == name && !v.IsPreset);
        if (existing is not null)
        {
            Views[Views.IndexOf(existing)] = captured;
        }
        else
        {
            Views.Add(captured);
        }

        NewViewName = string.Empty;
        SelectedView = captured;
        _settings.ActiveView = captured.Name;
        _persist();
    }

    [RelayCommand]
    private void DeleteView()
    {
        // Presets ship with the app; deleting one would leave no way back to a sane
        // starting layout.
        if (SelectedView is not { IsPreset: false } view)
        {
            return;
        }

        Views.Remove(view);
        SelectedView = Views.FirstOrDefault();
        _persist();
    }

    [ObservableProperty] private string _newViewName = string.Empty;

    /// <summary>Every saved view, for persistence.</summary>
    public IReadOnlyList<SavedView> AllViews => [.. Views];

    /// <summary>Rebuilds the filter predicate. Called after a chip is toggled in the view.</summary>
    public void ReapplyFilter() => RefreshFilter();

    // ---- internals ----

    private void RebuildChips()
    {
        Rebuild(CurrencyChips, _session.Quotes.Rows.Select(static r => r.Currency));
        Rebuild(ExchangeChips, _session.Quotes.Rows.Select(static r => r.Exchange));

        static void Rebuild(ObservableCollection<FilterChip> chips, IEnumerable<string> values)
        {
            foreach (string value in values.Where(v => !string.IsNullOrEmpty(v)).Distinct().OrderBy(v => v, StringComparer.Ordinal))
            {
                if (chips.All(c => c.Key != value))
                {
                    chips.Add(new FilterChip(value, value));
                }
            }
        }
    }

    private void ApplyWatchlist()
    {
        if (_watchlist.Count == 0)
        {
            return;
        }

        foreach (InstrumentRow row in _session.Quotes.Rows)
        {
            bool watched = _watchlist.Contains(row.Uid);
            if (row.IsWatched != watched)
            {
                row.IsWatched = watched;
            }
        }
    }

    private void Recount()
    {
        DateTimeOffset since = DateTimeOffset.Now.Date;
        IReadOnlyList<InstrumentRow> rows = _session.Quotes.Rows;

        foreach (FilterChip chip in CurrencyChips)
        {
            chip.Count = rows.Count(r => r.Currency == chip.Key);
        }

        foreach (FilterChip chip in ExchangeChips)
        {
            chip.Count = rows.Count(r => r.Exchange == chip.Key);
        }

        GainersCount = rows.Count(static r => r.DayChange > 0m);
        LosersCount = rows.Count(static r => r.DayChange < 0m);
        UnusualVolumeCount = rows.Count(static r => r.DayVolOfAvg >= UnusualVolume);
        SignalledCount = rows.Count(r => r.LastSignalAt >= since);
        WatchlistCount = _watchlist.Count;
    }

    [ObservableProperty] private int _gainersCount;

    [ObservableProperty] private int _losersCount;

    [ObservableProperty] private int _unusualVolumeCount;

    [ObservableProperty] private int _signalledCount;

    [ObservableProperty] private int _watchlistCount;

    private void RefreshFilter()
    {
        string term = SearchText.Trim();
        bool gainers = GainersOnly;
        bool losers = LosersOnly;
        bool watchlist = WatchlistOnly;
        bool unusual = UnusualVolumeOnly;
        bool signalled = SignalledOnly;
        DateTimeOffset since = DateTimeOffset.Now.Date;

        string[] currencies = [.. CurrencyChips.Where(c => c.IsSelected).Select(c => c.Key)];
        string[] exchanges = [.. ExchangeChips.Where(c => c.IsSelected).Select(c => c.Key)];

        _filter.OnNext(row =>
        {
            if (gainers && row.DayChange <= 0m)
            {
                return false;
            }

            if (losers && row.DayChange >= 0m)
            {
                return false;
            }

            if (watchlist && !row.IsWatched)
            {
                return false;
            }

            if (unusual && row.DayVolOfAvg < UnusualVolume)
            {
                return false;
            }

            if (signalled && row.LastSignalAt < since)
            {
                return false;
            }

            // Chips are OR within a category and AND across categories, which is what
            // people expect from a faceted filter and what the count badges imply.
            if (currencies.Length > 0 && Array.IndexOf(currencies, row.Currency) < 0)
            {
                return false;
            }

            if (exchanges.Length > 0 && Array.IndexOf(exchanges, row.Exchange) < 0)
            {
                return false;
            }

            if (term.Length == 0)
            {
                return true;
            }

            // Ticker and name, case-insensitive. Ordinal-ignore-case rather than a
            // culture-aware compare so results do not change with the user's locale.
            return row.Ticker.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
        });

        VisibleCount = Rows.Count;
        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void RefreshSort()
    {
        // The store needs to know which column is live so it can decide whether a delta
        // is worth a sort signal at all.
        _session.Quotes.SetSortColumn(SortColumn);
        _comparer.OnNext(InstrumentRowComparer.For(SortColumn, SortDescending));
        SyncSortIndicators();
    }

    private void SyncSortIndicators()
    {
        foreach (MonitorColumnState column in Columns)
        {
            column.SortIndicator = column.Sort == SortColumn
                ? SortDescending ? "▼" : "▲"
                : string.Empty;
        }
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _filter.Dispose();
        _comparer.Dispose();
        Inspector.Dispose();
    }
}
