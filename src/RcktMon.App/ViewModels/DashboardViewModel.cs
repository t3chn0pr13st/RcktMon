using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcktMon.App.Formatting;
using RcktMon.Client.Core.Market;

namespace RcktMon.App.ViewModels;

/// <summary>
/// The landing screen: what is happening right now, in five numbers and eight cards.
/// </summary>
/// <remarks>
/// New in the rewrite. The WPF build opened straight into a 21-column grid of 2000 rows,
/// which answers "what is every instrument doing" — a question nobody actually has. This
/// answers "is anything happening", which is the reason the app is open at all.
/// </remarks>
public sealed partial class DashboardViewModel : PageViewModel
{
    /// <summary>How many mover cards the board shows.</summary>
    private const int RocketCount = 8;

    /// <summary>How many signals appear in the strip along the bottom.</summary>
    private const int StripCount = 10;

    /// <summary>Drains between refreshes. The board is a summary; twice a second is plenty.</summary>
    private const int RefreshEvery = 5;

    private readonly ClientSession _session;
    private readonly Action<string> _navigate;
    private readonly Action<InstrumentRow> _inspect;

    private int _drains;

    public DashboardViewModel(ClientSession session, Action<string> navigate, Action<InstrumentRow> inspect)
        : base("dashboard", "Обзор", "Icon.Dashboard")
    {
        _session = session;
        _navigate = navigate;
        _inspect = inspect;
    }

    /// <summary>Top movers by absolute day change, biggest first.</summary>
    public ObservableCollection<InstrumentRow> Rockets { get; } = [];

    /// <summary>The most recent signals, newest first.</summary>
    public ObservableCollection<SignalEntry> RecentSignals { get; } = [];

    [ObservableProperty] private int _signalsToday;

    [ObservableProperty] private InstrumentRow? _topGainer;

    [ObservableProperty] private InstrumentRow? _topLoser;

    [ObservableProperty] private InstrumentRow? _mostActive;

    [ObservableProperty] private string _sessionPhase = "Нет данных";

    [ObservableProperty] private string _sessionDetail = string.Empty;

    [ObservableProperty] private bool _isTrading;

    [ObservableProperty] private bool _hasData;

    /// <summary>True when the app has connected but the exchange is shut.</summary>
    [ObservableProperty] private bool _isMarketClosed;

    public override void Drained(in DrainResult result)
    {
        if (++_drains < RefreshEvery)
        {
            return;
        }

        _drains = 0;
        Refresh();
    }

    /// <summary>Recomputes every tile. Cheap enough to run twice a second at 2000 rows.</summary>
    public void Refresh()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        SignalsToday = _session.Signals.CountFrom(DateTimeOffset.Now.Date);

        IReadOnlyList<InstrumentRow> rows = _session.Quotes.Rows;
        HasData = rows.Count > 0;

        if (rows.Count > 0)
        {
            TopGainer = rows.MaxBy(static r => r.DayChange);
            TopLoser = rows.MinBy(static r => r.DayChange);
            MostActive = rows.MaxBy(static r => r.DayVolumeCost);

            Replace(Rockets, rows
                .Where(static r => r.DayChange != 0m)
                .OrderByDescending(static r => Math.Abs(r.DayChange))
                .Take(RocketCount));
        }

        Replace(RecentSignals, _session.Signals.Recent(StripCount));

        UpdateSession(now);
    }

    private void UpdateSession(DateTimeOffset now)
    {
        MarketPhase phase = _session.MarketStatus.Phase("MOEX", now);

        SessionPhase = Humanize.Phase(phase.Kind);
        IsTrading = phase.IsTrading;
        IsMarketClosed = phase.Kind is MarketPhaseKind.Closed && HasData;

        SessionDetail = phase switch
        {
            { NextChangeAt: { } next } =>
                $"{(phase.NextChangeOpens ? "Открытие" : "Закрытие")} {next.ToLocalTime():HH:mm} · {Humanize.Countdown(next - now)}",
            { Kind: MarketPhaseKind.Unknown } => "Расписание не получено",
            { IsTradingDay: false } => "Сегодня биржа не работает",
            _ => "Сегодня торгов больше не будет",
        };
    }

    /// <summary>Opens the monitor with this instrument selected.</summary>
    [RelayCommand]
    private void Open(InstrumentRow? row)
    {
        if (row is null)
        {
            return;
        }

        _inspect(row);
        _navigate("monitor");
    }

    /// <summary>Opens the signal feed.</summary>
    [RelayCommand]
    private void OpenSignals() => _navigate("signals");

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        // Rebuilt wholesale rather than diffed: these are at most ten items and the cards
        // are stateless, so a diff would cost more to maintain than it saves.
        target.Clear();

        foreach (T item in source)
        {
            target.Add(item);
        }
    }
}
