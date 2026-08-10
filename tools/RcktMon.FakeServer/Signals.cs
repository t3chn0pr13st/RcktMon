using Google.Protobuf.WellKnownTypes;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;

namespace RcktMon.FakeServer;

/// <summary>
/// Every signal this run has produced, live and seeded.
/// </summary>
/// <remarks>
/// The real server keeps signal history in Postgres; here it is a bounded in-memory list.
/// What matters for the client is that <c>ListSignals</c> returns something older than the
/// current session, because "scroll back to yesterday" is a screen that cannot be built
/// against a feed that starts empty on every launch.
/// </remarks>
public sealed class SignalLog
{
    /// <summary>Upper bound on retained signals. Old ones fall off the front.</summary>
    private const int Capacity = 5000;

    private readonly Lock _gate = new();
    private readonly List<Signal> _signals = new(Capacity);

    private long _nextId = 1;

    /// <summary>Raised for each newly fired signal, on the market clock's thread.</summary>
    public event Action<Signal>? Fired;

    /// <summary>Appends a signal, assigning it the next id, and notifies subscribers.</summary>
    public Signal Append(Signal signal)
    {
        Signal stored;

        lock (_gate)
        {
            signal.Id = _nextId++;
            _signals.Add(signal);

            if (_signals.Count > Capacity)
            {
                _signals.RemoveRange(0, _signals.Count - Capacity);
            }

            stored = signal;
        }

        Fired?.Invoke(stored);
        return stored;
    }

    /// <summary>Signals newer than <paramref name="sinceId"/>, oldest first.</summary>
    public IReadOnlyList<Signal> Since(long sinceId, IReadOnlyCollection<SignalKind> kinds)
    {
        lock (_gate)
        {
            return _signals
                .Where(s => s.Id > sinceId && Matches(s, kinds))
                .ToList();
        }
    }

    /// <summary>The most recent signals, newest first.</summary>
    public IReadOnlyList<Signal> Recent(int count)
    {
        lock (_gate)
        {
            return _signals
                .Skip(Math.Max(0, _signals.Count - count))
                .Reverse()
                .ToList();
        }
    }

    /// <summary>How many signals fired at or after <paramref name="from"/>.</summary>
    public int CountFrom(DateTimeOffset from)
    {
        lock (_gate)
        {
            return _signals.Count(s => s.FiredAt is not null && s.FiredAt.ToDateTimeOffset() >= from);
        }
    }

    /// <summary>Paged history, newest first, matching every supplied filter.</summary>
    public (IReadOnlyList<Signal> Items, string NextPageToken, int Total) Query(ListSignalsRequest request)
    {
        int pageSize = request.PageSize <= 0 ? 100 : Math.Clamp(request.PageSize, 1, 500);
        int offset = int.TryParse(request.PageToken, out int parsed) ? Math.Max(0, parsed) : 0;

        DateTimeOffset? from = request.From?.ToDateTimeOffset();
        DateTimeOffset? to = request.To?.ToDateTimeOffset();

        lock (_gate)
        {
            List<Signal> matched = _signals
                .Where(s => Matches(s, request.Kinds))
                .Where(s => request.InstrumentUids.Count == 0 || request.InstrumentUids.Contains(s.InstrumentUid))
                .Where(s => request.Currencies.Count == 0 || request.Currencies.Contains(s.Currency))
                .Where(s => Within(s, from, to))
                .OrderByDescending(s => s.Id)
                .ToList();

            List<Signal> page = matched.Skip(offset).Take(pageSize).ToList();
            int consumed = offset + page.Count;

            return (
                page,
                consumed < matched.Count ? consumed.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
                matched.Count);
        }
    }

