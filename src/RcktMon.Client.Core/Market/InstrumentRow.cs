using System.Reactive;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.ComponentModel;
using RcktMon.Protocol.V1;

namespace RcktMon.Client.Core.Market;

/// <summary>
/// One instrument in the monitor grid. Created once and mutated in place forever.
/// </summary>
/// <remarks>
/// <para>
/// Identity is <see cref="Uid"/> and never changes, which is what lets the grid keep its
/// containers, selection and scroll position while thousands of values tick underneath.
/// The pre-rewrite UI instead raised <c>PropertyChanged</c> on the whole dictionary to
/// force a rebind, rebuilding every row on every refresh.
/// </para>
/// <para>
/// Row updates deliberately do <em>not</em> go through <c>INotifyPropertyChanged</c>
/// subscriptions for sorting. See <see cref="SortKeyChanged"/>.
/// </para>
/// </remarks>
public sealed partial class InstrumentRow : ObservableObject
{
    private readonly Subject<Unit> _sortKeyChanged = new();

    public InstrumentRow(string uid, string ticker, string name)
    {
        Uid = uid;
        _ticker = ticker;
        _name = name;
    }

    /// <summary>T-Invest instrument uid. The join key across the whole system.</summary>
    public string Uid { get; }

    /// <summary>
    /// Fires only when the value of the <em>currently active</em> sort column changed.
    /// </summary>
    /// <remarks>
    /// This is the load-bearing optimisation of the monitor grid. The obvious approach —
    /// <c>AutoRefresh(x =&gt; x.DayChange)</c> — attaches an <c>INotifyPropertyChanged</c>
    /// handler per row and re-evaluates on every property change: at 2000 instruments,
    /// four changed fields per update and two updates a second, that is on the order of
    /// 16 000 refresh evaluations per second, nearly all of which cannot affect order.
    /// <see cref="QuoteStore"/> instead compares the active sort value before and after
    /// applying a delta and pushes here only on a real change.
    /// </remarks>
    public IObservable<Unit> SortKeyChanged => _sortKeyChanged;

    // ---- static identity, from the catalog ----

    [ObservableProperty] private string _ticker;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _currency = string.Empty;
    [ObservableProperty] private string _exchange = string.Empty;
    [ObservableProperty] private string _classCode = string.Empty;
    [ObservableProperty] private string _sector = string.Empty;
    [ObservableProperty] private string? _logoUrl;
    [ObservableProperty] private int _lot = 1;
    [ObservableProperty] private InstrumentClass _instrumentClass;
    [ObservableProperty] private bool _shortEnabled;

    // ---- live quote ----

    [ObservableProperty] private decimal _price;
    [ObservableProperty] private decimal _dayOpen;
    [ObservableProperty] private decimal _prevClose;

    /// <summary>Day change as a fraction: 0.0742 means +7.42%.</summary>
    [ObservableProperty] private decimal _dayChange;

    [ObservableProperty] private long _dayVolume;
    [ObservableProperty] private decimal _dayVolumeCost;
    [ObservableProperty] private decimal _bestBid;
    [ObservableProperty] private decimal _bestAsk;

    /// <summary>Day volume as a multiple of the monthly average. The "unusual volume" measure.</summary>
    [ObservableProperty] private decimal _dayVolOfAvg;

    [ObservableProperty] private TradingStatus _status;
    [ObservableProperty] private DateTimeOffset? _lastTradeTime;

    /// <summary>Last 60 one-minute closes as basis points relative to <see cref="DayOpen"/>.</summary>
    [ObservableProperty] private int[] _sparkline = [];

    // ---- derived UI state ----

    /// <summary>
    /// Sign of the most recent price change, for the cell flash. Reset by the view once
    /// the animation has run; the view is also responsible for throttling it, since an
    /// unthrottled flash on a fast mover reads as a strobe rather than as information.
    /// </summary>
    [ObservableProperty] private int _lastTickDirection;

    /// <summary>When this instrument last produced a signal, for the row accent bar.</summary>
    [ObservableProperty] private DateTimeOffset? _lastSignalAt;

