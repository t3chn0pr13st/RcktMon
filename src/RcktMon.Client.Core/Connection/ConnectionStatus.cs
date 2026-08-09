namespace RcktMon.Client.Core.Connection;

/// <summary>
/// What the client is currently doing about its server connection.
/// </summary>
public enum ConnectionState
{
    /// <summary>Not started, or deliberately stopped.</summary>
    Disconnected,

    /// <summary>First attempt in progress.</summary>
    Connecting,

    /// <summary>Streaming normally.</summary>
    Connected,

    /// <summary>Lost the stream, backing off before the next attempt.</summary>
    Reconnecting,

    /// <summary>
    /// Connected, but the data is not trustworthy: the server reports shard outages or
    /// is dropping ticks for this session, or quotes have gone stale during an open
    /// session.
    /// </summary>
    /// <remarks>
    /// This state exists because silently stale prices are the dangerous failure mode of
    /// a monitoring product. It must be visible on every screen, not buried in a log.
    /// </remarks>
    Degraded,

    /// <summary>The access key was rejected or revoked. Retrying will not help.</summary>
    Unauthorized,

    /// <summary>This client is too old for the server. Requires an update.</summary>
    VersionMismatch,
}

/// <summary>A connection state change, with enough detail for the UI to explain itself.</summary>
/// <param name="State">The new state.</param>
/// <param name="Detail">Human-readable reason, already localised where it comes from the server.</param>
/// <param name="RetryIn">When reconnecting, how long until the next attempt.</param>
public readonly record struct ConnectionStatus(
    ConnectionState State,
    string? Detail = null,
    TimeSpan? RetryIn = null)
{
    /// <summary>True while the client is showing data it believes to be current.</summary>
    public bool IsUsable => State is ConnectionState.Connected or ConnectionState.Degraded;

    /// <summary>True when retrying cannot possibly help and the user must act.</summary>
    public bool IsTerminal => State is ConnectionState.Unauthorized or ConnectionState.VersionMismatch;
}
