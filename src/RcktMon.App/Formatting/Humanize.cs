using System.Globalization;
using RcktMon.Client.Core.Market;
using RcktMon.Protocol.V1;

namespace RcktMon.App.Formatting;

/// <summary>
/// Russian-language formatting for the handful of values that need words rather than
/// digits.
/// </summary>
/// <remarks>
/// Centralised so plural agreement is written once. Russian needs three forms — 1 минуту,
/// 2 минуты, 5 минут — and getting it wrong in a feed the user reads all day is the kind
/// of detail that makes an app feel machine-translated.
/// </remarks>
public static class Humanize
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>"только что", "3 мин назад", "вчера 14:20", "12 марта".</summary>
    public static string Relative(DateTimeOffset value, DateTimeOffset now)
    {
        TimeSpan age = now - value;

        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age < TimeSpan.FromSeconds(45))
        {
            return "только что";
        }

        if (age < TimeSpan.FromHours(1))
        {
            int minutes = Math.Max(1, (int)age.TotalMinutes);
            return $"{minutes} {Plural(minutes, "минуту", "минуты", "минут")} назад";
        }

        DateTimeOffset local = value.ToLocalTime();
        DateTimeOffset today = now.ToLocalTime().Date;

        if (local >= today)
        {
            return local.ToString("HH:mm", Ru);
        }

        if (local >= today.AddDays(-1))
        {
            return $"вчера {local:HH:mm}";
        }

        return local.ToString("d MMMM, HH:mm", Ru);
    }

    /// <summary>Which day group a signal belongs to in the feed.</summary>
    public static string DayGroup(DateTimeOffset value, DateTimeOffset now)
    {
        DateTimeOffset today = now.ToLocalTime().Date;
        DateTimeOffset local = value.ToLocalTime();

        if (local >= today)
        {
            return "Сегодня";
        }

        return local >= today.AddDays(-1) ? "Вчера" : "Ранее";
    }

    /// <summary>A countdown like "через 2 ч 14 мин" or "через 40 с".</summary>
    public static string Countdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "вот-вот";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"через {(int)remaining.TotalHours} ч {remaining.Minutes} мин";
        }

        return remaining.TotalMinutes >= 1
            ? $"через {(int)remaining.TotalMinutes} {Plural((int)remaining.TotalMinutes, "минуту", "минуты", "минут")}"
            : $"через {remaining.Seconds} с";
    }

    /// <summary>Short label for a market phase.</summary>
    public static string Phase(MarketPhaseKind kind) => kind switch
    {
        MarketPhaseKind.OpeningAuction => "Аукцион открытия",
        MarketPhaseKind.Open => "Торги идут",
        MarketPhaseKind.ClosingAuction => "Аукцион закрытия",
        MarketPhaseKind.Break => "Перерыв",
        MarketPhaseKind.Evening => "Вечерняя сессия",
        MarketPhaseKind.Closed => "Закрыто",
        _ => "Нет данных",
    };

    /// <summary>Per-instrument trading status, as shown on the instruments page.</summary>
    public static string Status(TradingStatus status) => status switch
    {
        TradingStatus.Normal => "Торгуется",
        TradingStatus.OpeningAuction => "Аукцион открытия",
        TradingStatus.ClosingAuction => "Аукцион закрытия",
        TradingStatus.Break => "Перерыв",
        TradingStatus.Closed => "Закрыт",
        TradingStatus.Halted => "Остановлен",
        TradingStatus.NotAvailable => "Недоступен",
        _ => "Неизвестно",
    };

    /// <summary>Human name for a signal kind.</summary>
    public static string SignalKindName(SignalKind kind) => kind switch
    {
        SignalKind.DayChange => "Дневное движение",
        SignalKind.MinutesPriceChange => "Рывок цены",
        SignalKind.MinutesVolumeChange => "Всплеск объёма",
        _ => "Сигнал",
    };

    /// <summary>Russian plural agreement for a count.</summary>
    public static string Plural(int count, string one, string few, string many)
    {
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

    /// <summary>"14 сигналов" with the right ending.</summary>
    public static string SignalCount(int count) =>
        $"{count} {Plural(count, "сигнал", "сигнала", "сигналов")}";
}
