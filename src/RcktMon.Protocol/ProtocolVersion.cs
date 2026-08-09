namespace RcktMon.Protocol;

/// <summary>
/// The wire-protocol version this assembly implements.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Major"/> is the compatibility gate. The server refuses a client whose
/// major is below its own <c>min_supported_client_major</c>, and the client must stop
/// and prompt for an update rather than carry on against a contract it does not
/// understand — a monitoring app that silently shows wrong or missing alerts is worse
/// than one that refuses to start.
/// </para>
/// <para>
/// Bump <see cref="Major"/> only for a breaking change: removing or renaming a field,
/// changing a field's type or number, or changing the meaning of an existing value.
/// Additive changes — new messages, new RPCs, new optional fields — bump
/// <see cref="Minor"/> and require no client update.
/// </para>
/// </remarks>
public static class ProtocolVersion
{
    /// <summary>Breaking version. Must match <c>rcktmon.v1</c> in the proto package name.</summary>
    public const int Major = 1;

    /// <summary>Additive version. Incremented for backwards-compatible additions.</summary>
    public const int Minor = 0;

    /// <summary>Human-readable form, e.g. <c>"1.0"</c>.</summary>
    public static string Display => $"{Major}.{Minor}";
}

/// <summary>
/// Well-known capability keys carried in <c>HelloResponse.capabilities</c>.
/// </summary>
/// <remarks>
/// Feature flags are resolved server-side from the account and its plan. This replaces
/// the pre-rewrite approach of gating features on <c>Environment.MachineName</c>.
/// Unknown keys must be ignored by the client, so the server can roll out a capability
/// before every client understands it.
/// </remarks>
public static class Capabilities
{
    /// <summary>Order book depth is available on the quote stream.</summary>
    public const string OrderBook = "orderbook";

    /// <summary>The anonymised trades tape is available.</summary>
    public const string TradesTape = "trades_tape";

    /// <summary>Server-rendered candlestick charts are available.</summary>
    public const string Charts = "charts";

    /// <summary>Commands may be routed to the browser terminal extension.</summary>
    public const string TerminalBridge = "terminal_bridge";

    /// <summary>Telegram delivery can be configured for this account.</summary>
    public const string TelegramAlerts = "telegram_alerts";

    /// <summary>Historical signal search beyond the current session.</summary>
    public const string SignalHistory = "signal_history";

    /// <summary>Order entry. Reserved: the client-side trading token is not implemented yet.</summary>
    public const string Trading = "trading";
}
