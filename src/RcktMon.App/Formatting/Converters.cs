using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using RcktMon.Client.Core.Connection;
using RcktMon.Protocol.V1;

namespace RcktMon.App.Formatting;

/// <summary>
/// Maps a fractional change to one of nine diverging colour buckets.
/// </summary>
/// <remarks>
/// The brush is resolved from the active theme dictionary by key, so a view never names
/// a colour and both themes stay switchable at runtime. Buckets are discrete rather than
/// a continuous gradient precisely so each shade can be contrast-checked once.
/// </remarks>
public sealed class ChangeBucketConverter : IValueConverter
{
    /// <summary>Returns the text brush for the bucket.</summary>
    public static ChangeBucketConverter Foreground { get; } = new() { _suffix = string.Empty };

    /// <summary>Returns the low-alpha cell tint for the bucket.</summary>
    public static ChangeBucketConverter Fill { get; } = new() { _suffix = ".Fill" };

    private string _suffix = string.Empty;

    /// <summary>
    /// Bucket edges as absolute fractional change: 0.5%, 2%, 5%.
    /// </summary>
    /// <remarks>
    /// Chosen so an ordinary session spreads across buckets 1–2 and only a genuine move
    /// reaches 3–4. If everything is bucket 4, the colour stops carrying information.
    /// </remarks>
    private static readonly decimal[] Edges = [0.005m, 0.02m, 0.05m];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal change)
        {
            return AvaloniaProperty.UnsetValue;
        }

        string key = KeyFor(change, _suffix);

        return Application.Current?.TryFindResource(key, CurrentVariant(), out object? brush) is true
            ? brush as IBrush ?? (object)AvaloniaProperty.UnsetValue
            : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Change colouring is display-only.");

    internal static string KeyFor(decimal change, string suffix)
    {
        decimal magnitude = Math.Abs(change);

        if (magnitude < Edges[0])
        {
            // Neutral has no fill: tinting every flat row would make the whole grid hum.
            return suffix.Length == 0 ? "Brush.Chg.Neutral" : "Brush.Chg.Neutral.Fill";
        }

        int level = magnitude < Edges[1] ? 1 : magnitude < Edges[2] ? 2 : magnitude < 0.10m ? 3 : 4;
        string direction = change > 0m ? "Up" : "Down";

        return $"Brush.Chg.{direction}{suffix}.{level}";
    }

    private static ThemeVariant CurrentVariant() =>
        Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
}

/// <summary>Formats a fraction as a signed percentage with an arrow.</summary>
/// <remarks>
/// The arrow is not decoration. Colour must never be the only channel carrying meaning,
/// both for colour-blind users and for anyone reading a screenshot in greyscale.
/// </remarks>
public sealed class SignedPercentConverter : IValueConverter
{
    public static SignedPercentConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal change)
        {
            return string.Empty;
        }

        string arrow = change switch
        {
            > 0.0001m => "▲",
            < -0.0001m => "▼",
            _ => "•",
        };

        return $"{arrow} {change:+0.00%;-0.00%;0.00%}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Compacts large numbers to k / M / B, the way a trading grid needs.</summary>
public sealed class CompactNumberConverter : IValueConverter
{
    public static CompactNumberConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        decimal number = value switch
        {
            long l => l,
            int i => i,
            decimal d => d,
            double db => (decimal)db,
            _ => 0m,
        };

        decimal abs = Math.Abs(number);

