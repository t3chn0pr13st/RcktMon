using System.Collections.Concurrent;
using DynamicData;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;

namespace RcktMon.Client.Core.Market;

/// <summary>
/// One fired alert, decoded from the wire into the types the UI binds to.
/// </summary>
/// <remarks>
/// A record rather than an observable object: a signal never changes after it fires, so
/// there is nothing to notify about. That also makes the feed cheap — cards are pure
/// functions of immutable data and can be virtualised freely.
/// </remarks>
public sealed record SignalEntry(
    long Id,
    string InstrumentUid,
    string Ticker,
    SignalKind Kind,
    DateTimeOffset FiredAt,
    int WindowMinutes,
    decimal PriceChange,
    decimal VolumeChange,
    decimal PriceFrom,
    decimal PriceTo,
    long Volume,
    decimal DayChange,
    long DayVolume,
    decimal DayVolOfAvg,
    string Currency,
    string Headline)
{
    /// <summary>Decodes a wire signal, filling in a headline if the server left it empty.</summary>
    public static SignalEntry From(Signal signal) => new(
        signal.Id,
        signal.InstrumentUid,
        signal.Ticker,
        signal.Kind,
        signal.FiredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
        signal.WindowMinutes,
        signal.PriceChange.ToDecimal(),
        signal.VolumeChange.ToDecimal(),
        signal.PriceFrom.ToDecimal(),
        signal.PriceTo.ToDecimal(),
        signal.Volume,
        signal.DayChange.ToDecimal(),
        signal.DayVolume,
        signal.DayVolOfAvg.ToDecimal(),
        signal.Currency,
        string.IsNullOrEmpty(signal.Headline) ? signal.Ticker : signal.Headline);

    /// <summary>Direction of the move this signal describes: +1, -1 or 0.</summary>
    public int Direction => Kind == SignalKind.DayChange
        ? Math.Sign(DayChange)
        : Math.Sign(PriceChange);
}

/// <summary>
/// The signal feed: live alerts plus whatever history the server served.
/// </summary>
/// <remarks>
/// Same reader-thread-to-UI-thread contract as <see cref="QuoteStore"/>. Signals arrive
/// far less often than quotes, but they arrive on the same gRPC reader threads and must
/// not mutate a bound collection from there.
/// </remarks>
public sealed class SignalStore : IDisposable
{
    /// <summary>
    /// Upper bound on retained signals.
    /// </summary>
    /// <remarks>
    /// The pre-rewrite app kept messages in an unbounded, non-thread-safe
    /// <c>HashSet</c> that grew for as long as the process lived. Anything older than
    /// this cap is a server query away, so there is no reason to hold it.
    /// </remarks>
    private const int Capacity = 2000;

    private readonly SourceCache<SignalEntry, long> _signals = new(static s => s.Id);
    private readonly ConcurrentQueue<SignalEntry> _inbox = new();

    /// <summary>Highest signal id applied, used to resume the stream without gaps.</summary>
    public long LastId { get; private set; }

    /// <summary>Observable change set for the feed.</summary>
    public IObservable<IChangeSet<SignalEntry, long>> Connect() => _signals.Connect();

    /// <summary>How many signals are currently held.</summary>
    public int Count => _signals.Count;

    /// <summary>Queues a signal from the stream reader. Safe to call from any thread.</summary>
    public void Enqueue(Signal signal) => _inbox.Enqueue(SignalEntry.From(signal));

    /// <summary>Queues a page of history. Safe to call from any thread.</summary>
    public void EnqueueRange(IEnumerable<Signal> signals)
    {
        foreach (Signal signal in signals)
        {
            Enqueue(signal);
        }
    }

    /// <summary>
    /// Applies everything queued. Call from the UI thread.
    /// </summary>
    /// <returns>The signals that were genuinely new, so the caller can react to them.</returns>
    public IReadOnlyList<SignalEntry> Drain()
    {
        if (_inbox.IsEmpty)
        {
            return [];
        }

        var added = new List<SignalEntry>();

        _signals.Edit(updater =>
        {
            while (_inbox.TryDequeue(out SignalEntry? entry))
            {
                if (updater.Lookup(entry.Id).HasValue)
                {
                    // History and the live stream overlap by design — the client resumes
                    // from an id and the server replays inclusively at the boundary.
                    continue;
                }

                updater.AddOrUpdate(entry);
                added.Add(entry);
                LastId = Math.Max(LastId, entry.Id);
            }

            if (updater.Count > Capacity)
            {
                IEnumerable<long> oldest = updater.Items
                    .OrderBy(static s => s.Id)
                    .Take(updater.Count - Capacity)
                    .Select(static s => s.Id);

                updater.RemoveKeys(oldest.ToList());
            }
        });

        return added;
    }

    /// <summary>Everything held, newest first.</summary>
    /// <remarks>
    /// A distinct method rather than <c>Recent(int.MaxValue)</c>: passing a huge count to
    /// <c>Take</c> on an ordered sequence is asking LINQ's partitioning to do arithmetic
    /// near <see cref="int.MaxValue"/>, which is a trap with no upside.
    /// </remarks>
    public IReadOnlyList<SignalEntry> All() =>
        _signals.Items.OrderByDescending(static s => s.Id).ToList();

    /// <summary>The newest signals, newest first.</summary>
    public IReadOnlyList<SignalEntry> Recent(int count) =>
        _signals.Items.OrderByDescending(static s => s.Id).Take(count).ToList();

    /// <summary>The newest signals for one instrument, newest first.</summary>
    public IReadOnlyList<SignalEntry> ForInstrument(string uid, int count) =>
        _signals.Items
            .Where(s => string.Equals(s.InstrumentUid, uid, StringComparison.Ordinal))
            .OrderByDescending(static s => s.Id)
            .Take(count)
            .ToList();

    /// <summary>How many signals fired at or after <paramref name="from"/>.</summary>
    public int CountFrom(DateTimeOffset from) =>
        _signals.Items.Count(s => s.FiredAt >= from);

    public void Dispose() => _signals.Dispose();
}