    /// <summary>Set when the server reports its data for this instrument as stale.</summary>
    [ObservableProperty] private bool _isStale;

    /// <summary>True while the instrument is not actually tradable right now.</summary>
    public bool IsInactive =>
        Status is TradingStatus.Closed
            or TradingStatus.Halted
            or TradingStatus.NotAvailable
            or TradingStatus.Unspecified;

    partial void OnStatusChanged(TradingStatus value) => OnPropertyChanged(nameof(IsInactive));

    /// <summary>Reads the value the given column sorts by. Non-numeric columns return null.</summary>
    /// <remarks>
    /// Ticker, name, currency and exchange are fixed for the life of a session, so a
    /// sort on one of them can never be invalidated by a quote update and needs no
    /// change signal at all.
    /// </remarks>
    public decimal? GetNumericSortValue(MonitorColumn column) => column switch
    {
        MonitorColumn.Price => Price,
        MonitorColumn.DayChange => DayChange,
        MonitorColumn.DayVolume => DayVolume,
        MonitorColumn.DayVolumeCost => DayVolumeCost,
        MonitorColumn.DayVolOfAvg => DayVolOfAvg,
        MonitorColumn.BestBid => BestBid,
        MonitorColumn.BestAsk => BestAsk,
        MonitorColumn.DayOpen => DayOpen,
        MonitorColumn.Status => (int)Status,
        MonitorColumn.LastTrade => LastTradeTime?.ToUnixTimeMilliseconds() ?? 0m,
        _ => null,
    };

    internal void RaiseSortKeyChanged() => _sortKeyChanged.OnNext(Unit.Default);
}

/// <summary>Sortable columns of the monitor grid.</summary>
public enum MonitorColumn
{
    Ticker,
    Name,
    Currency,
    Exchange,
    Price,
    DayOpen,
    DayChange,
    DayVolume,
    DayVolumeCost,
    DayVolOfAvg,
    BestBid,
    BestAsk,
    Status,
    LastTrade,
}

/// <summary>Builds comparers for <see cref="MonitorColumn"/> in either direction.</summary>
public static class InstrumentRowComparer
{
    public static IComparer<InstrumentRow> For(MonitorColumn column, bool descending)
    {
        IComparer<InstrumentRow> comparer = column switch
        {
            MonitorColumn.Ticker => Text(static r => r.Ticker),
            MonitorColumn.Name => Text(static r => r.Name),
            MonitorColumn.Currency => Text(static r => r.Currency),
            MonitorColumn.Exchange => Text(static r => r.Exchange),
            _ => Numeric(column),
        };

        return descending ? new ReverseComparer(comparer) : comparer;
    }

    private static IComparer<InstrumentRow> Text(Func<InstrumentRow, string> selector) =>
        Comparer<InstrumentRow>.Create((a, b) =>
        {
            // Ordinal, not culture-aware: the grid must produce the same order for every
            // user regardless of locale, and tickers are ASCII by construction.
            int primary = string.CompareOrdinal(selector(a), selector(b));
            return primary != 0 ? primary : string.CompareOrdinal(a.Uid, b.Uid);
        });

    private static IComparer<InstrumentRow> Numeric(MonitorColumn column) =>
        Comparer<InstrumentRow>.Create((a, b) =>
        {
            decimal av = a.GetNumericSortValue(column) ?? 0m;
            decimal bv = b.GetNumericSortValue(column) ?? 0m;

            int primary = av.CompareTo(bv);

            // Ties broken by uid so the order is total and stable. Without this, rows with
            // equal values (very common: every untraded instrument has DayChange 0) can
            // swap places between sorts and visibly jitter.
            return primary != 0 ? primary : string.CompareOrdinal(a.Uid, b.Uid);
        });

    private sealed class ReverseComparer(IComparer<InstrumentRow> inner) : IComparer<InstrumentRow>
    {
        public int Compare(InstrumentRow? x, InstrumentRow? y) => inner.Compare(y, x);
    }
}
