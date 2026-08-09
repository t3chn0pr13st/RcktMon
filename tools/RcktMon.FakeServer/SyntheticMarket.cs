using System.Collections.Concurrent;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;

namespace RcktMon.FakeServer;

/// <summary>
/// A plausible synthetic market: a fixed universe of instruments doing a random walk,
/// with occasional sharp moves so the alert-shaped parts of the UI actually light up.
/// </summary>
/// <remarks>
/// The point is not realism for its own sake. This is the client's permanent performance
/// harness — 2000 instruments at two updates a second is the load the monitor grid has to
/// survive — and the source of the "rocket" behaviour the conditional colouring, row
/// accents and live re-sorting exist to display.
/// </remarks>
public sealed class SyntheticMarket
{
    private readonly Instrument[] _instruments;
    private readonly State[] _states;
    private readonly ConcurrentDictionary<string, int> _indexByUid = new(StringComparer.Ordinal);
    private readonly Random _random;
    private readonly FakeServerOptions _options;

    private ulong _seq;

    public SyntheticMarket(FakeServerOptions options)
    {
        _options = options;

        // Fixed seed: the same run produces the same market, so a UI bug found once can
        // be reproduced. Chaos mode is where nondeterminism belongs.
        _random = new Random(options.Seed);

        _instruments = new Instrument[options.InstrumentCount];
        _states = new State[options.InstrumentCount];

        for (int i = 0; i < options.InstrumentCount; i++)
        {
            _instruments[i] = CreateInstrument(i);
            _states[i] = CreateState(i, options.SignalWindowTicks);
            _indexByUid[_instruments[i].Uid] = i;
        }

        Etag = $"synthetic-{options.InstrumentCount}-{options.Seed}";
    }

    public string Etag { get; }

    public IReadOnlyList<Instrument> Instruments => _instruments;

    public ulong CurrentSeq => Interlocked.Read(ref _seq);

    /// <summary>Advances every instrument one step and returns the ones that moved.</summary>
    public MarketTick Tick()
    {
        var deltas = new List<QuoteDelta>(capacity: _states.Length / 4);
        var moves = new List<InstrumentMove>(capacity: _states.Length / 4);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Google.Protobuf.WellKnownTypes.Timestamp stamp =
            Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now);

        lock (_random)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (!Advance(ref _states[i], out decimal newPrice))
                {
                    continue;
                }

                State state = _states[i];

                deltas.Add(new QuoteDelta
                {
                    InstrumentUid = _instruments[i].Uid,
                    LastPrice = newPrice.ToProto(),
                    DayChange = state.DayChange.ToProto(),
                    DayVolume = state.DayVolume,
                    DayVolumeCost = (state.DayVolume * newPrice).ToProto(),
                    DayVolOfAvg = state.DayVolOfAvg.ToProto(),
                    BestBid = (newPrice * 0.9995m).ToProto(),
                    BestAsk = (newPrice * 1.0005m).ToProto(),
                    LastTradeTime = stamp,
                });

                // The window lookback is what turns a price walk into something the
                // X-minute rules can fire on; without it only whole-day thresholds
                // would ever trigger and two thirds of the signal feed would be empty.
                state.TryWindowAgo(
                    _options.SignalWindowTicks,
                    out decimal priceThen,
                    out long volumeThen);

