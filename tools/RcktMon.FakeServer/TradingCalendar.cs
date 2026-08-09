using Google.Protobuf.WellKnownTypes;
using RcktMon.Protocol.V1;

namespace RcktMon.FakeServer;

/// <summary>
/// Session windows for the exchanges in the synthetic universe.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is produced as absolute UTC instants, which is the whole point. The
/// real server takes these from <c>InstrumentsService.TradingSchedules</c>; the pre-rewrite
/// app scraped a string and then hardcoded a UTC+3 offset, so every user outside Moscow
/// saw the wrong session and — worse — subscriptions were gated on it.
/// </para>
/// <para>
/// Moscow does not observe daylight saving, so the fixed +3 offset used to build the
/// wall-clock windows is correct rather than merely convenient.
/// </para>
/// </remarks>
internal static class TradingCalendar
{
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    private static readonly ExchangeHours Moscow = new(
        "MOEX",
        Open: new TimeSpan(9, 50, 0),
        Close: new TimeSpan(18, 50, 0),
        EveningOpen: new TimeSpan(19, 5, 0),
        EveningClose: new TimeSpan(23, 50, 0));

    private static readonly ExchangeHours SaintPetersburg = new(
        "SPB",
        Open: new TimeSpan(10, 0, 0),
        Close: new TimeSpan(23, 0, 0),
        EveningOpen: null,
        EveningClose: null);

    /// <summary>Both exchanges' sessions for the day <paramref name="now"/> falls in.</summary>
    public static IReadOnlyList<ExchangeSession> Sessions(DateTimeOffset now) =>
        [Session(Moscow, now), Session(SaintPetersburg, now)];

    /// <summary>The next open or close transition, for the dashboard countdown.</summary>
    public static (string Label, DateTimeOffset At)? NextTransition(ExchangeSession session, DateTimeOffset now)
    {
        if (session.OpensAt is not null && now < session.OpensAt.ToDateTimeOffset())
        {
            return ("open", session.OpensAt.ToDateTimeOffset());
        }

        if (session.ClosesAt is not null && now < session.ClosesAt.ToDateTimeOffset())
        {
            return ("close", session.ClosesAt.ToDateTimeOffset());
        }

        if (session.EveningOpensAt is not null && now < session.EveningOpensAt.ToDateTimeOffset())
        {
            return ("open", session.EveningOpensAt.ToDateTimeOffset());
        }

        if (session.EveningClosesAt is not null && now < session.EveningClosesAt.ToDateTimeOffset())
        {
            return ("close", session.EveningClosesAt.ToDateTimeOffset());
        }

        return null;
    }

    private static ExchangeSession Session(ExchangeHours hours, DateTimeOffset now)
    {
        DateTimeOffset moscowNow = now.ToOffset(MoscowOffset);
        DateOnly today = DateOnly.FromDateTime(moscowNow.DateTime);

        bool tradingDay = moscowNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        DateTimeOffset opens = At(today, hours.Open);
        DateTimeOffset closes = At(today, hours.Close);
        DateTimeOffset? eveningOpens = hours.EveningOpen is { } eo ? At(today, eo) : null;
        DateTimeOffset? eveningCloses = hours.EveningClose is { } ec ? At(today, ec) : null;

        var session = new ExchangeSession
        {
            Exchange = hours.Name,
            IsTradingDay = tradingDay,
            OpensAt = Timestamp.FromDateTimeOffset(opens),
            ClosesAt = Timestamp.FromDateTimeOffset(closes),
            Status = Status(tradingDay, now, opens, closes, eveningOpens, eveningCloses),
        };

        if (eveningOpens is not null && eveningCloses is not null)
        {
            session.EveningOpensAt = Timestamp.FromDateTimeOffset(eveningOpens.Value);
            session.EveningClosesAt = Timestamp.FromDateTimeOffset(eveningCloses.Value);
        }

        return session;
    }

    private static TradingStatus Status(
        bool tradingDay,
        DateTimeOffset now,
        DateTimeOffset opens,
        DateTimeOffset closes,
        DateTimeOffset? eveningOpens,
        DateTimeOffset? eveningCloses)
    {
        if (!tradingDay)
        {
            return TradingStatus.Closed;
        }

        // The opening and closing auctions are separate statuses rather than a blunt
        // "open", because the monitor grid dims non-tradable rows and an auction is the
        // one time of day when that distinction is visible and matters.
        if (now < opens)
        {
            return now >= opens - TimeSpan.FromMinutes(10)
                ? TradingStatus.OpeningAuction
                : TradingStatus.Closed;
        }

        if (now < closes)
        {
            return now >= closes - TimeSpan.FromMinutes(10)
                ? TradingStatus.ClosingAuction
                : TradingStatus.Normal;
        }

        if (eveningOpens is not null && eveningCloses is not null)
        {
            if (now < eveningOpens)
            {
                return TradingStatus.Break;
            }

            if (now < eveningCloses)
            {
                return TradingStatus.Normal;
            }
        }

        return TradingStatus.Closed;
    }

    private static DateTimeOffset At(DateOnly date, TimeSpan time) =>
        new(date.Year, date.Month, date.Day, time.Hours, time.Minutes, time.Seconds, MoscowOffset);

    private readonly record struct ExchangeHours(
        string Name,
        TimeSpan Open,
        TimeSpan Close,
        TimeSpan? EveningOpen,
        TimeSpan? EveningClose);
}
