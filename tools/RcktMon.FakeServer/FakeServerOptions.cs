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

    public static FakeServerOptions Parse(string[] args)
    {
        var options = new FakeServerOptions();

        int instruments = options.InstrumentCount;
        double rate = options.TicksPerSecond;
        int port = options.Port;
        int seed = options.Seed;
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
            Chaos = chaos,
        };
    }

    private static void PrintUsage() =>
        Console.WriteLine(
            """
            RcktMon fake server — synthetic market data for client development.

              --instruments <n>   universe size (default 2000)
              --rate <n>          market steps per second (default 2)
              --port <n>          listen port, h2c (default 5299)
              --seed <n>          price-walk seed (default 20260809)
              --chaos             drop streams, stall, and serve stale sequence numbers
              --help              this text

            Connect the desktop client to http://localhost:<port> with any access key.
            """);
}
