using Microsoft.AspNetCore.Server.Kestrel.Core;
using RcktMon.FakeServer;

FakeServerOptions options = FakeServerOptions.Parse(args);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
builder.Services.AddSingleton<MarketHub>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketHub>());
builder.Services.AddGrpc(grpc =>
{
    grpc.MaxSendMessageSize = 16 * 1024 * 1024;
    grpc.EnableDetailedErrors = true;
});

builder.WebHost.ConfigureKestrel(kestrel =>
{
    // h2c: cleartext HTTP/2, no TLS. Acceptable only because this binds to loopback and
    // serves synthetic data. The real server is TLS on 443, and the client refuses a
    // plaintext address unless AllowInsecureHttp is set explicitly.
    kestrel.ListenLocalhost(options.Port, listen => listen.Protocols = HttpProtocols.Http2);
});

WebApplication app = builder.Build();

app.MapGrpcService<FakeSessionService>();
app.MapGrpcService<FakeCatalogService>();
app.MapGrpcService<FakeMarketDataService>();
app.MapGrpcService<FakeSignalService>();
app.MapGrpcService<FakeStatusService>();
app.MapGrpcService<FakeProfileService>();

app.MapGet("/", () =>
    $"RcktMon fake server — {options.InstrumentCount} instruments at {options.TicksPerSecond}/s. " +
    "gRPC only; point the desktop client at http://localhost:" + options.Port);

app.Logger.LogInformation("Listening on http://localhost:{Port} (h2c)", options.Port);

await app.RunAsync();
