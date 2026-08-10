using System.Globalization;
using RcktMon.App.Localization;
using RcktMon.Client.Core.Market;
using RcktMon.Protocol.V1;

namespace RcktMon.App.Formatting;

/// <summary>
/// Formatting for the handful of values that need words rather than digits.
/// </summary>
/// <remarks>
/// <para>
/// Everything here reads from the resources, so the same code produces English or Russian.
/// The one thing that cannot be a plain lookup is plural agreement: English needs two
/// forms and Russian three — 1 минуту, 2 минуты, 5 минут — and getting that wrong in a
/// feed someone reads all day is what makes an app feel machine-translated.
/// </para>
/// <para>
/// Dates and numbers use <see cref="CultureInfo.CurrentCulture"/>, not the interface
/// language: someone reading an English interface in Moscow still wants their own
/// conventions for numbers and times.
/// </para>
/// </remarks>
public static class Humanize
{
    /// <summary>"just now", "3 minutes ago", "yesterday 14:20", "12 March".</summary>
    public static string Relative(DateTimeOffset value, DateTimeOffset now)
    {
        TimeSpan age = now - value;

        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age < TimeSpan.FromSeconds(45))
        {
            return Strings.Time_JustNow;
        }

        if (age < TimeSpan.FromHours(1))
        {
            int minutes = Math.Max(1, (int)age.TotalMinutes);
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.Time_AgoFormat,
                minutes,
                Minutes(minutes));
        }

        DateTimeOffset local = value.ToLocalTime();
        DateTimeOffset today = now.ToLocalTime().Date;

        if (local >= today)
        {
            return local.ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        if (local >= today.AddDays(-1))
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.Time_Yesterday,
                local.ToString("HH:mm", CultureInfo.CurrentCulture));
        }

        return local.ToString("d MMMM, HH:mm", CultureInfo.CurrentCulture);
    }

    /// <summary>Which day group a signal belongs to in the feed.</summary>
    public static string DayGroup(DateTimeOffset value, DateTimeOffset now)
    {
        DateTimeOffset today = now.ToLocalTime().Date;
        DateTimeOffset local = value.ToLocalTime();

        if (local >= today)
        {
            return Strings.Group_Today;
        }

        return local >= today.AddDays(-1) ? Strings.Group_Yesterday : Strings.Group_Earlier;
    }

    /// <summary>A countdown like "in 2 h 14 min" or "in 40 s".</summary>
    public static string Countdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return Strings.Countdown_Imminent;
        }

        if (remaining.TotalHours >= 1)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.Countdown_HoursMinutes,
                (int)remaining.TotalHours,
                remaining.Minutes);
        }

        if (remaining.TotalMinutes >= 1)
        {
            int minutes = (int)remaining.TotalMinutes;
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.Countdown_Minutes,
                minutes,
                Minutes(minutes));
        }

        return string.Format(CultureInfo.CurrentCulture, Strings.Countdown_Seconds, remaining.Seconds);
    }

    /// <summary>Short label for a market phase.</summary>
    public static string Phase(MarketPhaseKind kind) => kind switch
    {
        MarketPhaseKind.OpeningAuction => Strings.Phase_OpeningAuction,
        MarketPhaseKind.Open => Strings.Phase_Open,
        MarketPhaseKind.ClosingAuction => Strings.Phase_ClosingAuction,
        MarketPhaseKind.Break => Strings.Phase_Break,
        MarketPhaseKind.Evening => Strings.Phase_Evening,
        MarketPhaseKind.Closed => Strings.Phase_Closed,
        _ => Strings.Phase_Unknown,
    };

    /// <summary>Per-instrument trading status, as shown on the instruments page.</summary>
    public static string Status(TradingStatus status) => status switch
    {
        TradingStatus.Normal => Strings.Status_Normal,
        TradingStatus.OpeningAuction => Strings.Status_OpeningAuction,
        TradingStatus.ClosingAuction => Strings.Status_ClosingAuction,
        TradingStatus.Break => Strings.Status_Break,
        TradingStatus.Closed => Strings.Status_Closed,
        TradingStatus.Halted => Strings.Status_Halted,
        TradingStatus.NotAvailable => Strings.Status_NotAvailable,
        _ => Strings.Status_Unknown,
    };

    /// <summary>Human name for a signal kind.</summary>
    public static string SignalKindName(SignalKind kind) => kind switch
    {
        SignalKind.DayChange => Strings.SignalKind_DayChange,
        SignalKind.MinutesPriceChange => Strings.SignalKind_MinutesPrice,
        SignalKind.MinutesVolumeChange => Strings.SignalKind_MinutesVolume,
        _ => Strings.SignalKind_Other,
    };

    /// <summary>"14 signals" / "14 сигналов", with the right ending.</summary>
    public static string SignalCount(int count) =>
        $"{count} {Plural(count, Strings.Unit_Signal_One, Strings.Unit_Signal_Few, Strings.Unit_Signal_Many)}";

    /// <summary>The right form of "minute" for a count.</summary>
    private static string Minutes(int count) =>
        Plural(count, Strings.Unit_Minute_One, Strings.Unit_Minute_Few, Strings.Unit_Minute_Many);

    /// <summary>
    /// Picks a plural form for the current interface language.
    /// </summary>
    /// <remarks>
    /// Russian has three forms with a rule that depends on the last two digits; English
    /// has two and only cares whether the count is one. Languages added later will need
    /// their own branch here — there is no general rule to fall back on, which is why this
    /// is explicit rather than clever.
    /// </remarks>
    public static string Plural(int count, string one, string few, string many)
    {
        if (Localizer.Instance.Culture.TwoLetterISOLanguageName != "ru")
        {
            return Math.Abs(count) == 1 ? one : many;
        }

        int mod100 = Math.Abs(count) % 100;
        if (mod100 is >= 11 and <= 14)
        {
            return many;
        }

        return (mod100 % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }
}
