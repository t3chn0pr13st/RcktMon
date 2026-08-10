using System.Collections.Concurrent;
using DynamicData;
using DynamicData.Kernel;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;

namespace RcktMon.Client.Core.Market;

/// <summary>
/// Authoritative client-side state for every instrument, and the thread boundary between
/// the gRPC reader and the UI.
/// </summary>
/// <remarks>
/// <para>
/// Quote messages arrive on a gRPC reader thread. Property changes have to be raised on
/// the UI thread for bindings to work. Marshalling each one individually would mean
/// thousands of dispatcher hops a second, so messages are queued here by the reader and
/// applied in a single pass by <see cref="Drain"/>, which the UI calls from a timer
/// roughly ten times a second. One hop per drain instead of one per field.
/// </para>
/// <para>
/// This type has no UI dependency; the caller decides what "the UI thread" means.
/// </para>
/// </remarks>
public sealed class QuoteStore : IDisposable
{
    /// <summary>How long the price cell stays lit after a move.</summary>
    private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Minimum gap between two flashes of the same row.
    /// </summary>
    /// <remarks>
    /// Without this a liquid instrument printing several times a second reads as a strobe
    /// rather than as information, and the eye stops registering any of them.
    /// </remarks>
    private static readonly TimeSpan FlashThrottle = TimeSpan.FromMilliseconds(300);

    /// <summary>How long a row keeps its "signalled recently" accent.</summary>
    private static readonly TimeSpan SignalAccentWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often a sparkline point is appended locally.
    /// </summary>
    /// <remarks>
    /// The wire format is one point per minute, which on a synthetic or fast-forward feed
    /// would take an hour to fill. Sampling every few seconds fills the line quickly while
    /// still costing one small array per row per period rather than one per tick.
    /// </remarks>
    private static readonly TimeSpan SparklinePeriod = TimeSpan.FromSeconds(5);

    private readonly SourceCache<InstrumentRow, string> _rows = new(static row => row.Uid);
    private readonly ConcurrentQueue<QuotesMessage> _inbox = new();

    // Rows with transient state that has to be cleared on a timer. Kept as small lists
    // rather than scanning every row on every drain: at 2000 instruments the scan would
    // dominate the drain even though only a handful of rows are ever lit at once.
    private readonly List<InstrumentRow> _flashing = [];
    private readonly List<InstrumentRow> _accented = [];

    private readonly Lock _sortGate = new();
    private MonitorColumn _sortColumn = MonitorColumn.DayChange;

    // Uids seen in the snapshot currently being received, so trailing rows can be pruned
    // when it completes.
    private HashSet<string>? _snapshotUids;

    /// <summary>Highest sequence number applied. Used to resume a dropped stream.</summary>
    public ulong LastSeq { get; private set; }

    /// <summary>Total ticks the server reported skipping because this client was too slow.</summary>
    public uint DroppedTicks { get; private set; }

    /// <summary>Server timestamp of the most recently applied message.</summary>
    public DateTimeOffset? LastServerTime { get; private set; }

    /// <summary>When a quote last changed anything, used to detect a silently stale grid.</summary>
    public DateTimeOffset? LastQuoteAt { get; private set; }

    /// <summary>Observable change set for the DynamicData pipeline that feeds the grid.</summary>
    public IObservable<IChangeSet<InstrumentRow, string>> Connect() => _rows.Connect();

    /// <summary>Current row count.</summary>
    public int Count => _rows.Count;

    /// <summary>Every row currently known. Snapshot; safe to enumerate on the UI thread.</summary>
    public IReadOnlyList<InstrumentRow> Rows => _rows.Items.ToList();

    /// <summary>Looks up a row by uid, or null when the instrument is not in the current selector.</summary>
    public InstrumentRow? Lookup(string uid) => _rows.Lookup(uid).ValueOrDefault();

