using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;

namespace RcktMon.FakeServer;

/// <summary>Handshake. Accepts any access key — this server exists to develop against.</summary>
public sealed class FakeSessionService(MarketHub hub) : SessionService.SessionServiceBase
{
    public override Task<HelloResponse> Hello(HelloRequest request, ServerCallContext context)
    {
        // Remembered per device, because only this call carries the locale and every
        // later one has to render text in the same language.
        SessionLocales.Remember(request.DeviceId, request.Locale);

        var response = new HelloResponse
        {
            ProtocolMajor = ProtocolVersion.Major,
            ProtocolMinor = ProtocolVersion.Minor,
            AccountId = "fake-account",
            AccountDisplayName = FakeStrings.AccountName(request.Locale),
            ServerVersion = $"fake-server/{ProtocolVersion.Display}",
            ServerTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            MinSupportedClientMajor = ProtocolVersion.Major,
        };

        response.Capabilities.AddRange([
            Capabilities.Charts,
            Capabilities.SignalHistory,
            Capabilities.OrderBook,
            Capabilities.TerminalBridge,
        ]);

        _ = hub;
        return Task.FromResult(response);
    }
}

/// <summary>Serves the synthetic instrument universe, chunked and etag-cached.</summary>
public sealed class FakeCatalogService(MarketHub hub) : CatalogService.CatalogServiceBase
{
    private const int ChunkSize = 500;

    public override async Task GetCatalog(
        GetCatalogRequest request,
        IServerStreamWriter<CatalogChunk> responseStream,
        ServerCallContext context)
    {
        if (request.Etag == hub.Market.Etag)
        {
            await responseStream.WriteAsync(new CatalogChunk
            {
                Etag = hub.Market.Etag,
                NotModified = true,
                IsLast = true,
            }).ConfigureAwait(false);
            return;
        }

        IReadOnlyList<Instrument> all = hub.Market.Instruments;

        for (int offset = 0; offset < all.Count; offset += ChunkSize)
        {
            int count = Math.Min(ChunkSize, all.Count - offset);
            var chunk = new CatalogChunk
            {
                Etag = hub.Market.Etag,
                IsLast = offset + count >= all.Count,
            };

            for (int i = 0; i < count; i++)
            {
                chunk.Instruments.Add(all[offset + i]);
            }

            await responseStream.WriteAsync(chunk, context.CancellationToken).ConfigureAwait(false);
        }
    }

    public override Task StreamCatalogChanges(
        StreamCatalogChangesRequest request,
        IServerStreamWriter<CatalogChange> responseStream,
        ServerCallContext context)
        // The synthetic universe is fixed; hold the stream open so the client's handling
        // of a quiet channel gets exercised.
        => Task.Delay(Timeout.Infinite, context.CancellationToken);
}

