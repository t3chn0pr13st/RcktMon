using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using RcktMon.App.Threading;
using RcktMon.Client.Core.Market;
using RcktMon.Protocol.V1;

namespace RcktMon.App.ViewModels;

/// <summary>One exchange's session window, shown in both the user's zone and Moscow time.</summary>
/// <param name="Exchange">Exchange code.</param>
/// <param name="Phase">Human-readable phase.</param>
/// <param name="LocalWindow">Session window in the user's local time.</param>
/// <param name="MoscowWindow">The same window in Moscow time.</param>
/// <param name="IsTradingDay">False on weekends and holidays.</param>
public readonly record struct ExchangeSessionRow(
    string Exchange,
    string Phase,
    string LocalWindow,
    string MoscowWindow,
    bool IsTradingDay);

/// <summary>
/// The instrument universe and the state of the data behind it.
/// </summary>
/// <remarks>
/// The old "Статус инструментов" grid, with two additions that matter for a hosted
/// product: the session window in <em>both</em> the user's time zone and Moscow's — the
/// pre-rewrite build hardcoded a UTC+3 offset and was wrong for everyone outside Moscow —
/// and the server's own health, so a user can tell whether a flat price is the market or
/// the pipeline.
/// </remarks>
public sealed partial class InstrumentsViewModel : PageViewModel, IDisposable
{
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    private const int RefreshEvery = 10;

    private readonly ClientSession _session;
    private readonly BehaviorSubject<Func<InstrumentRow, bool>> _filter;
    private readonly IDisposable _pipeline;

    private int _drains;

    public InstrumentsViewModel(ClientSession session)
        : base("instruments", "Инструменты", "Icon.Instruments")
    {
        _session = session;
        _filter = new BehaviorSubject<Func<InstrumentRow, bool>>(static _ => true);

        _pipeline = session.Quotes.Connect()
            .Filter(_filter)
            .ObserveOn(UiThreadScheduler.Instance)

            // Sorted by ticker, which never changes, so unlike the monitor grid this
            // pipeline needs no refresh signal and no re-sort budget at all.
            .SortAndBind(
                out ReadOnlyObservableCollection<InstrumentRow> rows,
                InstrumentRowComparer.For(MonitorColumn.Ticker, descending: false),
                new SortAndBindOptions { UseBinarySearch = true, ResetThreshold = 64 })
            .Subscribe();

        Rows = rows;
    }

    public ReadOnlyObservableCollection<InstrumentRow> Rows { get; }

    /// <summary>One row per exchange the server reported.</summary>
    public ObservableCollection<ExchangeSessionRow> Sessions { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private bool _tradableOnly;

    [ObservableProperty] private bool _shortableOnly;

    [ObservableProperty] private int _visibleCount;

    [ObservableProperty] private int _subscribedCount;

    [ObservableProperty] private int _shardsHealthy;

    [ObservableProperty] private int _shardsTotal;

    [ObservableProperty] private string _healthDetail = "Ожидание статуса сервера…";

    [ObservableProperty] private bool _isDegraded;

    public override void Drained(in DrainResult result)
    {
        if (result.StatusChanged)
        {
            RefreshHealth();
        }

        if (++_drains < RefreshEvery)
        {
            return;
        }

        _drains = 0;
        VisibleCount = Rows.Count;
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnTradableOnlyChanged(bool value) => RefreshFilter();

    partial void OnShortableOnlyChanged(bool value) => RefreshFilter();

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        TradableOnly = false;
        ShortableOnly = false;
    }

    private void RefreshHealth()
    {
        MarketDataHealth? health = _session.MarketStatus.Latest?.MarketData;
        if (health is not null)
        {
            ShardsHealthy = health.ShardsHealthy;
            ShardsTotal = health.ShardsTotal;
            SubscribedCount = health.InstrumentsSubscribed;
            IsDegraded = health.Degraded;

            HealthDetail = health.Degraded
                ? health.DegradedReason
                : $"Все шарды в норме · последнее событие {health.LastMarketEvent?.ToDateTimeOffset().ToLocalTime():HH:mm:ss}";
        }

        Sessions.Clear();

        foreach (MarketPhase phase in _session.MarketStatus.Phases(DateTimeOffset.UtcNow))
        {
            ExchangeSession? raw = _session.MarketStatus.Latest?.Sessions
                .FirstOrDefault(s => s.Exchange == phase.Exchange);

            Sessions.Add(new ExchangeSessionRow(
                phase.Exchange,
                Formatting.Humanize.Phase(phase.Kind),
                Window(raw, local: true),
                Window(raw, local: false),
                raw?.IsTradingDay ?? false));
        }
    }

    private static string Window(ExchangeSession? session, bool local)
    {
        if (session?.OpensAt is null || session.ClosesAt is null)
        {
            return "—";
        }

        string main = $"{Format(session.OpensAt.ToDateTimeOffset(), local)}–{Format(session.ClosesAt.ToDateTimeOffset(), local)}";

        if (session.EveningOpensAt is null || session.EveningClosesAt is null)
        {
            return main;
        }

        return $"{main}, {Format(session.EveningOpensAt.ToDateTimeOffset(), local)}–" +
               $"{Format(session.EveningClosesAt.ToDateTimeOffset(), local)}";
    }

    private static string Format(DateTimeOffset instant, bool local) =>
        (local ? instant.ToLocalTime() : instant.ToOffset(MoscowOffset))
        .ToString("HH:mm", CultureInfo.InvariantCulture);

    private void RefreshFilter()
    {
        string term = SearchText.Trim();
        bool tradable = TradableOnly;
        bool shortable = ShortableOnly;

        _filter.OnNext(row =>
        {
            if (tradable && row.IsInactive)
            {
                return false;
            }

            if (shortable && !row.ShortEnabled)
            {
                return false;
            }

            return term.Length == 0
                || row.Ticker.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.Isin.Contains(term, StringComparison.OrdinalIgnoreCase);
        });

        VisibleCount = Rows.Count;
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _filter.Dispose();
    }
}