    /// <summary>
    /// Fills the log with plausible history so the feed, the dashboard strip and the
    /// date-range filters all have something to show the moment the client connects.
    /// </summary>
    public void Seed(SyntheticMarket market, int count, int seed, int windowMinutes)
    {
        if (count <= 0)
        {
            return;
        }

        var random = new Random(seed);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Oldest first, so the ids the client sees increase with time exactly as they
        // would on a real server.
        var history = new List<Signal>(count);

        for (int i = count; i > 0; i--)
        {
            Instrument instrument = market.Sample(random);

            // Spread over the last two days, denser towards now — the shape a trading
            // session actually produces, and what makes the "Today / Yesterday / Earlier"
            // group headers meaningful.
            double hoursAgo = Math.Pow(random.NextDouble(), 1.8) * 46;
            DateTimeOffset firedAt = now - TimeSpan.FromHours(hoursAgo);

            SignalKind kind = random.Next(3) switch
            {
                0 => SignalKind.DayChange,
                1 => SignalKind.MinutesPriceChange,
                _ => SignalKind.MinutesVolumeChange,
            };

            decimal price = market.PriceOf(instrument.Uid);
            decimal change = (decimal)((random.NextDouble() * 0.08 + 0.012) * (random.NextDouble() < 0.42 ? -1 : 1));
            decimal from = decimal.Round(price / (1m + change), 4);

            history.Add(Build(
                instrument,
                kind,
                firedAt,
                kind == SignalKind.DayChange ? 0 : windowMinutes,
                priceChange: change,
                volumeChange: (decimal)(random.NextDouble() * 3 + 1.2),
                priceFrom: from,
                priceTo: price,
                volume: (long)(random.NextDouble() * 400_000),
                dayChange: (decimal)((random.NextDouble() - 0.4) * 0.12),
                dayVolume: (long)(random.NextDouble() * 8_000_000),
                dayVolOfAvg: (decimal)(random.NextDouble() * 3.5 + 0.2)));
        }

        foreach (Signal signal in history.OrderBy(s => s.FiredAt.ToDateTimeOffset()))
        {
            lock (_gate)
            {
                signal.Id = _nextId++;
                _signals.Add(signal);
            }
        }
    }

    /// <summary>
    /// Builds a signal from its measured values.
    /// </summary>
    /// <remarks>
    /// The headline is deliberately left empty. It is rendered when the signal is sent, in
    /// the language that session asked for at handshake, because one stored signal is read
    /// by clients in different languages.
    /// </remarks>
    public static Signal Build(
        Instrument instrument,
        SignalKind kind,
        DateTimeOffset firedAt,
        int windowMinutes,
        decimal priceChange,
        decimal volumeChange,
        decimal priceFrom,
        decimal priceTo,
        long volume,
        decimal dayChange,
        long dayVolume,
        decimal dayVolOfAvg)
    {
        var signal = new Signal
        {
            InstrumentUid = instrument.Uid,
            Ticker = instrument.Ticker,
            Kind = kind,
            FiredAt = Timestamp.FromDateTimeOffset(firedAt),
            WindowMinutes = windowMinutes,
            PriceChange = priceChange.ToProto(),
            VolumeChange = volumeChange.ToProto(),
            PriceFrom = priceFrom.ToProto(),
            PriceTo = priceTo.ToProto(),
            Volume = volume,
            DayChange = dayChange.ToProto(),
            DayVolume = dayVolume,
            DayVolOfAvg = dayVolOfAvg.ToProto(),
            Currency = instrument.Currency,
        };

        // No headline here: it is rendered per session, in the language that session
        // asked for during the handshake.
        return signal;
    }

    private static bool Matches(Signal signal, IReadOnlyCollection<SignalKind> kinds) =>
        kinds.Count == 0 || kinds.Contains(signal.Kind);

    private static bool Within(Signal signal, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (signal.FiredAt is null)
        {
            return from is null && to is null;
        }

        DateTimeOffset at = signal.FiredAt.ToDateTimeOffset();
        return (from is null || at >= from) && (to is null || at <= to);
    }
}

/// <summary>
/// The alert rules, in the same three shapes the real strategy fires.
/// </summary>
/// <remarks>
/// This is emphatically <em>not</em> a port of <c>RocketStrategy</c> — that lives in the
/// private server repo and is characterization-tested against the WPF build. What matters
/// here is only that the client receives the three kinds of signal, with realistic fields
/// and realistic debounce behaviour, so the feed and its filters can be built.
/// </remarks>
public sealed class SignalEngine(FakeServerOptions options, SignalLog log, SyntheticMarket market)
{
    /// <summary>|day change| that arms the whole-day rule, paired with the volume gate below.</summary>
    private const decimal DayChangeThreshold = 0.05m;

    /// <summary>Day volume as a multiple of the monthly average, for the whole-day rule.</summary>
    private const decimal DayVolumeThreshold = 1.5m;

    /// <summary>|price change| over the rolling window that fires the X-minute price rule.</summary>
    private const decimal WindowPriceThreshold = 0.012m;

    /// <summary>Window volume as a fraction of the average day, for the X-minute volume rule.</summary>
    private const decimal WindowVolumeThreshold = 0.06m;