        // Culture-aware on purpose: the grid is localised, and a Russian user expects a
        // comma as the decimal separator here.
        return abs switch
        {
            >= 1_000_000_000m => (number / 1_000_000_000m).ToString("0.##", culture) + " B",
            >= 1_000_000m => (number / 1_000_000m).ToString("0.##", culture) + " M",
            >= 1_000m => (number / 1_000m).ToString("0.#", culture) + " k",
            _ => number.ToString("0.##", culture),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Widths for the in-cell volume bar behind the "% of average" column.</summary>
public sealed class VolumeBarWidthConverter : IValueConverter
{
    public static VolumeBarWidthConverter Instance { get; } = new();

    /// <summary>Bar width at which the cell is considered "full", i.e. 3x the monthly average.</summary>
    private const decimal FullScale = 3m;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal ratio || ratio <= 0m)
        {
            return 0d;
        }

        double maxWidth = parameter is string s &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 80d;

        return (double)Math.Min(ratio / FullScale, 1m) * maxWidth;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Human-readable text for each connection state.</summary>
public sealed class ConnectionTextConverter : IValueConverter
{
    public static ConnectionTextConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ConnectionStatus status)
        {
            return string.Empty;
        }

        return status.State switch
        {
            ConnectionState.Connected => "Подключено",
            ConnectionState.Connecting => "Подключение…",
            ConnectionState.Reconnecting => status.RetryIn is { } retry
                ? $"Переподключение через {retry.TotalSeconds:0} с"
                : "Переподключение…",
            ConnectionState.Degraded => status.Detail ?? "Данные могут быть устаревшими",
            ConnectionState.Unauthorized => "Ключ доступа отклонён",
            ConnectionState.VersionMismatch => "Требуется обновление",
            _ => "Нет соединения",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Resolves a resource key held in a view model into the resource itself.</summary>
/// <remarks>
/// View models name their rail icon by key rather than holding a <c>Geometry</c>, so they
/// stay free of drawing types and remain testable headlessly.
/// </remarks>
public sealed class ResourceLookupConverter : IValueConverter
{
    public static ResourceLookupConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || Application.Current is not { } app)
        {
            return AvaloniaProperty.UnsetValue;
        }

        return app.TryFindResource(key, app.ActualThemeVariant, out object? resource)
            ? resource
            : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Relative time in Russian: "только что", "3 минуты назад", "вчера 14:20".</summary>
public sealed class RelativeTimeConverter : IValueConverter
{
    public static RelativeTimeConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            DateTimeOffset at => Humanize.Relative(at, DateTimeOffset.UtcNow),
            DateTime at => Humanize.Relative(at, DateTimeOffset.UtcNow),
            _ => string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Trading status as a word rather than an enum name.</summary>
public sealed class TradingStatusConverter : IValueConverter
{
    public static TradingStatusConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TradingStatus status ? Humanize.Status(status) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Signal kind as a word.</summary>
public sealed class SignalKindConverter : IValueConverter
{
    public static SignalKindConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SignalKind kind ? Humanize.SignalKindName(kind) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Compares a bound value with a converter parameter, for radio-style option groups.
/// </summary>
/// <remarks>
/// Avalonia has no <c>RadioButton</c> group binding to an enum, and hand-rolling one
/// property per option is how a settings pane ends up with a dozen booleans that can
/// disagree with each other.
/// </remarks>
public sealed class EnumMatchConverter : IValueConverter
{
    public static EnumMatchConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Only the checked direction is meaningful: unchecking a radio button in a group
        // happens as a side effect of checking another one.
        if (value is not true || parameter is null)
        {
            return AvaloniaProperty.UnsetValue;
        }

        Type enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return enumType.IsEnum && Enum.TryParse(enumType, parameter.ToString(), out object? parsed)
            ? parsed
            : AvaloniaProperty.UnsetValue;
    }
}

/// <summary>Dims rows for instruments that are not currently tradable.</summary>
public sealed class InactiveOpacityConverter : IValueConverter
{
    public static InactiveOpacityConverter Instance { get; } = new();

    /// <summary>Enough to recede, not so much that the row becomes unreadable.</summary>
    private const double Dimmed = 0.55;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Dimmed : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Renders a flag as a word, because "True" in a table is not a translation.</summary>
public sealed class YesNoConverter : IValueConverter
{
    public static YesNoConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            true => "Да",
            false => "Нет",
            _ => "—",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Fades the watchlist star for instruments that are not starred.</summary>
public sealed class WatchOpacityConverter : IValueConverter
{
    public static WatchOpacityConverter Instance { get; } = new();

    /// <summary>Visible enough to be discoverable, faint enough not to read as active.</summary>
    private const double Unwatched = 0.22;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : Unwatched;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Colour for the connection pill, resolved from the theme.</summary>
public sealed class ConnectionBrushConverter : IValueConverter
{
    public static ConnectionBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is ConnectionStatus status
            ? status.State switch
            {
                ConnectionState.Connected => "Brush.Positive",
                ConnectionState.Degraded or ConnectionState.Reconnecting or ConnectionState.Connecting => "Brush.Warning",
                _ => "Brush.Negative",
            }
            : "Brush.Text.Tertiary";

        ThemeVariant variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;

        return Application.Current?.TryFindResource(key, variant, out object? brush) is true
            ? brush
            : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
