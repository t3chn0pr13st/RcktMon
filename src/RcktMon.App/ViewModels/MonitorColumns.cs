using CommunityToolkit.Mvvm.ComponentModel;
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
/// </remarks>
public sealed partial class MonitorColumnState : ObservableObject
{
    public MonitorColumnState(
        string id,
        string header,
        double defaultWidth,
        MonitorColumn? sort = null,
        bool canHide = true,
        bool visibleByDefault = true)
    {
        Id = id;
        Header = header;
        DefaultWidth = defaultWidth;
        Sort = sort;
        CanHide = canHide;

        _width = defaultWidth;
        _isVisible = visibleByDefault;
    }

    /// <summary>Stable identifier used in <c>views.json</c>.</summary>
    public string Id { get; }

    /// <summary>Display text.</summary>
    public string Header { get; }

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
        new(Instrument, "Инструмент", 230, MonitorColumn.Ticker, canHide: false),
        new(Price, "Цена", 108, MonitorColumn.Price),
        new(Change, "Изм.", 116, MonitorColumn.DayChange),

        // Off by default: a templated cell with custom drawing is by far the most
        // expensive thing in a virtualised row, and most users never need it.
        new(Sparkline, "График", 116, sort: null, visibleByDefault: false),

        new(Volume, "Объём", 104, MonitorColumn.DayVolume),
        new(VolumeOfAverage, "× среднего", 130, MonitorColumn.DayVolOfAvg),
        new(Turnover, "Оборот", 110, MonitorColumn.DayVolumeCost, visibleByDefault: false),
        new(Bid, "Bid", 96, MonitorColumn.BestBid, visibleByDefault: false),
        new(Ask, "Ask", 96, MonitorColumn.BestAsk, visibleByDefault: false),
        new(Currency, "Валюта", 100, MonitorColumn.Currency),
        new(Exchange, "Биржа", 110, MonitorColumn.Exchange),
        new(Sector, "Сектор", 150, sort: null, visibleByDefault: false),
        new(Status, "Статус", 130, MonitorColumn.Status, visibleByDefault: false),
        new(LastTrade, "Сделка", 100, MonitorColumn.LastTrade, visibleByDefault: false),
    ];
}
