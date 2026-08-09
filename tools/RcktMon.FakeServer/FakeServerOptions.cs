namespace RcktMon.FakeServer;

/// <summary>How the fake server should behave for this run.</summary>
public sealed class FakeServerOptions
{
    /// <summary>
    /// Size of the synthetic universe.
    /// </summary>
    /// <remarks>
    /// The default is a deliberate overshoot. MOEX lists a few hundred equities, so 2000
    /// is roughly 4x the real universe — the point is to keep the grid honest under more
    /// load than it will ever see, not to model the exchange. Pass <c>--instruments 500</c>
    /// for a realistic session.
    /// </remarks>
    public int InstrumentCount { get; init; } = 2000;

    /// <summary>Market steps per second. The client's perf target is 2.</summary>
    public double TicksPerSecond { get; init; } = 2;

    /// <summary>Seed for the price walk, so a run is reproducible.</summary>
    public int Seed { get; init; } = 20260809;

    /// <summary>Port to listen on, h2c (no TLS) for local development only.</summary>
    public int Port { get; init; } = 5299;

    /// <summary>
    /// Inject failures: drop the stream periodically, serve deliberately stale sequence
    /// numbers, and stall.
    /// </summary>
    /// <remarks>
    /// Reconnect and degraded-state UX has to be built against real failures. Without
    /// this it gets built against imagined ones and breaks the first time a train goes
    /// through a tunnel.
    /// </remarks>
    public bool Chaos { get; init; }

    /// <summary>How often chaos mode kills an open stream.</summary>
    public TimeSpan ChaosDropInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Length of the rolling window the X-minute alert rules look back over, in ticks.
    /// </summary>
    /// <remarks>
    /// Expressed in ticks rather than minutes so it stays meaningful when the market
    /// clock is sped up. The signals the server emits report the window's real elapsed
    /// duration, so a fast run produces honest one-minute windows rather than pretending
    /// five minutes have passed.
    /// </remarks>
    public int SignalWindowTicks { get; init; } = 60;

    /// <summary>Chance per instrument per tick of starting a sustained directional run.</summary>
    /// <remarks>
    /// Raised well above a realistic market on purpose: without frequent runs the signal
    /// feed stays empty and the parts of the UI that exist to show alerts cannot be
    /// developed. Pass <c>--calm</c> to walk it back to something lifelike.
    /// </remarks>
    public double RunProbability { get; init; } = 0.0025;

    /// <summary>How many historical signals to seed so the feed is populated at startup.</summary>
    public int SignalBacklog { get; init; } = 140;

    public static FakeServerOptions Parse(string[] args)
    {
        var options = new FakeServerOptions();

        int instruments = options.InstrumentCount;
        double rate = options.TicksPerSecond;
        int port = options.Port;
        int seed = options.Seed;
        int window = options.SignalWindowTicks;
        int backlog = options.SignalBacklog;
        double runProbability = options.RunProbability;
        bool chaos = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--instruments" when i + 1 < args.Length:
                    instruments = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--rate" when i + 1 < args.Length:
                    rate = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--port" when i + 1 < args.Length:
                    port = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--seed" when i + 1 < args.Length:
                    seed = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--signal-window" when i + 1 < args.Length:
                    window = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--signal-backlog" when i + 1 < args.Length:
                    backlog = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--calm":
                    runProbability = 0.0006;
                    break;
                case "--chaos":
                    chaos = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }
        }

        return new FakeServerOptions
        {
            InstrumentCount = Math.Clamp(instruments, 1, 20_000),
            TicksPerSecond = Math.Clamp(rate, 0.1, 50),
            Port = port,
            Seed = seed,
            SignalWindowTicks = Math.Clamp(window, 2, 3600),
            SignalBacklog = Math.Clamp(backlog, 0, 5000),
            RunProbability = Math.Clamp(runProbability, 0, 0.2),
            Chaos = chaos,
        };
    }

    private static void PrintUsage() =>
        Console.WriteLine(
            """
            RcktMon fake server — synthetic market data for client development.

              --instruments <n>     universe size (default 2000)
              --rate <n>            market steps per second (default 2)
              --port <n>            listen port, h2c (default 5299)
              --seed <n>            price-walk seed (default 20260809)
              --signal-window <n>   X-minute rule lookback, in ticks (default 60)
              --signal-backlog <n>  historical signals seeded at startup (default 140)
              --calm                realistic run frequency instead of the alert-rich default
              --chaos               drop streams, stall, and serve stale sequence numbers
              --help                this text

            Connect the desktop client to http://localhost:<port> with any access key.
            """);
}
