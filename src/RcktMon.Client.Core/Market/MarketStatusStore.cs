using System.Collections.Concurrent;
using RcktMon.Protocol.V1;

namespace RcktMon.Client.Core.Market;

/// <summary>Where an exchange is in its trading day, in the terms the UI shows.</summary>
public enum MarketPhaseKind
{
    /// <summary>No schedule received yet.</summary>
    Unknown,

    /// <summary>A non-trading day, or before/after every window of a trading day.</summary>
    Closed,

    /// <summary>Inside the opening auction.</summary>
    OpeningAuction,

    /// <summary>Main session.</summary>
    Open,

    /// <summary>Inside the closing auction.</summary>
    ClosingAuction,

    /// <summary>Between the main and evening sessions.</summary>
    Break,

    /// <summary>Evening session.</summary>
    Evening,
}

/// <summary>
/// One exchange's current phase and the next transition.
/// </summary>
/// <param name="Exchange">Exchange code, e.g. <c>MOEX</c>.</param>
/// <param name="Kind">Where the session currently is.</param>
/// <param name="NextChangeAt">When the phase next changes, or null if nothing is scheduled.</param>
/// <param name="NextChangeOpens">True when the next transition opens trading rather than closing it.</param>
/// <param name="IsTradingDay">False on weekends and holidays.</param>
public readonly record struct MarketPhase(
    string Exchange,
    MarketPhaseKind Kind,
    DateTimeOffset? NextChangeAt,
    bool NextChangeOpens,
    bool IsTradingDay)
{
    /// <summary>True while orders can actually be matched.</summary>
    public bool IsTrading => Kind is MarketPhaseKind.Open or MarketPhaseKind.Evening;
}

/// <summary>
/// Latest server health and exchange schedule.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="QuoteStore"/> because it answers a different question. A
/// grid that has stopped moving is either a quiet market or a broken pipeline, and those
/// look identical without this stream. Getting that distinction wrong is the failure this
/// product cannot afford, so it gets its own first-class state.
/// </remarks>
public sealed class MarketStatusStore
{
    private readonly ConcurrentQueue<ServerStatus> _inbox = new();

    /// <summary>The most recent status, or null before the first one arrives.</summary>
    public ServerStatus? Latest { get; private set; }

    /// <summary>When the last status arrived, so a dead status stream is detectable.</summary>
    public DateTimeOffset? LatestAt { get; private set; }

    /// <summary>Queues a status from the stream reader. Safe to call from any thread.</summary>
    public void Enqueue(ServerStatus status) => _inbox.Enqueue(status);

    /// <summary>Applies the newest queued status. Call from the UI thread.</summary>
    /// <returns>True when something was applied.</returns>
    public bool Drain()
    {
        bool applied = false;

        // Only the newest matters: this is a state snapshot, not an event log, so a
        // backlog is drained by discarding all but the last entry.
        while (_inbox.TryDequeue(out ServerStatus? status))
        {
            Latest = status;
            applied = true;
        }

        if (applied)
        {
            LatestAt = DateTimeOffset.UtcNow;
        }

        return applied;
    }

    /// <summary>Drops the current status, e.g. after the connection is lost.</summary>
    public void Reset()
    {
        Latest = null;
        LatestAt = null;
    }

    /// <summary>Current phase of the named exchange, or the first one reported.</summary>
    public MarketPhase Phase(string exchange, DateTimeOffset now)
    {
        ExchangeSession? session = Latest?.Sessions.FirstOrDefault(
            s => string.Equals(s.Exchange, exchange, StringComparison.OrdinalIgnoreCase))
            ?? Latest?.Sessions.FirstOrDefault();

        if (session is null)
        {
            return new MarketPhase(exchange, MarketPhaseKind.Unknown, null, false, false);
        }

        return Derive(session, now);
    }

    /// <summary>Every exchange the server reported, in the order it reported them.</summary>
    public IReadOnlyList<MarketPhase> Phases(DateTimeOffset now) =>
        Latest is null
            ? []
            : [.. Latest.Sessions.Select(session => Derive(session, now))];

    private static MarketPhase Derive(ExchangeSession session, DateTimeOffset now)
    {
        DateTimeOffset? opens = session.OpensAt?.ToDateTimeOffset();
        DateTimeOffset? closes = session.ClosesAt?.ToDateTimeOffset();
        DateTimeOffset? eveningOpens = session.EveningOpensAt?.ToDateTimeOffset();
        DateTimeOffset? eveningCloses = session.EveningClosesAt?.ToDateTimeOffset();

        // The server already resolved the status against its own clock; the phase mapping
        // below only translates it into the vocabulary the UI uses, and the timestamps
        // supply the countdown.
        MarketPhaseKind kind = !session.IsTradingDay
            ? MarketPhaseKind.Closed
            : session.Status switch
            {
                TradingStatus.OpeningAuction => MarketPhaseKind.OpeningAuction,
                TradingStatus.ClosingAuction => MarketPhaseKind.ClosingAuction,
                TradingStatus.Break => MarketPhaseKind.Break,
                TradingStatus.Normal => closes is not null && now >= closes
                    ? MarketPhaseKind.Evening
                    : MarketPhaseKind.Open,
                _ => MarketPhaseKind.Closed,
            };

        // A closed exchange has no transition to count down to. Reporting one — the
        // schedule still carries today's window even on a Sunday — would have the tile
        // announce a close that is never going to happen.
        (DateTimeOffset At, bool Opens)? next = session.IsTradingDay
            ? Next(now, opens, closes, eveningOpens, eveningCloses)
            : null;

        return new MarketPhase(
            session.Exchange, kind, next?.At, next?.Opens ?? false, session.IsTradingDay);
    }

    private static (DateTimeOffset At, bool Opens)? Next(
        DateTimeOffset now,
        DateTimeOffset? opens,
        DateTimeOffset? closes,
        DateTimeOffset? eveningOpens,
        DateTimeOffset? eveningCloses)
    {
        if (opens is not null && now < opens)
        {
            return (opens.Value, true);
        }

        if (closes is not null && now < closes)
        {
            return (closes.Value, false);
        }

        if (eveningOpens is not null && now < eveningOpens)
        {
            return (eveningOpens.Value, true);
        }

        if (eveningCloses is not null && now < eveningCloses)
        {
            return (eveningCloses.Value, false);
        }

        return null;
    }
}
