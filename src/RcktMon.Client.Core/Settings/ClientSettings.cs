using RcktMon.Client.Core.Market;

namespace RcktMon.Client.Core.Settings;

/// <summary>Which theme the user asked for. <see cref="System"/> follows the OS.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Everything the client persists between runs, minus secrets.
/// </summary>
/// <remarks>
/// <para>
/// Mutable and plainly serialisable on purpose — this is a settings file, not a domain
/// model. What matters is that it is written through <see cref="JsonFileStore{T}"/> to an
/// absolute path under <c>IAppPaths</c>: the pre-rewrite build wrote <c>settings.json</c>,
/// <c>columns.json</c> and <c>WindowPos.json</c> as bare relative names, which resolve
/// against the process working directory and break the moment the app is a macOS bundle.
/// </para>
/// <para>
/// The access key is deliberately absent. It lives in <c>ISecretStore</c>.
/// </para>
/// </remarks>
public sealed class ClientSettings
{
    /// <summary>Server base address. Plaintext http is only accepted for loopback.</summary>
    public string ServerAddress { get; set; } = "http://localhost:5299";

    /// <summary>Stable per-installation id. Not a secret; the account owner can revoke it.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>BCP-47 UI language. Empty means follow the OS.</summary>
    public string Locale { get; set; } = string.Empty;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>Tighter grid rows, for users who want more instruments on screen.</summary>
    public bool CompactDensity { get; set; }

    /// <summary>Sparkline column. Off by default: it is the most expensive cell to realise.</summary>
    public bool ShowSparklines { get; set; }

    /// <summary>Blue/orange instead of red/green, and never colour alone.</summary>
    public bool ColourBlindSafe { get; set; }

    /// <summary>Requested coalescing interval; the server clamps it.</summary>
    public int TickIntervalMs { get; set; } = 500;

    /// <summary>Set once the user has been through onboarding.</summary>
    public bool OnboardingCompleted { get; set; }

    /// <summary>Page to open on launch.</summary>
    public string StartPage { get; set; } = "monitor";

    /// <summary>Instruments the user starred, by uid.</summary>
    public List<string> Watchlist { get; set; } = [];

    /// <summary>Name of the saved view the monitor should restore.</summary>
    public string ActiveView { get; set; } = string.Empty;

    /// <summary>Last window geometry, validated against the current screens on restore.</summary>
    public WindowLayout Window { get; set; } = new();
}

/// <summary>Window position and size, in screen coordinates.</summary>
public sealed class WindowLayout
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 1360;

    public double Height { get; set; } = 860;

    public bool Maximized { get; set; }

    /// <summary>False until the window has been placed once, so first run can centre it.</summary>
    public bool HasPosition { get; set; }
}

/// <summary>
/// A named monitor configuration: filters, sort and columns.
/// </summary>
/// <remarks>
/// This subsumes the pre-rewrite <c>columns.json</c>, which stored widths keyed by the
/// literal header string — so localising a header silently lost the user's layout.
/// Columns here are keyed by a stable id that never appears on screen.
/// </remarks>
public sealed class SavedView
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Presets ship with the app and cannot be overwritten, only copied.</summary>
    public bool IsPreset { get; set; }

    public string SearchText { get; set; } = string.Empty;

    public bool GainersOnly { get; set; }

    public bool LosersOnly { get; set; }

    public bool WatchlistOnly { get; set; }

    /// <summary>Only instruments trading above their monthly average volume.</summary>
    public bool UnusualVolumeOnly { get; set; }

    /// <summary>Only instruments that produced a signal today.</summary>
    public bool SignalledOnly { get; set; }

    /// <summary>Currencies to include. Empty means all.</summary>
    public List<string> Currencies { get; set; } = [];

    /// <summary>Exchanges to include. Empty means all.</summary>
    public List<string> Exchanges { get; set; } = [];

    public MonitorColumn SortColumn { get; set; } = MonitorColumn.DayChange;

    public bool SortDescending { get; set; } = true;

    public List<ColumnLayout> Columns { get; set; } = [];

    public SavedView Copy(string name) => new()
    {
        Name = name,
        IsPreset = false,
        SearchText = SearchText,
        GainersOnly = GainersOnly,
        LosersOnly = LosersOnly,
        WatchlistOnly = WatchlistOnly,
        UnusualVolumeOnly = UnusualVolumeOnly,
        SignalledOnly = SignalledOnly,
        Currencies = [.. Currencies],
        Exchanges = [.. Exchanges],
        SortColumn = SortColumn,
        SortDescending = SortDescending,
        Columns = [.. Columns.Select(c => new ColumnLayout
        {
            Id = c.Id,
            IsVisible = c.IsVisible,
            Width = c.Width,
            Order = c.Order,
        })],
    };
}

/// <summary>One column's persisted state, keyed by a stable id rather than its header.</summary>
public sealed class ColumnLayout
{
    public string Id { get; set; } = string.Empty;

    public bool IsVisible { get; set; } = true;

    public double Width { get; set; }

    public int Order { get; set; }
}

/// <summary>The saved views file.</summary>
public sealed class ViewCollection
{
    public List<SavedView> Views { get; set; } = [];
}
