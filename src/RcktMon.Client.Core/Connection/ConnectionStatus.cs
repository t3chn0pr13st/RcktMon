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

/// <summary>Why the connection is in the state it is in.</summary>
/// <remarks>
/// A kind rather than a sentence. This assembly has no business deciding what language
/// the user reads: it reports what happened, and the UI layer turns that into words.
/// Before this existed, the transport produced Russian prose, which made translating the
/// app impossible without touching the networking code.
/// </remarks>
public enum ConnectionFaultKind
{
    /// <summary>Nothing wrong.</summary>
    None,

    /// <summary>The server ended the stream cleanly. Still a disconnect.</summary>
    StreamClosed,

    /// <summary>Refused to open a cleartext connection to a non-loopback address.</summary>
    InsecureAddress,

    /// <summary>The access key was rejected or revoked.</summary>
    KeyRejected,

    /// <summary>The server requires a newer protocol major than this client implements.</summary>
    ClientTooOld,

    /// <summary>Anything else the transport reported. <see cref="ConnectionStatus.Detail"/> carries it.</summary>
    Transport,

    /// <summary>The server is skipping ticks for this session because it cannot keep up.</summary>
    DroppedTicks,

    /// <summary>Quotes stopped arriving while the exchange is open.</summary>
    QuotesStale,
}

/// <summary>
/// A fault and the values needed to describe it.
/// </summary>
/// <param name="Kind">What went wrong.</param>
/// <param name="Argument">First substitution, e.g. the address or the dropped-tick count.</param>
/// <param name="SecondArgument">Second substitution, used by <see cref="ConnectionFaultKind.ClientTooOld"/>.</param>
/// <remarks>
/// Arguments are strings so the whole status stays a comparable value type — the
/// supervisor publishes only on a real change, and that check is an equality test.
/// </remarks>
public readonly record struct ConnectionFault(
    ConnectionFaultKind Kind,
    string? Argument = null,
    string? SecondArgument = null)
{
    public static ConnectionFault None { get; } = new(ConnectionFaultKind.None);

    public static ConnectionFault Of(ConnectionFaultKind kind) => new(kind);
}

/// <summary>A connection state change, with enough detail for the UI to explain itself.</summary>
/// <param name="State">The new state.</param>
/// <param name="Fault">Why, in a form the UI can translate.</param>
/// <param name="Detail">
/// Raw technical text from the transport, for tooltips and logs. Never translated, because
/// it is a gRPC status detail rather than a sentence written for a user.
/// </param>
/// <param name="RetryIn">When reconnecting, how long until the next attempt.</param>
public readonly record struct ConnectionStatus(
    ConnectionState State,
    ConnectionFault Fault = default,
    string? Detail = null,
    TimeSpan? RetryIn = null)
{
    /// <summary>True while the client is showing data it believes to be current.</summary>
    public bool IsUsable => State is ConnectionState.Connected or ConnectionState.Degraded;

    /// <summary>True when retrying cannot possibly help and the user must act.</summary>
    public bool IsTerminal => State is ConnectionState.Unauthorized or ConnectionState.VersionMismatch;
}