    /// <summary>
    /// Tells the store which column the grid is sorted by, so it knows which value
    /// changes are worth signalling.
    /// </summary>
    public void SetSortColumn(MonitorColumn column)
    {
        lock (_sortGate)
        {
            _sortColumn = column;
        }
    }

    /// <summary>Queues a message from the stream reader. Safe to call from any thread.</summary>
    public void Enqueue(QuotesMessage message) => _inbox.Enqueue(message);

    /// <summary>
    /// Applies every queued message and expires transient row state. Call from the UI thread.
    /// </summary>
    /// <returns>The number of messages applied, for diagnostics.</returns>
    public int Drain()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Decay first and unconditionally: a flash lit by the previous drain has to go out
        // even in a tick where nothing new arrived.
        DecayTransient(now);

        if (_inbox.IsEmpty)
        {
            return 0;
        }

        MonitorColumn sortColumn;
        lock (_sortGate)
        {
            sortColumn = _sortColumn;
        }

        int applied = 0;

        // A single Edit for the whole drain: DynamicData emits one change set rather than
        // one per message, so the downstream sort and bind run once.
        _rows.Edit(updater =>
        {
            while (_inbox.TryDequeue(out QuotesMessage? message))
            {
                applied++;
                switch (message.PayloadCase)
                {
                    case QuotesMessage.PayloadOneofCase.Snapshot:
                        ApplySnapshot(updater, message.Snapshot, sortColumn, now);
                        break;
                    case QuotesMessage.PayloadOneofCase.Batch:
                        ApplyBatch(updater, message.Batch, sortColumn, now);
                        break;
                    case QuotesMessage.PayloadOneofCase.Control:
                        ApplyControl(message.Control);
                        break;
                }
            }
        });

