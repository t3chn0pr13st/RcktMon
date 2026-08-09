namespace RcktMon.Client.Core.Connection;

/// <summary>
/// Everything the client needs to reach a RcktMon server.
/// </summary>
/// <remarks>
/// Note what is <em>not</em> here: there is no broker token. Market data is subscribed by
/// the server using its own T-Invest credentials, so the desktop client never holds one.
/// A user-supplied token will appear later and only for order entry, stored in the OS
/// keychain rather than in this settings object.
/// </remarks>
public sealed class ConnectionSettings
{
    /// <summary>Server base address, e.g. <c>https://api.rcktmon.ru</c>.</summary>
    public required Uri ServerUri { get; init; }

    /// <summary>
    /// Opaque access key issued by the server (<c>rm_…</c>). Held in memory only; the
    /// persisted copy lives in the OS keychain via <c>ISecretStore</c>.
    /// </summary>
    public required string AccessKey { get; init; }

    /// <summary>Stable per-installation id, so the account owner can revoke one machine.</summary>
    public required string DeviceId { get; init; }

    /// <summary>BCP-47 locale used for server-rendered signal headlines.</summary>
    public string Locale { get; init; } = "ru-RU";

    /// <summary>
    /// Requested coalescing interval in milliseconds. The server clamps this to its own
    /// allowed range and to whatever the account's plan permits.
    /// </summary>
    public int TickIntervalMs { get; init; } = 500;

    /// <summary>Ask the server to include order book best bid/ask.</summary>
    public bool WantOrderBook { get; init; }

    /// <summary>Ask the server to include month baselines.</summary>
    public bool WantMonthStats { get; init; } = true;

    /// <summary>Ask the server to include the 60-point sparkline.</summary>
    public bool WantSparkline { get; init; }

    /// <summary>
    /// Allow an unencrypted h2c connection. Only meaningful for local development
    /// against the fake server; a real deployment is TLS on 443.
    /// </summary>
    public bool AllowInsecureHttp { get; init; }
}
