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
    }

    public SyntheticMarket Market { get; }

    public FakeServerOptions Options { get; }

    /// <summary>Raised on every market step with the instruments that actually moved.</summary>
    public event Action<IReadOnlyList<Protocol.V1.QuoteDelta>>? Ticked;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(1 / Options.TicksPerSecond);
        using var timer = new PeriodicTimer(period);

        _logger.LogInformation(
            "Synthetic market: {Count} instruments at {Rate} ticks/s (seed {Seed}){Chaos}",
            Options.InstrumentCount,
            Options.TicksPerSecond,
            Options.Seed,
            Options.Chaos ? ", CHAOS ENABLED" : string.Empty);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            IReadOnlyList<Protocol.V1.QuoteDelta> changed = Market.Tick();
            if (changed.Count > 0)
            {
                Ticked?.Invoke(changed);
            }
        }
    }
}
