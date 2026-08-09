using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RcktMon.FakeServer;

namespace RcktMon.App.UiTests;

/// <summary>
/// The fake server, hosted in-process on a free loopback port.
/// </summary>
/// <remarks>
/// Not a mock: the UI is driven over a real gRPC connection with real protobuf on the
/// wire, because the parts most likely to break — presence semantics, chunked snapshots,
/// the reader-thread handoff — only fail against a real transport.
/// </remarks>
public sealed class FakeServerHost : IDisposable
{
    private readonly WebApplication _app;

    private FakeServerHost(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    public int Port { get; }

    public string Address => $"http://localhost:{Port}";

    /// <summary>The running market, so a test can check the server side of a discrepancy.</summary>
    public MarketHub Hub => _app.Services.GetRequiredService<MarketHub>();

    /// <summary>
    /// Starts the server and blocks until it is listening.
    /// </summary>
    /// <remarks>
    /// Synchronous on purpose: the UI test body runs on Avalonia's dispatcher thread,
    /// where awaiting would deadlock against the loop it is occupying.
    /// </remarks>
    public static FakeServerHost Start(
        int instruments = 120,
        double ticksPerSecond = 4,
        int signalBacklog = 60,
        bool chaos = false)
    {
        int port = FreePort();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(
            new FakeServerOptions
            {
                InstrumentCount = instruments,
                TicksPerSecond = ticksPerSecond,
                SignalBacklog = signalBacklog,
                Port = port,
                Chaos = chaos,
                ChaosDropInterval = TimeSpan.FromSeconds(5),

                // Short window and frequent runs: a UI test cannot wait five synthetic
                // minutes for the first alert to fire.
                SignalWindowTicks = 20,
                RunProbability = 0.02,
            }));

        builder.Services.AddSingleton<MarketHub>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketHub>());
        builder.Services.AddGrpc();

        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.ListenLocalhost(port, listen => listen.Protocols = HttpProtocols.Http2));

        WebApplication app = builder.Build();

        app.MapGrpcService<FakeSessionService>();
        app.MapGrpcService<FakeCatalogService>();
        app.MapGrpcService<FakeMarketDataService>();
        app.MapGrpcService<FakeSignalService>();
        app.MapGrpcService<FakeStatusService>();
        app.MapGrpcService<FakeProfileService>();

        app.StartAsync().GetAwaiter().GetResult();
        return new FakeServerHost(app, port);
    }

    public void Dispose()
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
