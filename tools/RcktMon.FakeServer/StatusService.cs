using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using RcktMon.Protocol.V1;

namespace RcktMon.FakeServer;

/// <summary>
/// Exchange sessions and market-data health.
/// </summary>
/// <remarks>
/// This stream is what lets the client tell "the market is closed" apart from "our data
/// pipeline is broken". Both look like a grid that has stopped moving, and conflating
/// them is the failure mode a monitoring product cannot afford, so the fake server
/// reports the distinction honestly — including a genuinely degraded state under chaos.
/// </remarks>
public sealed class FakeStatusService(MarketHub hub) : StatusService.StatusServiceBase
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>Shard count the real server plans for; see the 32-stream budget.</summary>
    private const int ShardsTotal = 32;

    public override async Task StreamStatus(
        StreamStatusRequest request,
        IServerStreamWriter<ServerStatus> responseStream,
        ServerCallContext context)
    {
        CancellationToken ct = context.CancellationToken;

        try
        {
            string locale = SessionLocales.For(context);

            // Send immediately rather than waiting out the first interval: the dashboard
            // shows an empty session tile until this arrives.
            await responseStream.WriteAsync(Build(locale), ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(Build(locale), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client went away. Normal.
        }
    }

    private ServerStatus Build(string locale)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Under chaos a couple of shards are reported down, so the "degraded" pill and
        // the per-instrument stale marking have something real to render.
        int healthy = hub.Options.Chaos ? ShardsTotal - 2 : ShardsTotal;
        bool degraded = healthy < ShardsTotal;

        var status = new ServerStatus
        {
            ServerTime = Timestamp.FromDateTimeOffset(now),
            ConnectedClients = 1,
            MarketData = new MarketDataHealth
            {
                ShardsTotal = ShardsTotal,
                ShardsHealthy = healthy,
                InstrumentsSubscribed = hub.Market.Instruments.Count,
                LastMarketEvent = Timestamp.FromDateTimeOffset(
                    hub.LastTickAt == default ? hub.StartedAt : hub.LastTickAt),
                Degraded = degraded,
                DegradedReason = degraded
                    ? FakeStrings.ShardsDown(ShardsTotal - healthy, ShardsTotal, locale)
                    : string.Empty,
            },
        };

        status.Sessions.AddRange(TradingCalendar.Sessions(now));
        return status;
    }
}