                moves.Add(new InstrumentMove(
                    _instruments[i].Uid,
                    _instruments[i].Ticker,
                    _instruments[i].Currency,
                    newPrice,
                    priceThen,
                    state.DayChange,
                    state.DayVolume,
                    volumeThen,
                    state.DayVolOfAvg,
                    state.AvgDayVolume));
            }
        }

        Interlocked.Increment(ref _seq);
        return new MarketTick(deltas, moves, now);
    }

    /// <summary>Full state of every instrument, for the initial snapshot.</summary>
    public IEnumerable<Quote> Snapshot(bool includeSparkline, bool includeMonthStats)
    {
        for (int i = 0; i < _states.Length; i++)
        {
            State state = _states[i];

            var quote = new Quote
            {
                InstrumentUid = _instruments[i].Uid,
                LastPrice = state.Price.ToProto(),
                DayOpen = state.DayOpen.ToProto(),
                PrevClose = state.PrevClose.ToProto(),
                DayChange = state.DayChange.ToProto(),
                DayVolume = state.DayVolume,
                DayVolumeCost = (state.DayVolume * state.Price).ToProto(),
                BestBid = (state.Price * 0.9995m).ToProto(),
                BestAsk = (state.Price * 1.0005m).ToProto(),
                DayVolOfAvg = state.DayVolOfAvg.ToProto(),
                Status = state.Status,
                LastTradeTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(
                    DateTimeOffset.UtcNow),
            };

            if (includeMonthStats)
            {
                quote.Month = new MonthStats
                {
                    MonthOpen = (state.DayOpen * 0.94m).ToProto(),
                    MonthHigh = (state.DayOpen * 1.18m).ToProto(),
                    MonthLow = (state.DayOpen * 0.85m).ToProto(),
                    MonthVolume = state.AvgDayVolume * 21,
                    AvgDayVolume = state.AvgDayVolume,
                    AvgDayPrice = state.DayOpen.ToProto(),
                    YesterdayVolume = (long)(state.AvgDayVolume * 1.1),
                    YesterdayAvgPrice = state.PrevClose.ToProto(),
                };
            }

            if (includeSparkline)
            {
                quote.Sparkline.AddRange(BuildSparkline(state));
            }

            yield return quote;
        }
    }

    /// <summary>Looks up an instrument by uid, or null when it is not in the universe.</summary>
    public Instrument? Find(string uid) =>
        _indexByUid.TryGetValue(uid, out int index) ? _instruments[index] : null;

    /// <summary>A random sample of instruments, used to populate the seeded signal backlog.</summary>
    public Instrument Sample(Random random) => _instruments[random.Next(_instruments.Length)];

    /// <summary>Current price of an instrument, or 0 when it is unknown.</summary>
    public decimal PriceOf(string uid) =>
        _indexByUid.TryGetValue(uid, out int index) ? _states[index].Price : 0m;

    /// <summary>
    /// Deterministic candle history for the inspector chart.
    /// </summary>
    /// <remarks>
    /// Seeded from the instrument uid rather than from the live walk, so the same request
    /// always returns the same series. A chart that redraws differently on every open is
    /// worse than no chart when you are trying to tell whether a rendering change worked.
    /// </remarks>
    public IReadOnlyList<Candle> History(string uid, CandleInterval interval, int count)
    {
        if (!_indexByUid.TryGetValue(uid, out int index))
        {
            return [];
        }

        count = Math.Clamp(count, 1, 1440);

        State state = _states[index];
        var random = new Random(HashCode.Combine(_options.Seed, uid, (int)interval));

        TimeSpan step = interval switch
        {
            CandleInterval.FiveMinutes => TimeSpan.FromMinutes(5),
            CandleInterval.FifteenMinutes => TimeSpan.FromMinutes(15),
            CandleInterval.OneHour => TimeSpan.FromHours(1),
            CandleInterval.OneDay => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(1),
        };

        // Walk backwards from the current price so the newest candle joins up with the
        // live quote instead of floating somewhere unrelated to it.
        var closes = new decimal[count];
        decimal price = state.Price;
        for (int i = count - 1; i >= 0; i--)
        {
            closes[i] = price;
            price = decimal.Round(price / (1m + (decimal)((random.NextDouble() - 0.5) * 0.01)), 4);
        }

        DateTimeOffset end = DateTimeOffset.UtcNow;
        var candles = new List<Candle>(count);

        for (int i = 0; i < count; i++)
        {
            decimal close = closes[i];
            decimal open = i == 0 ? close : closes[i - 1];
            decimal high = Math.Max(open, close) * (1m + (decimal)(random.NextDouble() * 0.003));
            decimal low = Math.Min(open, close) * (1m - (decimal)(random.NextDouble() * 0.003));

            candles.Add(new Candle
            {
                Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(
                    end - step * (count - 1 - i)),
                Open = decimal.Round(open, 4).ToProto(),
                High = decimal.Round(high, 4).ToProto(),
                Low = decimal.Round(low, 4).ToProto(),
                Close = decimal.Round(close, 4).ToProto(),
                Volume = state.AvgDayVolume / Math.Max(1, count) * random.Next(1, 4),
            });
        }

        return candles;
    }

    private bool Advance(ref State state, out decimal newPrice)
    {
        newPrice = state.Price;

        // Not every instrument prints on every tick. Illiquid names going quiet is part
        // of what the grid has to render correctly.
        if (_random.NextDouble() > state.Liquidity)
        {
            return false;
        }

        // Occasionally start a sustained run, so "rockets" are a streak rather than a
        // single-tick spike that the eye never catches.
        if (state.RunTicksLeft == 0 && _random.NextDouble() < _options.RunProbability)
        {
            state.RunTicksLeft = _random.Next(20, 90);
            state.RunDrift = (decimal)((_random.NextDouble() - 0.45) * 0.004);
        }

        decimal drift = 0m;
        if (state.RunTicksLeft > 0)
        {
            state.RunTicksLeft--;
            drift = state.RunDrift;
        }

        decimal noise = (decimal)((_random.NextDouble() - 0.5) * 0.002);
        decimal factor = 1m + drift + noise;

        newPrice = decimal.Round(state.Price * factor, 4);
        if (newPrice <= 0m)
        {
            newPrice = state.Price;
            return false;
        }

        state.Price = newPrice;
        state.DayChange = state.DayOpen > 0m ? (newPrice - state.DayOpen) / state.DayOpen : 0m;

        long lotVolume = _random.Next(1, 400);
        state.DayVolume += lotVolume;
        state.DayVolOfAvg = state.AvgDayVolume > 0
            ? (decimal)state.DayVolume / state.AvgDayVolume
            : 0m;

        state.PushSparkline(newPrice);
        state.PushWindow(newPrice, state.DayVolume);

        return true;
    }

    private int[] BuildSparkline(in State state)
    {
        Span<decimal> closes = stackalloc decimal[SparklineCodec.Points];
        int count = state.CopySparkline(closes);

        return count == 0
            ? []
            : SparklineCodec.Encode(closes[..count], state.DayOpen);
    }

    private Instrument CreateInstrument(int index)
    {
        string ticker = TickerCatalog.At(index);
        (string currency, string exchange) = index % 5 == 0
            ? ("USD", "SPB")
            : ("RUB", "MOEX");

        return new Instrument
        {
            // Shaped like a real T-Invest uid so nothing in the client can accidentally
            // depend on the key being short or human-readable.
            Uid = $"synthetic-{index:D5}-{Guid.Empty}",
            Figi = $"BBG{index:D9}",
            Ticker = ticker,
            ClassCode = currency == "RUB" ? "TQBR" : "SPBXM",
            Isin = $"RU000A{index:D6}",
            Name = TickerCatalog.NameFor(ticker, index),
            Currency = currency,
            InstrumentClass = InstrumentClass.Share,
            Lot = index % 7 == 0 ? 10 : 1,
            MinPriceIncrement = 0.01m.ToProto(),
            Exchange = exchange,
            Sector = TickerCatalog.SectorFor(index),
            ShortEnabled = index % 3 != 0,
            ApiTradeAvailable = true,
            FirstCandleDate = "2018-01-01",
        };
    }

    private State CreateState(int index, int windowTicks)
    {
        decimal open = decimal.Round((decimal)(_random.NextDouble() * 4900 + 10), 2);

        var state = new State
        {
            DayOpen = open,
            PrevClose = decimal.Round(open * (decimal)(0.98 + _random.NextDouble() * 0.04), 2),
            Price = open,
            AvgDayVolume = _random.Next(5_000, 4_000_000),
            // Heavily skewed: a handful of names carry most of the prints, like a real
            // market. This is what makes the grid's update pattern non-uniform.
            Liquidity = Math.Pow(_random.NextDouble(), 2.2) * 0.9 + 0.02,
            Status = index % 37 == 0 ? TradingStatus.Break : TradingStatus.Normal,
        };

        state.DayVolume = (long)(state.AvgDayVolume * _random.NextDouble() * 1.4);
        state.DayVolOfAvg = state.AvgDayVolume > 0
            ? (decimal)state.DayVolume / state.AvgDayVolume
            : 0m;

        state.InitSparkline(open);
        state.InitWindow(windowTicks, open, state.DayVolume);

        // Pre-fill the sparkline with a plausible walk into the open, so the very first
        // snapshot carries a drawable line. Otherwise the column is blank for the first
        // few minutes of every run and the one cell that needs looking at cannot be.
        decimal walk = open;
        for (int i = 0; i < SparklineCodec.Points - 1; i++)
        {
            walk = decimal.Round(walk * (1m + (decimal)((_random.NextDouble() - 0.5) * 0.004)), 4);
            state.PushSparkline(walk);
        }

        state.PushSparkline(state.Price);
        return state;
    }

    private struct State
    {
        private decimal[] _sparkline;
        private int _sparklineCount;
        private int _sparklineHead;

        private decimal[] _windowPrice;
        private long[] _windowVolume;
        private int _windowCount;
        private int _windowHead;

        public decimal Price;
        public decimal DayOpen;
        public decimal PrevClose;
        public decimal DayChange;
        public long DayVolume;
        public long AvgDayVolume;
        public decimal DayVolOfAvg;
        public double Liquidity;
        public TradingStatus Status;
        public int RunTicksLeft;
        public decimal RunDrift;

        public void InitSparkline(decimal seed)
        {
            _sparkline = new decimal[SparklineCodec.Points];
            _sparklineCount = 0;
            _sparklineHead = 0;
            PushSparkline(seed);
        }

        public void PushSparkline(decimal value)
        {
            _sparkline[_sparklineHead] = value;
            _sparklineHead = (_sparklineHead + 1) % _sparkline.Length;
            if (_sparklineCount < _sparkline.Length)
            {
                _sparklineCount++;
            }
        }

        public readonly int CopySparkline(Span<decimal> destination)
        {
            int count = Math.Min(_sparklineCount, destination.Length);
            int start = (_sparklineHead - count + _sparkline.Length) % _sparkline.Length;

            for (int i = 0; i < count; i++)
            {
                destination[i] = _sparkline[(start + i) % _sparkline.Length];
            }

            return count;
        }

        public void InitWindow(int ticks, decimal price, long volume)
        {
            _windowPrice = new decimal[Math.Max(2, ticks)];
            _windowVolume = new long[_windowPrice.Length];
            _windowCount = 0;
            _windowHead = 0;
            PushWindow(price, volume);
        }

        public void PushWindow(decimal price, long volume)
        {
            _windowPrice[_windowHead] = price;
            _windowVolume[_windowHead] = volume;
            _windowHead = (_windowHead + 1) % _windowPrice.Length;
            if (_windowCount < _windowPrice.Length)
            {
                _windowCount++;
            }
        }

        /// <summary>
        /// Reads the state <paramref name="ticks"/> steps back, or the oldest step held.
        /// </summary>
        /// <returns>
        /// False while the ring is still filling. The caller treats that as "not enough
        /// history to judge", which is the same warm-up gate the real strategy applies.
        /// </returns>
        public readonly bool TryWindowAgo(int ticks, out decimal price, out long volume)
        {
            int back = Math.Min(ticks, _windowCount);
            if (back <= 1)
            {
                price = Price;
                volume = DayVolume;
                return false;
            }

            int index = (_windowHead - back + _windowPrice.Length) % _windowPrice.Length;
            price = _windowPrice[index];
            volume = _windowVolume[index];
            return true;
        }
    }
}

/// <summary>One market step: the wire deltas plus everything the signal engine needs.</summary>
/// <param name="Deltas">Quote deltas for instruments that printed this step.</param>
/// <param name="Moves">The same instruments, with the window lookback attached.</param>
/// <param name="At">When the step happened.</param>
public sealed record MarketTick(
    IReadOnlyList<QuoteDelta> Deltas,
    IReadOnlyList<InstrumentMove> Moves,
    DateTimeOffset At);

/// <summary>
/// What one instrument did this step, in the shape the alert rules are written against.
/// </summary>
public readonly record struct InstrumentMove(
    string Uid,
    string Ticker,
    string Currency,
    decimal Price,
    decimal PriceWindowAgo,
    decimal DayChange,
    long DayVolume,
    long DayVolumeWindowAgo,
    decimal DayVolOfAvg,
    long AvgDayVolume);