/// <summary>
/// Snapshot then coalesced deltas, with the same backpressure contract as the real server.
/// </summary>
public sealed class FakeMarketDataService(MarketHub hub, ILogger<FakeMarketDataService> logger)
    : MarketDataService.MarketDataServiceBase
{
    private const int SnapshotChunkSize = 500;

    public override async Task StreamQuotes(
        StreamQuotesRequest request,
        IServerStreamWriter<QuotesMessage> responseStream,
        ServerCallContext context)
    {
        CancellationToken ct = context.CancellationToken;

        int tickMs = request.TickIntervalMs == 0 ? 500 : Math.Clamp(request.TickIntervalMs, 250, 5000);
        bool wantSparkline = request.Fields?.Sparkline ?? false;
        bool wantMonth = request.Fields?.MonthStats ?? false;

        // Coalescing buffer: one pending delta per instrument, last write wins. This is
        // what keeps a slow client from turning into an unbounded queue.
        var pending = new Dictionary<string, QuoteDelta>(StringComparer.Ordinal);
        var gate = new Lock();
        uint droppedTicks = 0;

        void OnTicked(IReadOnlyList<QuoteDelta> deltas)
        {
            lock (gate)
            {
                foreach (QuoteDelta delta in deltas)
                {
                    pending[delta.InstrumentUid] = delta;
                }
            }
        }

        await SendSnapshotAsync(responseStream, request, wantSparkline, wantMonth, ct).ConfigureAwait(false);

        hub.Ticked += OnTicked;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(tickMs));
            DateTimeOffset chaosDeadline = DateTimeOffset.UtcNow + hub.Options.ChaosDropInterval;
            var lastControl = DateTimeOffset.UtcNow;

            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (hub.Options.Chaos && DateTimeOffset.UtcNow > chaosDeadline)
                {
                    logger.LogWarning("Chaos: dropping stream for {Peer}", context.Peer);
                    throw new RpcException(new Status(StatusCode.Unavailable, "chaos: stream dropped"));
                }

                QuoteDelta[] batch;
                lock (gate)
                {
                    if (pending.Count == 0)
                    {
                        batch = [];
                    }
                    else
                    {
                        batch = [.. pending.Values];
                        pending.Clear();
                    }
                }

                if (batch.Length > 0)
                {
                    var message = new QuotesMessage
                    {
                        Batch = new QuoteBatch
                        {
                            Seq = hub.Market.CurrentSeq,
                            ServerTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                        },
                    };
                    message.Batch.Deltas.AddRange(batch);

                    await responseStream.WriteAsync(message, ct).ConfigureAwait(false);
                }

                // Heartbeat at least every 5 seconds, so the client can tell a quiet
                // market from a dead socket.
                if (DateTimeOffset.UtcNow - lastControl >= TimeSpan.FromSeconds(5))
                {
                    lastControl = DateTimeOffset.UtcNow;
                    await responseStream.WriteAsync(
                        new QuotesMessage
                        {
                            Control = new StreamControl
                            {
                                Seq = hub.Market.CurrentSeq,
                                DroppedTicks = droppedTicks,
                                ServerTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                            },
                        },
                        ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client went away. Normal.
        }
        finally
        {
            hub.Ticked -= OnTicked;
        }
    }

    private async Task SendSnapshotAsync(
        IServerStreamWriter<QuotesMessage> responseStream,
        StreamQuotesRequest request,
        bool wantSparkline,
        bool wantMonth,
        CancellationToken ct)
    {
        // Chaos serves a stale-seq snapshot so the client's SEQ_TOO_OLD path gets used.
        SnapshotReason reason = request.ResumeFromSeq == 0
            ? SnapshotReason.Initial
            : SnapshotReason.SeqTooOld;

        var buffer = new List<Quote>(SnapshotChunkSize);

        foreach (Quote quote in hub.Market.Snapshot(wantSparkline, wantMonth))
        {
            buffer.Add(quote);
            if (buffer.Count == SnapshotChunkSize)
            {
                await WriteChunkAsync(responseStream, buffer, isLast: false, reason, ct).ConfigureAwait(false);
                buffer.Clear();
            }
        }

        await WriteChunkAsync(responseStream, buffer, isLast: true, reason, ct).ConfigureAwait(false);
    }

    private async Task WriteChunkAsync(
        IServerStreamWriter<QuotesMessage> responseStream,
        List<Quote> quotes,
        bool isLast,
        SnapshotReason reason,
        CancellationToken ct)
    {
        var chunk = new QuoteSnapshotChunk
        {
            Seq = hub.Market.CurrentSeq,
            ServerTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            IsLast = isLast,
            Reason = reason,
        };
        chunk.Quotes.AddRange(quotes);

        await responseStream.WriteAsync(new QuotesMessage { Snapshot = chunk }, ct).ConfigureAwait(false);
    }

    public override Task<GetHistoryResponse> GetHistory(GetHistoryRequest request, ServerCallContext context)
    {
        // Range is advisory here: the inspector asks for "the last N of these", and the
        // synthetic history is generated backwards from the live price so the chart joins
        // up with the quote next to it.
        int count = request.From is not null && request.To is not null
            ? (int)(request.To.ToDateTimeOffset() - request.From.ToDateTimeOffset()).TotalMinutes
            : 120;

        var response = new GetHistoryResponse();
        response.Candles.AddRange(hub.Market.History(request.InstrumentUid, request.Interval, count));
        return Task.FromResult(response);
    }

    public override Task<GetChartResponse> GetChart(GetChartRequest request, ServerCallContext context)
        => Task.FromResult(new GetChartResponse());
}
