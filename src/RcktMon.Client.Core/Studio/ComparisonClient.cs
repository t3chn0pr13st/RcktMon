using System.Net.Http.Json;

namespace RcktMon.Client.Core.Studio;

public sealed record ComparisonRun(string Id, string FromDate, string ThroughDate, string State,
    string Message, int Done, int Total);
public sealed record ComparisonResult(string FromDate, string ThroughDate, ComparisonRow[] Rows, ComparisonTrade[] Trades);
public sealed record ComparisonRow(string Rule, int ExitRed, int CooldownHours, int Signals, int Entered,
    int ProxyPriced, int ProxyWins, decimal? ProxyNet, int BookPriced, int BookWins, decimal? BookNet, int CooldownSkipped = 0);
public sealed record ComparisonTrade(string Rule, int ExitRed, int CooldownHours, string Ticker, long SignalMs,
    long? EntryMs, long? ExitMs, decimal? EntryPrice, decimal? ExitPrice, decimal? ProxyNet, decimal? BookNet,
    decimal? EntryVwap = null, decimal? ExitVwap = null, long? ProxyExitMs = null,
    string? ProxyStatus = null, string? BookStatus = null, decimal? ProxyFees = null, decimal? BookFees = null,
    decimal? ProxyFunding = null, decimal? BookFunding = null);
public sealed record HistoryCandle(long TimeMs, decimal Open, decimal High, decimal Low, decimal Close);

public sealed partial class StudioClient
{
    private static string ComparisonPath(string id) => Guid.TryParseExact(id, "N", out _)
        ? $"api/history/comparisons/{id}" : throw new ArgumentException("Invalid comparison id.", nameof(id));
    public Task<ComparisonRun[]> ComparisonsAsync(CancellationToken ct) => Read<ComparisonRun[]>("api/history/comparisons", ct);
    public Task<ComparisonResult> ComparisonResultAsync(string id, CancellationToken ct) =>
        Read<ComparisonResult>(ComparisonPath(id) + "/result", ct);
    public Task<HistoryCandle[]> ComparisonCandlesAsync(string id, ComparisonTrade trade, CancellationToken ct) =>
        Read<HistoryCandle[]>(FormattableString.Invariant(
            $"{ComparisonPath(id)}/candles?ticker={Uri.EscapeDataString(trade.Ticker)}&signalMs={trade.SignalMs}&exitMs={trade.ExitMs ?? trade.SignalMs}"), ct);

    private sealed record Created(string Id);
    public async Task<string> StartComparisonAsync(DateOnly from, DateOnly through, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/history/comparisons")
        { Content = JsonContent.Create(new { fromDate = from, throughDate = through }) };
        request.Headers.Add("X-RocketMon-History", "1");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Created>(Json, ct).ConfigureAwait(false))?.Id
            ?? throw new System.Text.Json.JsonException("Missing comparison id.");
    }

    public async Task CancelComparisonAsync(string id, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ComparisonPath(id) + "/cancel");
        request.Headers.Add("X-RocketMon-History", "1");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
