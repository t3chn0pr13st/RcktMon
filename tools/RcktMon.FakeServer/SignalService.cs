using System.Threading.Channels;
using Grpc.Core;
using RcktMon.Protocol.V1;

namespace RcktMon.FakeServer;

/// <summary>Live signals plus the history the feed scrolls back through.</summary>
public sealed class FakeSignalService(MarketHub hub) : SignalService.SignalServiceBase
{
    /// <summary>
    /// Bounded, drop-oldest: a client that stops reading must not grow the server's
    /// memory, and for an alert feed the newest items are the ones worth keeping.
    /// </summary>
    private const int BufferedSignals = 512;

    public override async Task StreamSignals(
        StreamSignalsRequest request,
        IServerStreamWriter<Signal> responseStream,
        ServerCallContext context)
    {
        CancellationToken ct = context.CancellationToken;

        var live = Channel.CreateBounded<Signal>(new BoundedChannelOptions(BufferedSignals)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        void OnFired(Signal signal)
        {
            if (request.Kinds.Count == 0 || request.Kinds.Contains(signal.Kind))
            {
                live.Writer.TryWrite(signal);
            }
        }

        // Subscribe *before* reading the backlog, then skip anything the backlog already
        // covered. The other order has a window in which a signal fires between the read
        // and the subscribe and is silently lost — exactly the failure this product
        // cannot have.
        hub.Signals.Fired += OnFired;

        try
        {
            long highestSent = request.SinceId;

            foreach (Signal signal in hub.Signals.Since(request.SinceId, request.Kinds))
            {
                await responseStream.WriteAsync(signal, ct).ConfigureAwait(false);
                highestSent = Math.Max(highestSent, signal.Id);
            }

            await foreach (Signal signal in live.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (signal.Id <= highestSent)
                {
                    continue;
                }

                await responseStream.WriteAsync(signal, ct).ConfigureAwait(false);
                highestSent = signal.Id;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client went away. Normal.
        }
        finally
        {
            hub.Signals.Fired -= OnFired;
            live.Writer.TryComplete();
        }
    }

    public override Task<ListSignalsResponse> ListSignals(ListSignalsRequest request, ServerCallContext context)
    {
        (IReadOnlyList<Signal> items, string nextPageToken, int total) = hub.Signals.Query(request);

        var response = new ListSignalsResponse
        {
            NextPageToken = nextPageToken,
            TotalCount = total,
        };

        response.Signals.AddRange(items);
        return Task.FromResult(response);
    }
}
