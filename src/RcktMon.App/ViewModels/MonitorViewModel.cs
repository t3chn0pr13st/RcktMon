using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Threading;
using RcktMon.App.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using RcktMon.Client.Core.Market;

namespace RcktMon.App.ViewModels;

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
/// The shape below is load-bearing; see the comments on each stage.
/// </para>
/// </remarks>
public sealed partial class MonitorViewModel : ObservableObject, IDisposable
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

    /// <summary>How often queued quote messages are applied on the UI thread.</summary>
    private static readonly TimeSpan DrainInterval = TimeSpan.FromMilliseconds(100);

    private readonly QuoteStore _store;
    private readonly BehaviorSubject<Func<InstrumentRow, bool>> _filter;
    private readonly BehaviorSubject<IComparer<InstrumentRow>> _comparer;
    private readonly IDisposable _pipeline;
    private readonly DispatcherTimer _drainTimer;

    public MonitorViewModel(QuoteStore store)
    {
        _store = store;

        _filter = new BehaviorSubject<Func<InstrumentRow, bool>>(static _ => true);
        _comparer = new BehaviorSubject<IComparer<InstrumentRow>>(
            InstrumentRowComparer.For(MonitorColumn.DayChange, descending: true));

        _store.SetSortColumn(MonitorColumn.DayChange);

        _pipeline = _store.Connect()
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

        // One dispatcher hop per interval rather than one per changed field. Quote
        // messages arrive on the gRPC reader thread and are queued; this applies the
        // whole backlog in a single pass.
        _drainTimer = new DispatcherTimer(DrainInterval, DispatcherPriority.Background, (_, _) => Drain());
        _drainTimer.Start();
    }

    /// <summary>Sorted, filtered rows bound directly to the grid.</summary>
    public ReadOnlyObservableCollection<InstrumentRow> Rows { get; }

    /// <summary>Total instruments known, before filtering.</summary>
    public int TotalCount => _store.Count;

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private MonitorColumn _sortColumn = MonitorColumn.DayChange;

    [ObservableProperty] private bool _sortDescending = true;

    [ObservableProperty] private int _visibleCount;

    [ObservableProperty] private bool _gainersOnly;

    [ObservableProperty] private bool _losersOnly;

    [ObservableProperty] private string? _currencyFilter;

    /// <summary>Applied quote messages in the last drain, for the status bar.</summary>
    [ObservableProperty] private int _lastDrainSize;

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

    partial void OnCurrencyFilterChanged(string? value) => RefreshFilter();

    partial void OnSortColumnChanged(MonitorColumn value) => RefreshSort();

    partial void OnSortDescendingChanged(bool value) => RefreshSort();

    /// <summary>Sorts by the given column, flipping direction when it is already active.</summary>
    [RelayCommand]
    private void SortBy(MonitorColumn column)
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
        CurrencyFilter = null;
    }

    private void Drain()
    {
        LastDrainSize = _store.Drain();

        if (LastDrainSize > 0)
        {
            OnPropertyChanged(nameof(TotalCount));
            VisibleCount = Rows.Count;
        }
    }

    private void RefreshFilter()
    {
        string term = SearchText.Trim();
        bool gainers = GainersOnly;
        bool losers = LosersOnly;
        string? currency = CurrencyFilter;

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

            if (currency is not null &&
                !string.Equals(row.Currency, currency, StringComparison.OrdinalIgnoreCase))
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
    }

    private void RefreshSort()
    {
        // The store needs to know which column is live so it can decide whether a delta
        // is worth a sort signal at all.
        _store.SetSortColumn(SortColumn);
        _comparer.OnNext(InstrumentRowComparer.For(SortColumn, SortDescending));
    }

    public void Dispose()
    {
        _drainTimer.Stop();
        _pipeline.Dispose();
        _filter.Dispose();
        _comparer.Dispose();
    }
}
