using System.Net;
using System.Text;
using System.Text.Json;
using RcktMon.Client.Core.Trading;
using Xunit;

namespace RcktMon.Client.Core.Tests;

public sealed class FinamTradingClientTests
{
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T02:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // The handler handles every URL locally. It has no inner handler, socket or network fallback.
    private sealed class Broker : HttpMessageHandler
    {
        // Null models a gateway omitting a default-valued proto3 bool.
        internal bool? ReadOnly = false;
        // Synthetic lot size deliberately > 1 to catch an accidental lots-as-shares order.
        internal string Asset = """{"id":"asset-1","ticker":"SBER","isin":"RU0009029540","board":"TQBR","mic":"MISX","type":"EQUITIES","quote_currency":"RUB","lot_size":{"value":"10"},"min_step":"1","decimals":2}""";
        internal string Params = """{"symbol":"SBER@MISX","account_id":"A123","is_tradable":true,"trade_lot_size":"10"}""";
        internal string Account = """{"account_id":"A123","cash":[{"currency_code":"RUB","units":"10000","nanos":0}],"positions":[{"symbol":"SBER@MISX","quantity":{"value":"35"}}]}""";
        internal string Orders = """{"orders":[]}""";
        internal string WritePath = "";
        internal string? OrderBody;
        internal int Writes;
        internal int Gets;
        internal Func<JsonElement, HttpResponseMessage>? OnOrder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Assert.Equal("https://api.finam.ru", req.RequestUri!.GetLeftPart(UriPartial.Authority));
            string path = Uri.UnescapeDataString(req.RequestUri.PathAndQuery);
            var body = req.Content == null ? "" : await req.Content.ReadAsStringAsync(ct);
            if (path.StartsWith("/v1/sessions", StringComparison.Ordinal))
            {
                Assert.False(req.Headers.Contains("Authorization"));
                Assert.Equal(HttpMethod.Post, req.Method);
                if (path == "/v1/sessions")
                {
                    Assert.Equal("demo-secret", JsonDocument.Parse(body).RootElement.GetProperty("secret").GetString());
                    return Json("""{"token":"short-lived-jwt"}""");
                }
                Assert.Equal("/v1/sessions/details", path);
                Assert.Equal("short-lived-jwt", JsonDocument.Parse(body).RootElement.GetProperty("token").GetString());
                var details = new Dictionary<string, object>
                {
                    ["account_ids"] = new[] { "A123", "SECOND" }, ["expires_at"] = "2026-11-24T02:15:00Z"
                };
                if (ReadOnly.HasValue) details["readonly"] = ReadOnly.Value;
                return Json(JsonSerializer.Serialize(details));
            }

            Assert.Equal("short-lived-jwt", Assert.Single(req.Headers.GetValues("Authorization")));
            if (req.Method == HttpMethod.Get)
            {
                Gets++;
                return Json(path switch
                {
                    "/v1/assets/SBER@MISX?account_id=A123" => Asset,
                    "/v1/assets/SBER@MISX/params?account_id=A123" => Params,
                    "/v1/accounts/A123/orders" => Orders,
                    "/v1/accounts/A123" => Account,
                    _ => throw new InvalidDataException("Unexpected read")
                });
            }
            Assert.Equal("/v1/accounts/A123/orders", path);
            Assert.Equal(HttpMethod.Post, req.Method);
            Writes++;
            WritePath = path;
            OrderBody = body;
            var order = JsonDocument.Parse(body).RootElement;
            if (OnOrder != null) return OnOrder(order);
            var echoed = JsonSerializer.Deserialize<Dictionary<string, object>>(body)!;
            echoed["account_id"] = "A123";
            return Json(JsonSerializer.Serialize(new { order_id = "ORDER-1", status = "ORDER_STATUS_NEW", order = echoed }));
        }

        internal static string Echo(string id, string account = "A123", string status = "ORDER_STATUS_NEW") =>
            JsonSerializer.Serialize(new { order_id = "ORDER-1", status, order = new { account_id = account, client_order_id = id } });

