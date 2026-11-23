using System.Net.Http.Json;
namespace RcktMon.Client.Core.Studio;
public sealed record ArchiveTicker(string Ticker,DateTimeOffset ListedAt);
public sealed record ArchiveCatalog(string First,string Last,ArchiveTicker[] Tickers);
public sealed record ArchiveQuery(string From,string Through,string Ticker,decimal Rise,decimal Nominal,decimal Low,decimal High);
public sealed record ArchiveRun(string Id,ArchiveQuery Query,string State,string Message,int Done,int Total);
public sealed record ArchiveGroup(decimal Floor,string Exit,int Signals,int Entered,int Closed,int Wins,int Unknown,string? Net);
public sealed record ArchiveResult(ArchiveGroup[] Groups,int CacheHits,int DownloadedDays);
public sealed partial class StudioClient
{
    public Task<ArchiveCatalog> ArchiveCatalogAsync(CancellationToken ct)=>Read<ArchiveCatalog>("api/history/catalog",ct);
    public Task<ArchiveRun[]> ArchiveRunsAsync(CancellationToken ct)=>Read<ArchiveRun[]>("api/history/runs",ct);
    public Task<ArchiveResult> ArchiveResultAsync(string id,CancellationToken ct)=>
        Guid.TryParseExact(id,"N",out _)?Read<ArchiveResult>($"api/history/runs/{id}/result",ct):throw new ArgumentException("Invalid run");
    public async Task StartArchiveAsync(ArchiveQuery query,CancellationToken ct)
    {
        using var req=new HttpRequestMessage(HttpMethod.Post,"api/history/runs"){Content=JsonContent.Create(query)};
        req.Headers.Add("X-RocketMon-History","1");
        using var resp=await _http.SendAsync(req,ct).ConfigureAwait(false);resp.EnsureSuccessStatusCode();
    }
}
