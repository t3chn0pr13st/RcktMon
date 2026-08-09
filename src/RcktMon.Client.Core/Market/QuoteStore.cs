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
    private readonly SourceCache<InstrumentRow, string> _rows = new(static row => row.Uid);
    private readonly ConcurrentQueue<QuotesMessage> _inbox = new();

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

    /// <summary>Observable change set for the DynamicData pipeline that feeds the grid.</summary>
    public IObservable<IChangeSet<InstrumentRow, string>> Connect() => _rows.Connect();

    /// <summary>Current row count.</summary>
    public int Count => _rows.Count;

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
    /// Applies every queued message. Call from the UI thread.
    /// </summary>
    /// <returns>The number of messages applied, for diagnostics.</returns>
    public int Drain()
    {
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
                        ApplySnapshot(updater, message.Snapshot, sortColumn);
                        break;
                    case QuotesMessage.PayloadOneofCase.Batch:
                        ApplyBatch(updater, message.Batch, sortColumn);
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
                row.LogoUrl = string.IsNullOrEmpty(instrument.LogoUrl) ? null : instrument.LogoUrl;
                row.Lot = instrument.Lot == 0 ? 1 : instrument.Lot;
                row.InstrumentClass = instrument.InstrumentClass;
                row.ShortEnabled = instrument.ShortEnabled;
            }
        });
    }

    /// <summary>Drops every row. Used when the selector changes or the account switches.</summary>
    public void Clear() => _rows.Clear();

    private void ApplySnapshot(
        ISourceUpdater<InstrumentRow, string> updater,
        QuoteSnapshotChunk chunk,
        MonitorColumn sortColumn)
    {
        _snapshotUids ??= [];

        foreach (Quote quote in chunk.Quotes)
        {
            _snapshotUids.Add(quote.InstrumentUid);

            InstrumentRow row = GetOrCreate(updater, quote.InstrumentUid, quote.InstrumentUid, quote.InstrumentUid);
            decimal? before = row.GetNumericSortValue(sortColumn);

            ApplyQuote(row, quote);

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
    }

    private void ApplyBatch(
        ISourceUpdater<InstrumentRow, string> updater,
        QuoteBatch batch,
        MonitorColumn sortColumn)
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

            ApplyDelta(row, delta);

            SignalIfSortValueChanged(row, before, sortColumn);
        }

        LastSeq = batch.Seq;
        LastServerTime = batch.ServerTime?.ToDateTimeOffset();
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

    private static void ApplyQuote(InstrumentRow row, Quote quote)
    {
        decimal newPrice = quote.LastPrice.ToDecimal();
        SetPriceWithTickDirection(row, newPrice);

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

        if (quote.Sparkline.Count > 0)
        {
            row.Sparkline = [.. quote.Sparkline];
        }
    }

    private static void ApplyDelta(InstrumentRow row, QuoteDelta delta)
    {
        // Explicit presence throughout: an absent field means unchanged. Checking
        // `HasLastPrice` rather than `!= null` matters for the scalar fields, where
        // proto3 optional generates a Has* property and zero is a legal value.
        if (delta.LastPrice is not null)
        {
            SetPriceWithTickDirection(row, delta.LastPrice.ToDecimal());
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

    private static void SetPriceWithTickDirection(InstrumentRow row, decimal newPrice)
    {
        decimal previous = row.Price;

        // Only a real move counts as a tick. Re-sending the same price — which happens
        // every poll on an illiquid instrument — must not flash the cell.
        if (previous != 0m && newPrice != previous)
        {
            row.LastTickDirection = newPrice > previous ? 1 : -1;
        }

        row.Price = newPrice;
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