        internal static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
            new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static Task<FinamOrderReview> Review(FinamTradingClient client, FinamSide side = FinamSide.Buy) =>
        client.PrepareAsync("A123", "sber", side, 2, 299.51m, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(FinamSide.Buy, "SIDE_BUY")]
    [InlineData(FinamSide.Sell, "SIDE_SELL")]
    public async Task Lots_are_reviewed_in_shares_then_explicit_limit_day_is_submitted_exactly_once(FinamSide side, string expected)
    {
        var broker = new Broker();
        using var client = new FinamTradingClient(broker, new Clock());
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(new[] { "A123", "SECOND" }, await client.ConnectAsync("demo-secret", ct));
        var review = await Review(client, side);
        Assert.Equal("SBER@MISX", review.Symbol);
        Assert.Equal("TQBR", review.Board);
        Assert.Equal(2, review.Lots);
        Assert.Equal(10L, review.LotSize);
        Assert.Equal(20L, review.Shares);
        Assert.Equal(5990.20m, review.Notional);
        Assert.Equal("RUB", review.Currency);
        Assert.Equal(20, review.ClientOrderId.Length);
        Assert.Equal(0, broker.Writes);
        Assert.Equal(FinamOutcome.Rejected, (await client.SubmitAsync(review with { Shares = 30 }, ct)).Outcome);
        var results = await Task.WhenAll(client.SubmitAsync(review, ct), client.SubmitAsync(review, ct));
        Assert.Single(results, x => x.Outcome == FinamOutcome.Recorded);
        Assert.Single(results, x => x.Outcome == FinamOutcome.Rejected);
        Assert.Equal(1, broker.Writes);
        Assert.Equal(8, broker.Gets); // selected metadata, parameters, orders, portfolio; twice
        var body = JsonDocument.Parse(broker.OrderBody!).RootElement;
        Assert.Equal("ORDER_TYPE_LIMIT", body.GetProperty("type").GetString());
        Assert.Equal("TIME_IN_FORCE_DAY", body.GetProperty("time_in_force").GetString());
        Assert.Equal(expected, body.GetProperty("side").GetString());
        Assert.Equal("20", body.GetProperty("quantity").GetProperty("value").GetString());
        Assert.Equal("299.51", body.GetProperty("limit_price").GetProperty("value").GetString());
        Assert.Equal(review.ClientOrderId, body.GetProperty("client_order_id").GetString());
    }

