using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcktMon.App.Formatting;
using RcktMon.Client.Core.Market;
using RcktMon.Protocol.V1;

namespace RcktMon.App.ViewModels;

/// <summary>A sticky date header in the feed.</summary>
public sealed record SignalGroupHeader(string Title, int Count);

/// <summary>How far back the feed shows.</summary>
public enum SignalRange
{
    Today,
    TwoDays,
    All,
}

/// <summary>
/// The alert feed: rich cards, grouped by day, filterable, and backed by real history.
/// </summary>
/// <remarks>
/// A list of cards rather than a grid. A signal is a sentence — "SBER +7.42% за 5 мин,
/// 312.40 → 335.60" — and a grid row forces the reader to reassemble that sentence from
/// eight columns every time. The pre-rewrite app had neither: alerts lived in an unbounded
/// in-memory set that died with the process, so there was nothing to scroll back through.
/// </remarks>
public sealed partial class SignalsViewModel : PageViewModel
{
    /// <summary>Drains between rebuild checks.</summary>
    private const int RefreshEvery = 5;

    private readonly ClientSession _session;
    private readonly Action<string> _navigateToInstrument;

    private int _drains;
    private (int Count, long TopId, int Signature) _rendered = (-1, -1, 0);

    public SignalsViewModel(ClientSession session, Action<string> navigateToInstrument)
        : base("signals", "Сигналы", "Icon.Signals")
    {
        _session = session;
        _navigateToInstrument = navigateToInstrument;

        Kinds =
        [
            new FilterChip(nameof(SignalKind.DayChange), Humanize.SignalKindName(SignalKind.DayChange)),
            new FilterChip(nameof(SignalKind.MinutesPriceChange), Humanize.SignalKindName(SignalKind.MinutesPriceChange)),
            new FilterChip(nameof(SignalKind.MinutesVolumeChange), Humanize.SignalKindName(SignalKind.MinutesVolumeChange)),
        ];
    }

    /// <summary>Headers and signals interleaved, ready to bind to a virtualised list.</summary>
    public ObservableCollection<object> Items { get; } = [];

    /// <summary>Kind filter chips.</summary>
    public ObservableCollection<FilterChip> Kinds { get; }

    /// <summary>Currency filter chips, built from what the feed actually contains.</summary>
    public ObservableCollection<FilterChip> Currencies { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private SignalRange _range = SignalRange.TwoDays;

    /// <summary>
    /// When off, the feed stops rebuilding so a card can be read without it jumping.
    /// </summary>
    [ObservableProperty] private bool _isLive = true;

    [ObservableProperty] private int _matchCount;

    [ObservableProperty] private int _todayCount;

    [ObservableProperty] private bool _isEmpty = true;

    /// <summary>True when the feed is empty only because of the filters.</summary>
    public bool IsFilteredEmpty => IsEmpty && HasActiveFilter;

    public bool HasActiveFilter =>
        SearchText.Length > 0 ||
        Range != SignalRange.All ||
        Kinds.Any(static k => k.IsSelected) ||
        Currencies.Any(static c => c.IsSelected);

    public override void Drained(in DrainResult result)
    {
        if (result.NewSignals.Count > 0)
        {
            Badge = _session.Signals.CountFrom(DateTimeOffset.Now.Date);
        }

        if (++_drains < RefreshEvery)
        {
            return;
        }

        _drains = 0;
        Rebuild();
    }

    partial void OnSearchTextChanged(string value) => ForceRebuild();

    partial void OnRangeChanged(SignalRange value) => ForceRebuild();

    partial void OnIsLiveChanged(bool value)
    {
        if (value)
        {
            ForceRebuild();
        }
    }

    [RelayCommand]
    private void ToggleKind(FilterChip? chip)
    {
        if (chip is null)
        {
            return;
        }

        chip.IsSelected = !chip.IsSelected;
        ForceRebuild();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        Range = SignalRange.All;

        foreach (FilterChip chip in Kinds.Concat(Currencies))
        {
            chip.IsSelected = false;
        }

        ForceRebuild();
    }

    /// <summary>Opens the instrument behind a signal in the monitor.</summary>
    [RelayCommand]
    private void OpenInstrument(SignalEntry? signal)
    {
        if (signal is not null)
        {
            _navigateToInstrument(signal.InstrumentUid);
        }
    }

    /// <summary>Recomputes the feed after a filter checkbox changed in the view.</summary>
    public void ReapplyFilters() => ForceRebuild();

    private void ForceRebuild()
    {
        _rendered = (-1, -1, 0);
        OnPropertyChanged(nameof(HasActiveFilter));
        Rebuild();
    }

    private void Rebuild()
    {
        if (!IsLive && _rendered.Count >= 0)
        {
            // Frozen for reading. New signals still accumulate in the store and appear
            // the moment Live is turned back on.
            return;
        }

        IReadOnlyList<SignalEntry> all = _session.Signals.All();

        RebuildCurrencyChips(all);

        DateTimeOffset today = DateTimeOffset.Now.Date;
        TodayCount = all.Count(s => s.FiredAt >= today);
        Badge = TodayCount;

        DateTimeOffset from = Range switch
        {
            SignalRange.Today => today,
            SignalRange.TwoDays => today.AddDays(-1),
            _ => DateTimeOffset.MinValue,
        };

        HashSet<SignalKind> kinds = [.. Kinds.Where(k => k.IsSelected).Select(k => Enum.Parse<SignalKind>(k.Key))];
        HashSet<string> currencies = [.. Currencies.Where(c => c.IsSelected).Select(c => c.Key)];
        string term = SearchText.Trim();

        List<SignalEntry> matched = all
            .Where(s => s.FiredAt >= from)
            .Where(s => kinds.Count == 0 || kinds.Contains(s.Kind))
            .Where(s => currencies.Count == 0 || currencies.Contains(s.Currency))
            .Where(s => term.Length == 0 ||
                        s.Ticker.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        s.Headline.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Rebuilding a 2000-item list twice a second would thrash the virtualiser for no
        // reason, so it only happens when the result actually differs.
        var signature = (matched.Count, matched.Count > 0 ? matched[0].Id : 0L, HashCode.Combine(from, term, kinds.Count, currencies.Count));
        if (signature == _rendered)
        {
            return;
        }

        _rendered = signature;
        MatchCount = matched.Count;

        Items.Clear();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string? group = null;

        foreach (SignalEntry signal in matched)
        {
            string next = Humanize.DayGroup(signal.FiredAt, now);
            if (next != group)
            {
                group = next;
                Items.Add(new SignalGroupHeader(next, matched.Count(s => Humanize.DayGroup(s.FiredAt, now) == next)));
            }

            Items.Add(signal);
        }

        IsEmpty = matched.Count == 0;
        OnPropertyChanged(nameof(IsFilteredEmpty));
    }

    private void RebuildCurrencyChips(IReadOnlyList<SignalEntry> signals)
    {
        foreach (string currency in signals
                     .Select(static s => s.Currency)
                     .Where(static c => !string.IsNullOrEmpty(c))
                     .Distinct()
                     .OrderBy(static c => c, StringComparer.Ordinal))
        {
            if (Currencies.All(c => c.Key != currency))
            {
                Currencies.Add(new FilterChip(currency, currency));
            }
        }
    }
}
