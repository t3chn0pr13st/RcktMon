using CommunityToolkit.Mvvm.ComponentModel;
using RcktMon.App.Localization;
using RcktMon.Client.Core.Market;
using RcktMon.Client.Core.Settings;

namespace RcktMon.App.ViewModels;

/// <summary>
/// One grid column's user-controlled state.
/// </summary>
/// <remarks>
/// Keyed by <see cref="Id"/>, which never appears on screen. The pre-rewrite build keyed
/// <c>columns.json</c> by the literal header text, so translating a header threw away the
/// user's widths — the single reason the old layout file could not survive localisation.
/// The header itself now follows the interface language, which is why it is a subscription
/// rather than a string.
/// </remarks>
public sealed partial class MonitorColumnState : ObservableObject, IDisposable
{
    private readonly IDisposable _headerSubscription;

    public MonitorColumnState(
        string id,
        string headerKey,
        double defaultWidth,
        MonitorColumn? sort = null,
        bool canHide = true,
        bool visibleByDefault = true)
    {
        Id = id;
        HeaderKey = headerKey;
        DefaultWidth = defaultWidth;
        Sort = sort;
        CanHide = canHide;

        _width = defaultWidth;
        _isVisible = visibleByDefault;

        _headerSubscription = Localizer.Instance.Observe(headerKey).Subscribe(text =>
        {
            Header = text;
            OnPropertyChanged(nameof(DisplayHeader));
        });
    }

    /// <summary>Stable identifier used in <c>views.json</c>.</summary>
    public string Id { get; }

    /// <summary>Resource key of the header text.</summary>
    public string HeaderKey { get; }

    /// <summary>Display text, in the current interface language.</summary>
    [ObservableProperty] private string _header = string.Empty;

    /// <summary>Which sort key a click on this header selects, if any.</summary>
    public MonitorColumn? Sort { get; }

    /// <summary>False for the identity column, which must always be present.</summary>
    public bool CanHide { get; }

    public double DefaultWidth { get; }

    [ObservableProperty] private bool _isVisible;

    [ObservableProperty] private double _width;

    [ObservableProperty] private int _order;

    /// <summary>Arrow appended to the header while this column drives the sort.</summary>
    [ObservableProperty] private string _sortIndicator = string.Empty;

    /// <summary>Header text with the sort arrow, which is what the grid actually shows.</summary>
    public string DisplayHeader => SortIndicator.Length == 0 ? Header : $"{Header} {SortIndicator}";

    partial void OnSortIndicatorChanged(string value) => OnPropertyChanged(nameof(DisplayHeader));

    public ColumnLayout ToLayout() => new()
    {
        Id = Id,
        IsVisible = IsVisible,
        Width = Width,
        Order = Order,
    };

    public void Apply(ColumnLayout layout)
    {
        IsVisible = !CanHide || layout.IsVisible;
        Width = layout.Width > 20 ? layout.Width : DefaultWidth;
        Order = layout.Order;
    }

    public void Dispose() => _headerSubscription.Dispose();
}

/// <summary>
/// The monitor's column set, in the order the grid declares them.
/// </summary>
/// <remarks>
/// This list and <c>MonitorView.axaml</c>'s <c>DataGrid.Columns</c> are matched
/// positionally; the view asserts the counts agree at load time so a mismatch fails
/// immediately rather than silently mislabelling a column.
/// </remarks>
public static class MonitorColumns
{
    public const string Instrument = "instrument";
    public const string Price = "price";
    public const string Change = "change";
    public const string Sparkline = "sparkline";
    public const string Volume = "volume";
    public const string VolumeOfAverage = "volavg";
    public const string Turnover = "turnover";
    public const string Bid = "bid";
    public const string Ask = "ask";
    public const string Currency = "currency";
    public const string Exchange = "exchange";
    public const string Sector = "sector";
    public const string Status = "status";
    public const string LastTrade = "lasttrade";

    /// <summary>Fresh column state, in declaration order.</summary>
    public static List<MonitorColumnState> CreateDefault() =>
    [
        new(Instrument, "Col_Instrument", 230, MonitorColumn.Ticker, canHide: false),
        new(Price, "Col_Price", 108, MonitorColumn.Price),
        new(Change, "Col_Change", 116, MonitorColumn.DayChange),

        // Off by default: a templated cell with custom drawing is by far the most
        // expensive thing in a virtualised row, and most users never need it.
        new(Sparkline, "Col_Sparkline", 116, sort: null, visibleByDefault: false),

        new(Volume, "Col_Volume", 104, MonitorColumn.DayVolume),
        new(VolumeOfAverage, "Col_VolumeOfAverage", 130, MonitorColumn.DayVolOfAvg),
        new(Turnover, "Col_Turnover", 110, MonitorColumn.DayVolumeCost, visibleByDefault: false),
        new(Bid, "Col_Bid", 96, MonitorColumn.BestBid, visibleByDefault: false),
        new(Ask, "Col_Ask", 96, MonitorColumn.BestAsk, visibleByDefault: false),
        new(Currency, "Col_Currency", 118, MonitorColumn.Currency),
        new(Exchange, "Col_Exchange", 110, MonitorColumn.Exchange),
        new(Sector, "Col_Sector", 150, sort: null, visibleByDefault: false),
        new(Status, "Col_Status", 130, MonitorColumn.Status, visibleByDefault: false),
        new(LastTrade, "Col_LastTrade", 100, MonitorColumn.LastTrade, visibleByDefault: false),
    ];
}
