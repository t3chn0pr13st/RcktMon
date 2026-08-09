using System.Net;
using System.Reactive.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RcktMon.Client.Core.Connection;
using RcktMon.Client.Core.Market;
using RcktMon.FakeServer;
using Xunit;

namespace RcktMon.Client.Core.Tests;

/// <summary>
/// The Phase 1 gate, made executable: real gRPC over a real socket, from the fake server
/// through the connection supervisor into the quote store.
/// </summary>
/// <remarks>
/// This is deliberately not a mock. The parts most likely to break — protobuf presence
/// semantics, chunked snapshots, the delta contract, and the reader-thread-to-store
/// handoff — only fail against a real wire.
/// </remarks>
[Collection("e2e")]
public sealed class EndToEndStreamTests : IAsyncLifetime
{
    private WebApplication? _server;
    private int _port;

    public async ValueTask InitializeAsync()
    {
        _port = FindFreePort();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(
            new FakeServerOptions
            {
                // Small and fast: this test is about the wire, not about load.
                InstrumentCount = 120,
                TicksPerSecond = 20,
                Port = _port,
            }));
        builder.Services.AddSingleton<MarketHub>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketHub>());
        builder.Services.AddGrpc();

        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.ListenLocalhost(_port, listen => listen.Protocols = HttpProtocols.Http2));

        _server = builder.Build();
        _server.MapGrpcService<FakeSessionService>();
        _server.MapGrpcService<FakeCatalogService>();
        _server.MapGrpcService<FakeMarketDataService>();

        await _server.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.StopAsync();
            await _server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Client_receives_catalog_snapshot_and_live_deltas()
    {
        using var store = new QuoteStore();
        await using var connection = new RcktMonConnection(Settings(), store, NullLogger<RcktMonConnection>.Instance);

        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task run = connection.RunAsync(lifetime.Token);

        // The store is drained by the UI thread in the real app; here the test drives it.
        await WaitFor(() =>
        {
            store.Drain();
            return store.Count > 0;
        }, lifetime.Token);

        Assert.Equal(120, store.Count);

        InstrumentRow first = store.Lookup(FirstUid(store))!;

        // Catalog identity arrived, not just quote data.
        Assert.False(string.IsNullOrEmpty(first.Ticker));
        Assert.False(string.IsNullOrEmpty(first.Currency));
        Assert.True(first.Price > 0m, "snapshot should carry a price");

        ulong seqAfterSnapshot = store.LastSeq;

        // Now prove deltas keep arriving and actually mutate state.
        await WaitFor(() =>
        {
            store.Drain();
            return store.LastSeq > seqAfterSnapshot;
        }, lifetime.Token);

        Assert.True(store.LastSeq > seqAfterSnapshot);

        await lifetime.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Handshake_reports_the_account_and_capabilities()
    {
        using var store = new QuoteStore();
        await using var connection = new RcktMonConnection(Settings(), store, NullLogger<RcktMonConnection>.Instance);

        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task run = connection.RunAsync(lifetime.Token);

        await WaitFor(() => connection.Session is not null, lifetime.Token);

        Assert.Equal("Локальный стенд", connection.Session!.AccountDisplayName);
        Assert.True(connection.Can(Protocol.Capabilities.Charts));
        Assert.False(connection.Can(Protocol.Capabilities.Trading));

        await lifetime.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Connection_reports_connected_once_data_flows()
    {
        using var store = new QuoteStore();
        await using var connection = new RcktMonConnection(Settings(), store, NullLogger<RcktMonConnection>.Instance);

        var seen = new List<ConnectionState>();
        using IDisposable subscription = connection.Status.Subscribe(s => seen.Add(s.State));

        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task run = connection.RunAsync(lifetime.Token);

        await WaitFor(() => connection.CurrentStatus.State == ConnectionState.Connected, lifetime.Token);

        Assert.Contains(ConnectionState.Connecting, seen);
        Assert.Contains(ConnectionState.Connected, seen);
        Assert.True(connection.CurrentStatus.IsUsable);

        await lifetime.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Refuses_a_plaintext_address_unless_explicitly_allowed()
    {
        // Guards against shipping a build that silently talks to the production server
        // over cleartext because a dev flag leaked into a release config.
        using var store = new QuoteStore();

        var settings = new ConnectionSettings
        {
            ServerUri = new Uri($"http://localhost:{_port}"),
            AccessKey = "rm_test",
            DeviceId = "11111111-1111-1111-1111-111111111111",
            AllowInsecureHttp = false,
        };

        await using var connection = new RcktMonConnection(settings, store, NullLogger<RcktMonConnection>.Instance);

        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task run = connection.RunAsync(lifetime.Token);

        await WaitFor(
            () => connection.CurrentStatus.State == ConnectionState.Reconnecting &&
                  connection.CurrentStatus.Detail?.Contains("незашифрован", StringComparison.Ordinal) is true,
            lifetime.Token);

        await lifetime.CancelAsync();
        await run;
    }

    private ConnectionSettings Settings() => new()
    {
        ServerUri = new Uri($"http://localhost:{_port}"),
        AccessKey = "rm_test",
        DeviceId = "11111111-1111-1111-1111-111111111111",
        AllowInsecureHttp = true,
        TickIntervalMs = 250,
    };

    private static string FirstUid(QuoteStore store)
    {
        // Instrument 0 in the synthetic universe is always SBER.
        for (int i = 0; i < 120; i++)
        {
            string uid = $"synthetic-{i:D5}-{Guid.Empty}";
            if (store.Lookup(uid) is not null)
            {
                return uid;
            }
        }

        throw new InvalidOperationException("No synthetic instruments in the store.");
    }

    private static async Task WaitFor(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(50, ct);
        }
    }

    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
