using System.Net.Http.Json;
using System.Text.Json;

namespace RcktMon.Client.Core.Studio;

/// <summary>Public observations only. No configurable origin, cookies, proxies, redirects, trading or account APIs.</summary>
public sealed partial class StudioClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };
    private readonly HttpClient _http;
    public StudioClient() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { }

    public StudioClient(HttpMessageHandler handler) => _http = new(handler)
    {
        BaseAddress = new("http://127.0.0.1:47831/"), Timeout = TimeSpan.FromSeconds(40)
    };

    private async Task<T> Read<T>(string path, CancellationToken token) =>
        await _http.GetFromJsonAsync<T>(path, Json, token).ConfigureAwait(false) ?? throw new JsonException("Empty Studio response.");

    public Task<StudioLive> LiveAsync(CancellationToken token) => Read<StudioLive>("api/live", token);

    public Task<StudioCapture[]> RecordingsAsync(CancellationToken token) => Read<StudioCapture[]>("api/recordings", token);

    public Task<StudioReplay> ReplayAsync(string id, StudioParameters p, CancellationToken token)
    {
        if (id.Length != 64 || !id.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Invalid recording id.", nameof(id));
        if (p.MinBuy is < 1 or > 10_000_000_000 || p.Slip is < 0 or > 1000 || p.Move is < 0 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(p));
        return Read<StudioReplay>(FormattableString.Invariant(
            $"api/replay/{id}?flowOnly={(p.FlowOnly ? "true" : "false")}&minBuyUsdt={p.MinBuy}&minMovePct={p.Move}&slipBps={p.Slip}"), token);
    }

    public void Dispose() => _http.Dispose();
}

public sealed record StudioParameters(bool FlowOnly, decimal MinBuy, decimal Move, decimal Slip);

public sealed record StudioLive(string Status, DateTimeOffset AsOf, DateTimeOffset? LastMessageAt,
    int SubscribedCount, int FreshCount, long TotalPrints, StudioInstrument[] Instruments, StudioSignal[] Signals);

public sealed record StudioInstrument(string Ticker, string Instrument, string State, decimal? LastPrice,
    DateTimeOffset? LastTradeAt, decimal? BuyUsdt5s, decimal? SellUsdt5s, decimal? BuyShare5s,
    int? BuyPrints5s, decimal? Move5sPct);

public sealed record StudioSignal(string Ticker, DateTimeOffset At, decimal Price, decimal BuyUsdt5s, decimal Move5sPct, long LagMs);

public sealed record StudioCapture(string Id, string Ticker, string Instrument, string Source,
    DateTimeOffset From, DateTimeOffset Until, bool HasSip, int CryptoPrints, int StockQuotes)
{
    public string Label => $"{Ticker}  ·  {From.UtcDateTime:yyyy-MM-dd  HH:mm}–{Until.UtcDateTime:HH:mm:ss} UTC";
    public override string ToString() => Label;
}

public sealed record StudioReplay(StudioCapture Capture, long BucketMs, StudioBar[] Bars, StudioEvent[] Events, StudioSummary Summary,
    StudioExecution? Options = null);
public sealed record StudioExecution(decimal NominalUsdt, int LatencyMs, decimal FeeBpsPerSide, decimal SlippageBpsPerSide);
public sealed record StudioBar(long TimeMs, long CloseMs, decimal Close, decimal? StockMidUsd);
public sealed record StudioEvent(long SignalMs, long? EntryMs, long? ExitMs, decimal SignalPrice, string Status,
    decimal? ModeledEntryPrice, decimal? ModeledExitPrice, decimal? ModeledFeesUsdt, decimal? AfterFeesAndModeledSlippageUsdt);
public sealed record StudioSummary(int Signals, int Closed, int Wins, decimal? PositiveFraction,
    decimal? TotalNetUsdt, decimal? MeanNetUsdt, decimal? MedianNetUsdt);
