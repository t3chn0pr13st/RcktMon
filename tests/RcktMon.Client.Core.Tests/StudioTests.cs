using System.Net;
using System.Text;
using RcktMon.Client.Core.Studio;
using Xunit;

namespace RcktMon.Client.Core.Tests;

public sealed class StudioTests
{
    [Fact]
    public async Task Archive_run_is_a_single_local_research_request()
    {
        using var mock=new ArchiveHandler();using var c=new StudioClient(mock);
        var ct=TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentException>(()=>c.ArchiveResultAsync("../wrong",ct));
        await c.StartArchiveAsync(new("2026-02-25","2026-08-24","*",1,1000,10000,50000),ct);
        Assert.Equal(1,mock.Count);
    }
    private sealed class ArchiveHandler:HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            Assert.Equal("http://127.0.0.1:47831/api/history/runs",r.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post,r.Method);Assert.Equal("1",Assert.Single(r.Headers.GetValues("X-RocketMon-History")));
            ++Count;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public void Rebasing_uses_each_series_first_valid_trade_and_preserves_gaps()
    {
        var points = StudioPlot.Normalize([
            new(0, 988, 200, null), new(1000, 1540, 210, 100),
            new(2000, 2850, 212, null), new(4000, 4001, 220, 102)], 1000);
        Assert.Equal(988, points[0].Time);
        Assert.Null(points[0].Stock);
        Assert.Equal(5d, points[1].Perp);
        Assert.Equal(0d, points[1].Stock);
        Assert.Null(points[2].Stock);
        Assert.False(points[2].BreakBefore);
        Assert.True(points[3].BreakBefore);
        Assert.Equal(10d, points[3].Perp);
        Assert.Equal(2d, points[3].Stock);
    }

    [Theory]
    [InlineData(1100, 1000, 61000)]
    [InlineData(55000, 25000, 85000)]
    [InlineData(99999, 41000, 101000)]
    public void Signal_zoom_keeps_full_window_inside_capture(long signal, long start, long end) =>
        Assert.Equal((start, end), StudioPlot.Range(1000, 101000, signal, 60));

    [Fact]
    public async Task Replay_uses_only_loopback_get_validated_id_and_invariant_decimal_query()
    {
        var handler = new Fake();
        using var client = new StudioClient(handler);
        var id = new string('b', 64);
        var p = new StudioParameters(true, 100000, .25m, .5m);
        var response = await client.ReplayAsync(id, p, TestContext.Current.CancellationToken);
        Assert.Equal(0, response.Summary.Closed);
        Assert.Null(response.Summary.TotalNetUsdt);
        Assert.Equal("GET", handler.Method);
        Assert.Equal($"http://127.0.0.1:47831/api/replay/{id}?flowOnly=true&minBuyUsdt=100000&minMovePct=0.25&slipBps=0.5", handler.Url);
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReplayAsync("../trading", p, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ReplayAsync(id, p with { Slip = -1 }, CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    private sealed class Fake : HttpMessageHandler
    {
        public string? Method { get; private set; }
        public string? Url { get; private set; }
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++; Method = request.Method.Method; Url = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"capture":{"id":"recording","ticker":"NVDA","instrument":"NVDA-USDT-SWAP","source":"test","from":"2026-11-23T00:00:00Z","until":"2026-11-23T00:01:00Z","hasSip":false,"cryptoPrints":0,"stockQuotes":0},"bucketMs":1000,"bars":[],"events":[],"summary":{"signals":0,"closed":0,"wins":0,"positiveFraction":null,"totalNetUsdt":null,"meanNetUsdt":null,"medianNetUsdt":null}}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