    private readonly Dictionary<(string Uid, SignalKind Kind), DateTimeOffset> _lastFired = [];

    /// <summary>Minimum gap between two signals of the same kind for the same instrument.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMinutes(2);

    /// <summary>Real elapsed duration of the rolling window, rounded to whole minutes.</summary>
    public int WindowMinutes { get; } = Math.Max(
        1,
        (int)Math.Round(options.SignalWindowTicks / options.TicksPerSecond / 60d));

    /// <summary>Evaluates one market step and appends whatever fired.</summary>
    public void Evaluate(MarketTick tick)
    {
        foreach (InstrumentMove move in tick.Moves)
        {
            Instrument? instrument = market.Find(move.Uid);
            if (instrument is null)
            {
                continue;
            }

            EvaluateDay(instrument, move, tick.At);
            EvaluateWindowPrice(instrument, move, tick.At);
            EvaluateWindowVolume(instrument, move, tick.At);
        }
    }

    private void EvaluateDay(Instrument instrument, in InstrumentMove move, DateTimeOffset at)
    {
        if (Math.Abs(move.DayChange) < DayChangeThreshold || move.DayVolOfAvg < DayVolumeThreshold)
        {
            return;
        }

        if (!Arm(move.Uid, SignalKind.DayChange, at))
        {
            return;
        }

        decimal dayOpen = move.DayChange == -1m ? move.Price : move.Price / (1m + move.DayChange);

        log.Append(SignalLog.Build(
            instrument,
            SignalKind.DayChange,
            at,
            windowMinutes: 0,
            priceChange: move.DayChange,
            volumeChange: move.DayVolOfAvg,
            priceFrom: decimal.Round(dayOpen, 4),
            priceTo: move.Price,
            volume: move.DayVolume,
            dayChange: move.DayChange,
            dayVolume: move.DayVolume,
            dayVolOfAvg: move.DayVolOfAvg));
    }

    private void EvaluateWindowPrice(Instrument instrument, in InstrumentMove move, DateTimeOffset at)
    {
        if (move.PriceWindowAgo <= 0m)
        {
            return;
        }

        decimal change = (move.Price - move.PriceWindowAgo) / move.PriceWindowAgo;
        if (Math.Abs(change) < WindowPriceThreshold)
        {
            return;
        }

        if (!Arm(move.Uid, SignalKind.MinutesPriceChange, at))
        {
            return;
        }

        log.Append(SignalLog.Build(
            instrument,
            SignalKind.MinutesPriceChange,
            at,
            WindowMinutes,
            priceChange: change,
            volumeChange: move.DayVolOfAvg,
            priceFrom: move.PriceWindowAgo,
            priceTo: move.Price,
            volume: move.DayVolume - move.DayVolumeWindowAgo,
            dayChange: move.DayChange,
            dayVolume: move.DayVolume,
            dayVolOfAvg: move.DayVolOfAvg));
    }

    private void EvaluateWindowVolume(Instrument instrument, in InstrumentMove move, DateTimeOffset at)
    {
        if (move.AvgDayVolume <= 0)
        {
            return;
        }

        long windowVolume = move.DayVolume - move.DayVolumeWindowAgo;
        decimal multiple = (decimal)windowVolume / move.AvgDayVolume;

        if (multiple < WindowVolumeThreshold)
        {
            return;
        }

        if (!Arm(move.Uid, SignalKind.MinutesVolumeChange, at))
        {
            return;
        }

        log.Append(SignalLog.Build(
            instrument,
            SignalKind.MinutesVolumeChange,
            at,
            WindowMinutes,
            priceChange: move.PriceWindowAgo > 0m
                ? (move.Price - move.PriceWindowAgo) / move.PriceWindowAgo
                : 0m,
            volumeChange: multiple,
            priceFrom: move.PriceWindowAgo,
            priceTo: move.Price,
            volume: windowVolume,
            dayChange: move.DayChange,
            dayVolume: move.DayVolume,
            dayVolOfAvg: move.DayVolOfAvg));
    }

    /// <summary>Debounce gate. Returns true when this kind may fire for this instrument now.</summary>
    private bool Arm(string uid, SignalKind kind, DateTimeOffset at)
    {
        (string uid, SignalKind kind) key = (uid, kind);

        if (_lastFired.TryGetValue(key, out DateTimeOffset previous) && at - previous < Debounce)
        {
            return false;
        }

        _lastFired[key] = at;
        return true;
    }
}
