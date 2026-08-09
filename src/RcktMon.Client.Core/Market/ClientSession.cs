using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RcktMon.Client.Core.Connection;

namespace RcktMon.Client.Core.Market;

/// <summary>What one drain pass applied, so the UI knows what to react to.</summary>
/// <param name="QuoteMessages">Quote messages applied.</param>
/// <param name="NewSignals">Signals seen for the first time, oldest first.</param>
/// <param name="StatusChanged">True when a fresh server status arrived.</param>
public readonly record struct DrainResult(
    int QuoteMessages,
    IReadOnlyList<SignalEntry> NewSignals,
    bool StatusChanged)
{
    /// <summary>True when nothing at all happened, so the UI can skip its own work.</summary>
    public bool IsEmpty => QuoteMessages == 0 && NewSignals.Count == 0 && !StatusChanged;
}

/// <summary>
/// Everything one connected server session owns: the three stores and the supervisor
/// that feeds them.
/// </summary>
/// <remarks>
/// The stores are separate because they answer different questions, but they have to be
/// drained together on one UI-thread pass — a signal has to be able to mark its row in
/// the same frame the row's price updates, and health has to be applied before either.
/// Leaving that ordering to the view models is how it ends up subtly different on each
/// screen.
/// </remarks>
public sealed class ClientSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    private bool _staleApplied;

    public ClientSession(ConnectionSettings settings, ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;

        Quotes = new QuoteStore();
        Signals = new SignalStore();
        MarketStatus = new MarketStatusStore();

        Connection = new RcktMonConnection(
            settings,
            Quotes,
            loggerFactory.CreateLogger<RcktMonConnection>(),
            Signals,
            MarketStatus);
    }

    public QuoteStore Quotes { get; }

    public SignalStore Signals { get; }

    public MarketStatusStore MarketStatus { get; }

    public RcktMonConnection Connection { get; }

    /// <summary>Starts the supervisor. It runs until the session is disposed.</summary>
    public Task Start() => Connection.RunAsync(_lifetime.Token);

    /// <summary>Applies everything queued across all three stores. Call from the UI thread.</summary>
    public DrainResult Drain()
    {
        // Health first: whether the data is trustworthy has to be settled before anything
        // is drawn from it.
        bool statusChanged = MarketStatus.Drain();
        if (statusChanged)
        {
            ApplyStaleFlag();
        }

        int quotes = Quotes.Drain();

        IReadOnlyList<SignalEntry> signals = Signals.Drain();
        foreach (SignalEntry signal in signals)
        {
            // The accent belongs to the instrument, not to the feed, so it is applied to
            // the row here rather than being recomputed by the grid.
            Quotes.MarkSignal(signal.InstrumentUid, signal.FiredAt);
        }

        return new DrainResult(quotes, signals, statusChanged);
    }

    private void ApplyStaleFlag()
    {
        bool degraded = MarketStatus.Latest?.MarketData?.Degraded is true;

        // Only on a transition: marking 2000 rows raises 2000 property changes, and doing
        // that twice a second because the flag happened to still be true would be
        // pointless work in the one place the app cannot afford it.
        if (degraded == _staleApplied)
        {
            return;
        }

        _staleApplied = degraded;
        Quotes.SetStale(degraded);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);

        await Connection.DisposeAsync().ConfigureAwait(false);

        Signals.Dispose();
        Quotes.Dispose();
        _lifetime.Dispose();
    }
}
