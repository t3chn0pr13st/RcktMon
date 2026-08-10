using System.Collections.Concurrent;
using System.Globalization;
using Grpc.Core;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;

namespace RcktMon.FakeServer;

/// <summary>
/// Which language each connected install reads.
/// </summary>
/// <remarks>
/// The locale arrives once, on <c>Hello</c>, and every later call carries only the device
/// id — so it is remembered here, keyed by device. The real server does the same thing in
/// its auth interceptor; this is the smallest honest stand-in for that.
/// </remarks>
public static class SessionLocales
{
    private const string DeviceHeader = "x-rcktmon-device";

    private static readonly ConcurrentDictionary<string, string> ByDevice = new(StringComparer.Ordinal);

    /// <summary>Records the language a device asked for during the handshake.</summary>
    public static void Remember(string deviceId, string locale)
    {
        if (!string.IsNullOrEmpty(deviceId))
        {
            ByDevice[deviceId] = locale;
        }
    }

    /// <summary>The language for the call in progress, defaulting to English.</summary>
    public static string For(ServerCallContext context)
    {
        string? device = context.RequestHeaders.GetValue(DeviceHeader);

        return device is not null && ByDevice.TryGetValue(device, out string? locale)
            ? locale
            : "en";
    }
}

/// <summary>
/// The text this server renders on behalf of the client.
/// </summary>
/// <remarks>
/// <para>
/// Signal headlines are produced server-side by design: the same sentence has to appear in
/// the desktop app, in a Telegram post and in the web account, and three implementations
/// of it would drift. That makes the server responsible for the user's language too, which
/// is why the locale is part of the session rather than an afterthought.
/// </para>
/// <para>
/// Two languages, a dozen strings, no resource infrastructure. The real server has a
/// proper catalogue; this one only has to prove the protocol carries the locale.
/// </para>
/// </remarks>
public static class FakeStrings
{
    /// <summary>True when this session reads Russian.</summary>
    private static bool IsRussian(string locale) =>
        locale.StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    private static CultureInfo Culture(string locale) =>
        CultureInfo.GetCultureInfo(IsRussian(locale) ? "ru-RU" : "en-US");

    public static string AccountName(string locale) =>
        IsRussian(locale) ? "Локальный стенд" : "Local sandbox";

    public static string DefaultProfileName(string locale) =>
        IsRussian(locale) ? "Мой профиль" : "My profile";

    public static string PresetName(string key, string locale) => (key, IsRussian(locale)) switch
    {
        ("conservative", true) => "Консервативный",
        ("conservative", false) => "Conservative",
        ("balanced", true) => "Сбалансированный",
        ("balanced", false) => "Balanced",
        ("aggressive", true) => "Агрессивный",
        _ => "Aggressive",
    };

    public static string LastProfile(string locale) =>
        IsRussian(locale)
            ? "Нельзя удалить последний профиль оповещений."
            : "The last alert profile cannot be deleted.";

    public static string ProfileNotFound(string locale) =>
        IsRussian(locale) ? "Профиль не найден." : "Profile not found.";

    public static string ShardsDown(int down, int total, string locale) =>
        IsRussian(locale)
            ? $"{down} из {total} шардов не отвечают (chaos)"
            : $"{down} of {total} shards are not responding (chaos)";

    /// <summary>
    /// The one-line summary shown in the feed, on the dashboard and in Telegram.
    /// </summary>
    public static string Headline(Signal signal, string locale)
    {
        CultureInfo culture = Culture(locale);
        bool ru = IsRussian(locale);

        string change = signal.PriceChange.ToDecimal().ToString("+0.00%;-0.00%;0.00%", culture);
        string dayChange = signal.DayChange.ToDecimal().ToString("+0.00%;-0.00%;0.00%", culture);
        string from = signal.PriceFrom.ToDecimal().ToString("N2", culture);
        string to = signal.PriceTo.ToDecimal().ToString("N2", culture);
        string dayVolume = signal.DayVolOfAvg.ToDecimal().ToString("0.0", culture);
        string windowVolume = signal.VolumeChange.ToDecimal().ToString("0.0", culture);
        int minutes = signal.WindowMinutes;

        return (signal.Kind, ru) switch
        {
            (SignalKind.DayChange, true) =>
                $"{signal.Ticker} {dayChange} за день, объём {dayVolume}× среднего",
            (SignalKind.DayChange, false) =>
                $"{signal.Ticker} {dayChange} on the day, volume {dayVolume}× average",

            (SignalKind.MinutesPriceChange, true) =>
                $"{signal.Ticker} {change} за {minutes} мин, {from} → {to}",
            (SignalKind.MinutesPriceChange, false) =>
                $"{signal.Ticker} {change} in {minutes} min, {from} → {to}",

            (SignalKind.MinutesVolumeChange, true) =>
                $"{signal.Ticker} объём {windowVolume}× среднего за {minutes} мин",
            (SignalKind.MinutesVolumeChange, false) =>
                $"{signal.Ticker} volume {windowVolume}× average over {minutes} min",

            _ => signal.Ticker,
        };
    }

    /// <summary>A copy of the signal with its headline rendered for this session.</summary>
    /// <remarks>
    /// A copy, because the stored signal is shared by every connected client and two of
    /// them may be reading different languages.
    /// </remarks>
    public static Signal Localized(this Signal signal, string locale)
    {
        Signal copy = signal.Clone();
        copy.Headline = Headline(copy, locale);
        return copy;
    }
}
