using Microsoft.Extensions.Options;

namespace RcktMon.FakeServer;

/// <summary>Drives the synthetic market clock and fans ticks out to open streams.</summary>
public sealed class MarketHub : BackgroundService
{
    private readonly ILogger<MarketHub> _logger;

    public MarketHub(IOptions<FakeServerOptions> options, ILogger<MarketHub> logger)
    {
        Options = options.Value;
        _logger = logger;

        Market = new SyntheticMarket(Options);
        Signals = new SignalLog();
        _engine = new SignalEngine(Options, Signals, Market);

        Signals.Seed(Market, Options.SignalBacklog, Options.Seed, _engine.WindowMinutes);

        StartedAt = DateTimeOffset.UtcNow;
    }

    private readonly SignalEngine _engine;

    public SyntheticMarket Market { get; }

    public SignalLog Signals { get; }

    public FakeServerOptions Options { get; }

    /// <summary>When this process started, so the status stream can report an uptime.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Most recent market step, for the status service's "last event" field.</summary>
    public DateTimeOffset LastTickAt { get; private set; }

    /// <summary>Raised on every market step with the instruments that actually moved.</summary>
    public event Action<IReadOnlyList<Protocol.V1.QuoteDelta>>? Ticked;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(1 / Options.TicksPerSecond);
        using var timer = new PeriodicTimer(period);

        _logger.LogInformation(
            "Synthetic market: {Count} instruments at {Rate} ticks/s (seed {Seed}), " +
            "{Window}-minute signal window, {Backlog} seeded signals{Chaos}",
            Options.InstrumentCount,
            Options.TicksPerSecond,
            Options.Seed,
            _engine.WindowMinutes,
            Options.SignalBacklog,
            Options.Chaos ? ", CHAOS ENABLED" : string.Empty);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            MarketTick tick = Market.Tick();
            LastTickAt = tick.At;

            if (tick.Deltas.Count > 0)
            {
                Ticked?.Invoke(tick.Deltas);
            }

            // Signals are evaluated after the quote fan-out so a slow subscriber cannot
            // delay the alert path, which is the one thing this product must not drop.
            _engine.Evaluate(tick);
        }
    }
}