        return applied;
    }

    /// <summary>
    /// Seeds or refreshes the static identity fields from the instrument catalog.
    /// </summary>
    /// <remarks>
    /// The catalog and the quote stream arrive independently, and either can land first.
    /// Both paths create the row if it is missing, so neither has to wait for the other.
    /// </remarks>
    public void ApplyCatalog(IEnumerable<Instrument> instruments)
    {
        _rows.Edit(updater =>
        {
            foreach (Instrument instrument in instruments)
            {
                InstrumentRow row = GetOrCreate(updater, instrument.Uid, instrument.Ticker, instrument.Name);

                row.Ticker = instrument.Ticker;
                row.Name = instrument.Name;
                row.Currency = instrument.Currency;
                row.Exchange = instrument.Exchange;
                row.ClassCode = instrument.ClassCode;
                row.Sector = instrument.Sector;
                row.Isin = instrument.Isin;
                row.Figi = instrument.Figi;
                row.FirstCandleDate = instrument.FirstCandleDate;
                row.LogoUrl = string.IsNullOrEmpty(instrument.LogoUrl) ? null : instrument.LogoUrl;
                row.Lot = instrument.Lot == 0 ? 1 : instrument.Lot;
                row.InstrumentClass = instrument.InstrumentClass;
                row.ShortEnabled = instrument.ShortEnabled;
            }
        });
    }

    /// <summary>
    /// Marks an instrument as having just signalled, for the monitor's row accent.
    /// </summary>
    /// <remarks>
    /// Called from the signal stream rather than the quote stream, which is why it lives
    /// here rather than inside <see cref="Drain"/>: the accent is a property of the
    /// instrument, and the grid is the only place it can be shown.
    /// </remarks>
    public void MarkSignal(string uid, DateTimeOffset firedAt)
    {
        if (Lookup(uid) is not { } row)
        {
            return;
        }

        row.LastSignalAt = firedAt;

        if (!row.HasRecentSignal)
        {
            row.HasRecentSignal = true;
            _accented.Add(row);
        }
    }

    /// <summary>Asks every row to re-render its converter-backed cells.</summary>
    /// <remarks>Called after an interface language change; see the row's own remarks.</remarks>
    public void RefreshFormattedText()
    {
        foreach (InstrumentRow row in _rows.Items)
        {
            row.RefreshFormattedText();
        }
    }

    /// <summary>Marks every row stale, or clears the mark. Driven by server health.</summary>
    public void SetStale(bool stale)
    {
        foreach (InstrumentRow row in _rows.Items)
        {
            row.IsStale = stale;
        }
    }

    /// <summary>Drops every row. Used when the selector changes or the account switches.</summary>
    public void Clear()
    {
        _flashing.Clear();
        _accented.Clear();
        _rows.Clear();
    }

    /// <summary>Expires flashes and signal accents that have outlived their window.</summary>
    private void DecayTransient(DateTimeOffset now)
    {
        for (int i = _flashing.Count - 1; i >= 0; i--)
        {
            InstrumentRow row = _flashing[i];
            if (now - row.FlashStartedAt < FlashDuration)
            {
                continue;
            }

            row.Flash = 0;
            _flashing.RemoveAt(i);
        }

        for (int i = _accented.Count - 1; i >= 0; i--)
        {
            InstrumentRow row = _accented[i];
            if (row.LastSignalAt is { } at && now - at < SignalAccentWindow)
            {
                continue;
            }

            row.HasRecentSignal = false;
            _accented.RemoveAt(i);
        }
    }

    private void ApplySnapshot(
        ISourceUpdater<InstrumentRow, string> updater,
        QuoteSnapshotChunk chunk,
        MonitorColumn sortColumn,
        DateTimeOffset now)
    {
        _snapshotUids ??= [];

        foreach (Quote quote in chunk.Quotes)
        {
            _snapshotUids.Add(quote.InstrumentUid);

            InstrumentRow row = GetOrCreate(updater, quote.InstrumentUid, quote.InstrumentUid, quote.InstrumentUid);
            decimal? before = row.GetNumericSortValue(sortColumn);

            ApplyQuote(row, quote, now);

            SignalIfSortValueChanged(row, before, sortColumn);
        }

        if (chunk.IsLast)
        {
            // Anything the snapshot did not mention is outside the current selector.
            List<string> stale = updater.Keys.Where(key => !_snapshotUids.Contains(key)).ToList();
            if (stale.Count > 0)
            {
                updater.RemoveKeys(stale);
            }

            _snapshotUids = null;
        }

        LastSeq = chunk.Seq;
        LastServerTime = chunk.ServerTime?.ToDateTimeOffset();
        LastQuoteAt = now;
    }

    private void ApplyBatch(
        ISourceUpdater<InstrumentRow, string> updater,
        QuoteBatch batch,
        MonitorColumn sortColumn,
        DateTimeOffset now)
    {
        foreach (QuoteDelta delta in batch.Deltas)
        {
            InstrumentRow? row = updater.Lookup(delta.InstrumentUid).ValueOrDefault();
            if (row is null)
            {
                // A delta for an instrument we have never seen means our snapshot is out
                // of date. Skip it rather than inventing a half-populated row; the next
                // snapshot will fill the gap.
                continue;
            }

            decimal? before = row.GetNumericSortValue(sortColumn);

            ApplyDelta(row, delta, now);
            AppendSparkline(row, now);

            SignalIfSortValueChanged(row, before, sortColumn);
        }

        LastSeq = batch.Seq;
        LastServerTime = batch.ServerTime?.ToDateTimeOffset();

        if (batch.Deltas.Count > 0)
        {
            LastQuoteAt = now;
        }
    }

    private void ApplyControl(StreamControl control)
    {
        LastSeq = control.Seq;
        DroppedTicks = control.DroppedTicks;
        LastServerTime = control.ServerTime?.ToDateTimeOffset();
    }

    private static InstrumentRow GetOrCreate(
        ISourceUpdater<InstrumentRow, string> updater,
        string uid,
        string ticker,
        string name)
    {
        InstrumentRow? existing = updater.Lookup(uid).ValueOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        var created = new InstrumentRow(uid, ticker, name);
        updater.AddOrUpdate(created);
        return created;
    }

    private void ApplyQuote(InstrumentRow row, Quote quote, DateTimeOffset now)
    {
        decimal newPrice = quote.LastPrice.ToDecimal();
        SetPriceWithTickDirection(row, newPrice, now);

        row.DayOpen = quote.DayOpen.ToDecimal();
        row.PrevClose = quote.PrevClose.ToDecimal();
        row.DayChange = quote.DayChange.ToDecimal();
        row.DayVolume = quote.DayVolume;
        row.DayVolumeCost = quote.DayVolumeCost.ToDecimal();
        row.BestBid = quote.BestBid.ToDecimal();
        row.BestAsk = quote.BestAsk.ToDecimal();
        row.DayVolOfAvg = quote.DayVolOfAvg.ToDecimal();
        row.Status = quote.Status;
        row.LastTradeTime = quote.LastTradeTime?.ToDateTimeOffset();

        if (quote.Month is not null)
        {
            row.Month = quote.Month;
        }

        if (quote.Sparkline.Count > 0)
        {
            row.SeedSparkline(quote.Sparkline);
            row.SparklineAppendedAt = now;
        }
    }

    private void ApplyDelta(InstrumentRow row, QuoteDelta delta, DateTimeOffset now)
    {
        // Explicit presence throughout: an absent field means unchanged. Checking
        // `HasLastPrice` rather than `!= null` matters for the scalar fields, where
        // proto3 optional generates a Has* property and zero is a legal value.
        if (delta.LastPrice is not null)
        {
            SetPriceWithTickDirection(row, delta.LastPrice.ToDecimal(), now);
        }

        if (delta.DayChange is not null)
        {
            row.DayChange = delta.DayChange.ToDecimal();
        }

        if (delta.HasDayVolume)
        {
            row.DayVolume = delta.DayVolume;
        }

        if (delta.BestBid is not null)
        {
            row.BestBid = delta.BestBid.ToDecimal();
        }

        if (delta.BestAsk is not null)
        {
            row.BestAsk = delta.BestAsk.ToDecimal();
        }

        if (delta.HasStatus)
        {
            row.Status = delta.Status;
        }

        if (delta.LastTradeTime is not null)
        {
            row.LastTradeTime = delta.LastTradeTime.ToDateTimeOffset();
        }

        if (delta.DayVolOfAvg is not null)
        {
            row.DayVolOfAvg = delta.DayVolOfAvg.ToDecimal();
        }

        if (delta.DayVolumeCost is not null)
        {
            row.DayVolumeCost = delta.DayVolumeCost.ToDecimal();
        }
    }

    private void SetPriceWithTickDirection(InstrumentRow row, decimal newPrice, DateTimeOffset now)
    {
        decimal previous = row.Price;

        // Only a real move counts as a tick. Re-sending the same price — which happens
        // every poll on an illiquid instrument — must not flash the cell.
        if (previous != 0m && newPrice != previous)
        {
            int direction = newPrice > previous ? 1 : -1;
            row.LastTickDirection = direction;

            if (now - row.FlashStartedAt >= FlashThrottle)
            {
                row.FlashStartedAt = now;

                if (row.Flash == 0)
                {
                    _flashing.Add(row);
                }

                row.Flash = direction;
            }
        }

        row.Price = newPrice;
    }

    private static void AppendSparkline(InstrumentRow row, DateTimeOffset now)
    {
        if (now - row.SparklineAppendedAt < SparklinePeriod)
        {
            return;
        }

        row.SparklineAppendedAt = now;
        row.PushCurrentPrice();
    }

    private static void SignalIfSortValueChanged(InstrumentRow row, decimal? before, MonitorColumn sortColumn)
    {
        // Text columns are fixed for the session, so `before` is null and no signal is
        // ever needed.
        if (before is null)
        {
            return;
        }

        decimal? after = row.GetNumericSortValue(sortColumn);
        if (after != before)
        {
            row.RaiseSortKeyChanged();
        }
    }

    public void Dispose() => _rows.Dispose();
}
