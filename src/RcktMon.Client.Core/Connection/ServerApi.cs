using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using RcktMon.Protocol.V1;

namespace RcktMon.Client.Core.Connection;

/// <summary>
/// Alert-profile calls, bound to the connection's authenticated invoker.
/// </summary>
/// <remarks>
/// A thin wrapper rather than handing the raw generated stub to the view models: it keeps
/// the cancellation and deadline policy in one place, and it means a view model never
/// holds a channel it could accidentally outlive.
/// </remarks>
public sealed class ProfileClient(CallInvoker invoker)
{
    /// <summary>Unary calls are user-facing; a hung one has to surface, not hang the pane.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    private readonly ProfileService.ProfileServiceClient _client = new(invoker);

    public async Task<ListProfilesResponse> ListAsync(CancellationToken ct) =>
        await _client.ListProfilesAsync(
            new ListProfilesRequest(),
            deadline: DateTime.UtcNow + Deadline,
            cancellationToken: ct);

    public async Task<AlertProfile> UpsertAsync(AlertProfile profile, CancellationToken ct) =>
        await _client.UpsertProfileAsync(
            new UpsertProfileRequest { Profile = profile },
            deadline: DateTime.UtcNow + Deadline,
            cancellationToken: ct);

    public async Task DeleteAsync(string id, CancellationToken ct) =>
        await _client.DeleteProfileAsync(
            new DeleteProfileRequest { Id = id },
            deadline: DateTime.UtcNow + Deadline,
            cancellationToken: ct);

    public async Task SetActiveAsync(string id, CancellationToken ct) =>
        await _client.SetActiveProfileAsync(
            new SetActiveProfileRequest { Id = id },
            deadline: DateTime.UtcNow + Deadline,
            cancellationToken: ct);

    /// <summary>
    /// Asks the server how often these rules would have fired.
    /// </summary>
    /// <remarks>
    /// The reason the settings pane is worth building at all: a threshold slider with no
    /// feedback is guesswork, and guesswork is what the pre-rewrite settings dialog asked
    /// of every user.
    /// </remarks>
    public async Task<PreviewProfileResponse> PreviewAsync(
        AlertProfile profile,
        int lookbackDays,
        CancellationToken ct) =>
        await _client.PreviewProfileAsync(
            new PreviewProfileRequest { Profile = profile, LookbackDays = lookbackDays },
            deadline: DateTime.UtcNow + Deadline,
            cancellationToken: ct);
}

/// <summary>Candle history for the inspector chart.</summary>
public sealed class HistoryClient(CallInvoker invoker)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    private readonly MarketDataService.MarketDataServiceClient _client = new(invoker);

    /// <summary>The last <paramref name="minutes"/> of one-minute candles for an instrument.</summary>
    public async Task<IReadOnlyList<Candle>> IntradayAsync(
        string instrumentUid,
        int minutes,
        CancellationToken ct)
    {
        DateTimeOffset to = DateTimeOffset.UtcNow;

        GetHistoryResponse response = await _client.GetHistoryAsync(
            new GetHistoryRequest
            {
                InstrumentUid = instrumentUid,
                Interval = CandleInterval.OneMinute,
                From = Timestamp.FromDateTimeOffset(to - TimeSpan.FromMinutes(minutes)),
                To = Timestamp.FromDateTimeOffset(to),
            },
            deadline: DateTime.UtcNow + Deadline,
            cancellationToken: ct);

        return response.Candles;
    }
}