    [Theory]
    [InlineData("foreign-venue")]
    [InlineData("wrong-board")]
    [InlineData("futures")]
    [InlineData("not-rub")]
    [InlineData("lot-mismatch")]
    [InlineData("not-tradable")]
    [InlineData("other-account-params")]
    [InlineData("no-tick")]
    [InlineData("off-tick")]
    [InlineData("no-cash")]
    [InlineData("wrong-portfolio")]
    [InlineData("open-order")]
    [InlineData("quantity-exceeds-position")]
    public async Task Missing_or_ineligible_evidence_cannot_prepare_an_order(string fault)
    {
        var broker = new Broker();
        using var client = new FinamTradingClient(broker, new Clock());
        var ct = TestContext.Current.CancellationToken;
        await client.ConnectAsync("demo-secret", ct);
        switch (fault)
        {
            case "foreign-venue": broker.Asset = broker.Asset.Replace("MISX", "XNAS"); break;
            case "wrong-board": broker.Asset = broker.Asset.Replace("TQBR", "OTHER"); break;
            case "futures": broker.Asset = broker.Asset.Replace("EQUITIES", "FUTURES"); break;
            case "not-rub": broker.Asset = broker.Asset.Replace("RUB", "USD"); break;
            case "lot-mismatch": broker.Params = broker.Params.Replace("\"10\"", "\"1\""); break;
            case "not-tradable": broker.Params = broker.Params.Replace("true", "false"); break;
            case "other-account-params": broker.Params = broker.Params.Replace("A123", "SECOND"); break;
            case "no-tick": broker.Asset = broker.Asset.Replace("\"min_step\":\"1\"", "\"min_step\":\"0\""); break;
            case "off-tick": broker.Asset = broker.Asset.Replace("\"min_step\":\"1\"", "\"min_step\":\"5\""); break;
            case "no-cash": broker.Account = broker.Account.Replace("10000", "4000"); break;
            case "wrong-portfolio": broker.Account = broker.Account.Replace("A123", "SECOND"); break;
            case "open-order": broker.Orders = """{"orders":[{"status":"ORDER_STATUS_NEW"}]}"""; break;
            case "quantity-exceeds-position": broker.Account = broker.Account.Replace("\"35\"", "\"19\""); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Review(client,
            fault == "quantity-exceeds-position" ? FinamSide.Sell : FinamSide.Buy));
        Assert.Equal(0, broker.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task Read_only_or_unknown_privilege_never_sends_and_unknown_account_never_hits_a_provider_endpoint(bool? readOnly)
    {
        var broker = new Broker { ReadOnly = readOnly };
        using var client = new FinamTradingClient(broker, new Clock());
        var ct = TestContext.Current.CancellationToken;
        await client.ConnectAsync("demo-secret", ct);
        Assert.True(client.IsReadOnly);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PrepareAsync("not-in-token", "SBER", FinamSide.Buy, 1, 1, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PrepareAsync("A123", "x@y/../../", FinamSide.Buy, 1, 1, ct));
        Assert.Equal(0, broker.Gets);
        var preview = await Review(client);
        Assert.Equal(FinamOutcome.Rejected, (await client.SubmitAsync(preview, ct)).Outcome);
        Assert.Equal(0, broker.Writes);
    }

    [Theory]
    [InlineData("expires")]
    [InlineData("rebinding")]
    [InlineData("metadata")]
    [InlineData("coverage")]
    public async Task Terms_and_coverage_are_bound_to_this_session_and_rechecked_at_confirmation(string change)
    {
        var broker = new Broker();
        var clock = new Clock();
        using var client = new FinamTradingClient(broker, clock);
        var ct = TestContext.Current.CancellationToken;
        await client.ConnectAsync("demo-secret", ct);
        var review = await Review(client, FinamSide.Sell);
        switch (change)
        {
            case "expires": clock.Now = review.ExpiresAt; break;
            case "rebinding": await client.ConnectAsync("demo-secret", ct); break;
            case "metadata": broker.Asset = broker.Asset.Replace("asset-1", "asset-2"); break;
            case "coverage": broker.Account = broker.Account.Replace("\"35\"", "\"10\""); break;
        }
        Assert.Equal(FinamOutcome.Rejected, (await client.SubmitAsync(review, ct)).Outcome);
        Assert.Equal(0, broker.Writes);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("bad-json")]
    [InlineData("different-id")]
    [InlineData("redirect")]
    [InlineData("server-error")]
    public async Task Uncertain_ack_is_not_replayed_and_only_positive_readback_resolves_it(string failure)
    {
        var broker = new Broker { OnOrder = _ => failure switch
        {
            "timeout" => throw new TaskCanceledException(),
            "bad-json" => Broker.Json("{"),
            "different-id" => Broker.Json(Broker.Echo("other")),
            "redirect" => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
                { Headers = { Location = new Uri("https://example.invalid/forbidden") } },
            _ => Broker.Json("oops", HttpStatusCode.ServiceUnavailable)
        } };
        using var client = new FinamTradingClient(broker, new Clock());
        var ct = TestContext.Current.CancellationToken;
        await client.ConnectAsync("demo-secret", ct);
        var review = await Review(client);
        Assert.Equal(FinamOutcome.Unknown, (await client.SubmitAsync(review, ct)).Outcome);
        Assert.Equal(FinamOutcome.Rejected, (await client.SubmitAsync(review, ct)).Outcome);
        Assert.Equal(FinamOutcome.Unknown, (await client.LookupAsync("A123", review.ClientOrderId, ct)).Outcome);
        broker.Orders = "{\"orders\":[" + Broker.Echo(review.ClientOrderId, "SECOND") + "]}";
        Assert.Equal(FinamOutcome.Unknown, (await client.LookupAsync("A123", review.ClientOrderId, ct)).Outcome);
        broker.Orders = "{\"orders\":[" + Broker.Echo(review.ClientOrderId) + "]}";
        Assert.Equal(FinamOutcome.Recorded, (await client.LookupAsync("A123", review.ClientOrderId, ct)).Outcome);
        Assert.Equal(1, broker.Writes);
    }
}
