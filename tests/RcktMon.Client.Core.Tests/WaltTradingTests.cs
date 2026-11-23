using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using RcktMon.Client.Core.Trading;
using Xunit;

namespace RcktMon.Client.Core.Tests;

/// <summary>No traffic to Wallet: the entire host, including OAuth metadata and token endpoint, is intercepted.</summary>
public sealed class WaltTradingTests
{
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Provider(Clock clock) : HttpMessageHandler
    {
        private const string Root = "https://walletbot.me/bot-relay/public-api";
        public int Executes, Prepares, Registrations, Exchanges;
        public bool DenyExecute, WrongState, DenyPreview, WrongEcho, BlockRisk;
        public int OpenPositions = 1;
        public string? Challenge, GrantScope, Mutate;
        public string PositionId = "position-A";
        public string PositionSize = "0.012";
        public string RateMarket = "BTC-USD-PERP";
        public List<string> Calls { get; } = [];
        public JsonElement Prepared { get; private set; }
        public const string Token = "only-approved-token-for-provider";

        public void OpenBrowser(Uri uri)
        {
            Assert.Equal(Root + "/oauth/v1/authorize", uri.GetLeftPart(UriPartial.Path));
            var q = HttpUtility.ParseQueryString(uri.Query);
            Assert.Equal("perps.trade", q["scope"]);
            Assert.Equal("S256", q["code_challenge_method"]);
            Assert.Equal(Root + "/mcp", q["resource"]);
            Challenge = q["code_challenge"];
            // Only the local callback is real; the mock never invokes a browser or external site.
            var dest = new Uri(q["redirect_uri"]!);
            Assert.True(dest.IsLoopback);
            _ = Task.Run(async () =>
            {
                using var local = new HttpClient();
                await local.GetStringAsync(new Uri(dest + "?code=test-code&state=" + (WrongState ? "invalid" : Uri.EscapeDataString(q["state"]!))));
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            string u = req.RequestUri!.AbsoluteUri;
            Assert.Equal("walletbot.me", req.RequestUri.Host);
            if (u.EndsWith("/protected-resource", StringComparison.Ordinal) || u.Contains("/.well-known/oauth-protected-resource", StringComparison.Ordinal))
            {
                return Reply(new
                {
                    resource = Root + "/mcp",
                    authorization_servers = new[] { Root },
                    scopes_supported = new[] { "perps.readonly", "perps.trade" },
                });
            }
            if (u.Contains("/.well-known/oauth-authorization-server", StringComparison.Ordinal))
            {
                return Reply(new
                {
                    issuer = Root,
                    authorization_endpoint = Root + "/oauth/v1/authorize",
                    token_endpoint = Root + "/oauth/v1/token",
                    registration_endpoint = Root + "/oauth/v1/register",
                    response_types_supported = new[] { "code" },
                    grant_types_supported = new[] { "authorization_code", "refresh_token" },
                    code_challenge_methods_supported = new[] { "S256" },
                    token_endpoint_auth_methods_supported = new[] { "none" },
                });
            }
            if (u == Root + "/oauth/v1/register")
            {
                Registrations++;
                return Reply(new { client_id = "public-rocketmon", token_endpoint_auth_method = "none" });
            }
            if (u == Root + "/oauth/v1/token")
            {
                Exchanges++;
                var form = HttpUtility.ParseQueryString(await req.Content!.ReadAsStringAsync(ct));
                Assert.Equal("test-code", form["code"]);
                Assert.Equal("public-rocketmon", form["client_id"]);
                Assert.Equal(Root + "/mcp", form["resource"]);
                string proof = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!)))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                Assert.Equal(Challenge, proof);
                var grant = new Dictionary<string, object> { ["access_token"] = "walt-only-test", ["refresh_token"] = "walt-refresh-test", ["token_type"] = "Bearer", ["expires_in"] = 600 };
                if (GrantScope is not null) { grant["scope"] = GrantScope; }
                return Reply(grant);
            }

            Assert.Equal(Root + "/mcp", u);
            if (req.Headers.Authorization?.Parameter != "walt-only-test")
            {
                var res = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                res.Headers.TryAddWithoutValidation("WWW-Authenticate", $"Bearer resource_metadata=\"{Root}/oauth/v1/protected-resource\"");
                return res;
            }
            if (req.Method != HttpMethod.Post) { return new(HttpStatusCode.MethodNotAllowed); }
            using var doc = await JsonDocument.ParseAsync(await req.Content!.ReadAsStreamAsync(ct), cancellationToken: ct);
            var rpc = doc.RootElement;
            if (!rpc.TryGetProperty("id", out JsonElement id)) { return new(HttpStatusCode.Accepted); }
            string? method = rpc.GetProperty("method").GetString();
            string[] tools = ["perps_guide", "perps_list_contracts", "perps_latest_rate", "perps_prepare_order_place", "perps_list_positions", "perps_prepare_position_close", "perps_execute_operation"];
            object result;
            switch (method)
            {
                case "server/discover":
                    return Reply(new { jsonrpc = "2.0", id, error = new { code = -32601, message = "Method not found" } });
                case "initialize":
                    result = new { protocolVersion = "2025-11-25", capabilities = new { tools = new { } }, serverInfo = new { name = "mock", version = "1" } };
                    break;
                case "tools/list":
                    result = new { tools = tools.Select(name => new { name, inputSchema = new { type = "object" } }) };
                    break;
                case "tools/call":
                    string name = rpc.GetProperty("params").GetProperty("name").GetString()!;
                    Calls.Add(name);
                    object data;
                    switch (name)
                    {
                        case "perps_guide": data = "simulated guide"; break;
                        case "perps_list_contracts":
                            data = new { contracts = new[] { new { contract_id = 0, contract_name = "BTC-USDC", base_asset = "BTC", quote_asset = "USDC", is_active = true, venue_status = "active", min_size_quote = "1", max_size_quote = "100", max_leverage = 5, taker_fee_pct = "0.001", price_slippage = "0.01", funding_rate_hourly = "-0.0001" } } };
                            break;
                        case "perps_latest_rate":
                            data = new { market_id = RateMarket, rate = "12345.6" };
                            break;
                        case "perps_list_positions":
                            var filter = rpc.GetProperty("params").GetProperty("arguments");
                            Assert.Equal(0, filter.GetProperty("contractId").GetInt32());
                            Assert.Equal("open", Assert.Single(filter.GetProperty("statuses").EnumerateArray()).GetString());
                            data = new { positions = Enumerable.Repeat(new { contract_id = 0, position_id = PositionId, side = "short", status = "open", value_base = PositionSize, liquidation_price = "15000" }, OpenPositions) };
                            break;
                        case "perps_prepare_position_close":
                        case "perps_prepare_order_place":
                            Prepares++;
                            Prepared = rpc.GetProperty("params").GetProperty("arguments").Clone();
                            data = new
                            {
                                ok = !DenyPreview,
                                confirmationToken = Token,
                                expiresAt = clock.Now.AddMinutes(3),
                                summary = "provider summary " + Token + " walt-only-test walt-refresh-test",
                                action = new { @params = Prepared, kind = WrongEcho || name == "perps_prepare_position_close" ? "position.close" : "order.place", secret = "not-for-display" },
                                risk = new { details = "risk summary", checks = new[] { new { passed = !BlockRisk } } },
                                warnings = new[] { "risk " + Token },
                            };
                            if (Mutate is not null)
                            {
                                var fields = JsonSerializer.SerializeToNode(data)!.AsObject();
                                switch (Mutate)
                                {
                                    case "token-only": fields.Remove("action"); fields.Remove("risk"); break;
                                    case "missing-risk": fields.Remove("risk"); break;
                                    case "null-risk": fields["risk"] = null; break;
                                    case "empty-risk": fields["risk"] = new JsonObject(); break;
                                    case "opaque-checks": fields["risk"] = JsonNode.Parse("""{"checks":[{"xyz":true}]}"""); break;
                                    case "missing-action": fields.Remove("action"); break;
                                    case "empty-action": fields["action"] = new JsonObject(); break;
                                    case "missing-price": fields["action"]!["params"]!.AsObject().Remove("price"); break;
                                    case "wrong-price": fields["action"]!["params"]!["price"] = "999"; break;
                                    case "wrong-contract": fields["action"]!["params"]!["contractId"] = 42; break;
                                    case "wrong-position": fields["action"]!["params"]!["position_id"] = "wrong"; break;
                                    default: throw new ArgumentOutOfRangeException();
                                }
                                data = fields;
                            }
                            break;
                        case "perps_execute_operation":
                            Executes++;
                            Assert.Equal(Token, rpc.GetProperty("params").GetProperty("arguments").GetProperty("confirmationToken").GetString());
                            if (DenyExecute) { return new HttpResponseMessage(HttpStatusCode.Unauthorized); }
                            data = new { order_id = "ord-42" }; break;
                        default: throw new NotSupportedException(name);
                    }
                    result = new { content = new[] { new { type = "text", text = data is string s ? s : JsonSerializer.Serialize(data) } } };
                    break;
                default: throw new NotSupportedException(method);
            }
            return Reply(new { jsonrpc = "2.0", id, result });
        }

        private static HttpResponseMessage Reply(object body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task SeparateGrant_ReviewsExactMarketOrder_OneSubmitAndNoOnboarding()
    {
        var clock = new Clock();
        var provider = new Provider(clock);
        using var app = new WaltTradingClient(() => provider, clock);
        await app.ConnectAsync(provider.OpenBrowser, TestContext.Current.CancellationToken);
        Assert.Equal(1, provider.Registrations);
        Assert.Equal(1, provider.Exchanges);
        Assert.Equal(0, Assert.Single(await app.ListContractsAsync(TestContext.Current.CancellationToken)).Id);
        var view = await app.PrepareAsync(0, "short", 2, 12.5m, TestContext.Current.CancellationToken);
        Assert.Equal(clock.Now.AddSeconds(90), view.ExpiresAt);
        Assert.Contains("12.5 USDC", view.Summary);
        Assert.Contains("12345.6", view.Summary);
        Assert.Contains("risk summary", view.Summary);
        Assert.Contains("provider summary", view.Summary);
        Assert.DoesNotContain("walt-only-test", view.Summary);
        Assert.DoesNotContain("walt-refresh-test", view.Summary);
        Assert.DoesNotContain("not-for-display", view.Summary);
        Assert.DoesNotContain(Provider.Token, view.Summary);
        Assert.Equal("12.5", provider.Prepared.GetProperty("amount").GetString());
        Assert.Equal("12345.6", provider.Prepared.GetProperty("price").GetString());
        Assert.Equal("short", provider.Prepared.GetProperty("side").GetString());
        Assert.Equal("market", provider.Prepared.GetProperty("type").GetString());
        Assert.Equal(0, provider.Executes);
        Assert.Contains("ord-42", await app.ExecuteAsync(view.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.ExecuteAsync(view.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1, provider.Executes);
        Assert.Equal(new[] { "perps_guide", "perps_list_contracts", "perps_latest_rate", "perps_prepare_order_place", "perps_execute_operation" }, provider.Calls);
    }

    [Fact]
    public async Task Expired_Or_Replaced_Or_Refused_Prepare_CannotExecute()
    {
        var clock = new Clock();
        var provider = new Provider(clock);
        using var app = new WaltTradingClient(() => provider, clock);
        await app.ConnectAsync(provider.OpenBrowser, TestContext.Current.CancellationToken);
        await app.ListContractsAsync(TestContext.Current.CancellationToken);
        var p = await app.PrepareAsync(0, "long", 1, 2m, TestContext.Current.CancellationToken);
        clock.Now += TimeSpan.FromSeconds(91);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.ExecuteAsync(p.Id, TestContext.Current.CancellationToken));
        var p2 = await app.PrepareAsync(0, "short", 2, 3m, TestContext.Current.CancellationToken);
        provider.DenyPreview = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.PrepareAsync(0, "long", 1, 2m, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.ExecuteAsync(p2.Id, TestContext.Current.CancellationToken));
        provider.DenyPreview = false;
        provider.WrongEcho = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.PrepareAsync(0, "short", 2, 3m, TestContext.Current.CancellationToken));
        Assert.Equal(0, provider.Executes);
    }

    [Fact]
    public async Task UncertainSubmit_DoesNotReauthorizeOrDispatchAgain()
    {
        var clock = new Clock();
        var provider = new Provider(clock) { DenyExecute = true };
        using var app = new WaltTradingClient(() => provider, clock);
        await app.ConnectAsync(provider.OpenBrowser, TestContext.Current.CancellationToken);
        await app.ListContractsAsync(TestContext.Current.CancellationToken);
        var p = await app.PrepareAsync(0, "long", 1, 1m, TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.ExecuteAsync(p.Id, TestContext.Current.CancellationToken));
        Assert.Contains("не подтверждён", ex.Message);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.ExecuteAsync(p.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1, provider.Executes);
        Assert.Equal(1, provider.Exchanges);
        Assert.Equal(1, provider.Prepares);
    }

    [Fact]
    public async Task Close_ReadsOnlyRequestedContract_NoAmbiguousPosition_NoBlockedRisk()
    {
        var clock = new Clock();
        var provider = new Provider(clock);
        using var app = new WaltTradingClient(() => provider, clock);
        await app.ConnectAsync(provider.OpenBrowser, TestContext.Current.CancellationToken);
        await app.ListContractsAsync(TestContext.Current.CancellationToken);
        foreach (var n in new[] { 0, 2 })
        {
            provider.OpenPositions = n;
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.PrepareCloseAsync(0, TestContext.Current.CancellationToken));
        }
        Assert.Equal(0, provider.Prepares);
        provider.OpenPositions = 1;
        provider.BlockRisk = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.PrepareCloseAsync(0, TestContext.Current.CancellationToken));
        provider.BlockRisk = false;
        var p = await app.PrepareCloseAsync(0, TestContext.Current.CancellationToken);
        Assert.Contains("ЗАКРЫТЬ ВСЮ", p.Summary);
        Assert.Contains("SHORT: 0.012 BTC", p.Summary);
        Assert.Contains("15000 USDC", p.Summary);
        Assert.Equal(0, provider.Executes);
        Assert.Single(provider.Prepared.EnumerateObject());
        Assert.Equal(0, provider.Prepared.GetProperty("contractId").GetInt32());
        Assert.Contains("ord-42", await app.ExecuteAsync(p.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1, provider.Executes);
    }

    [Theory]
    [InlineData("perps.readonly")]
    [InlineData("perps.trade.not")]
    [InlineData("")]
    public async Task ReducedGrant_DoesNotAuthorizeAnyTools(string scope)
    {
        var clock = new Clock();
        var provider = new Provider(clock) { GrantScope = scope };
        using var app = new WaltTradingClient(() => provider, clock);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.ConnectAsync(provider.OpenBrowser, TestContext.Current.CancellationToken));
        Assert.Empty(provider.Calls);
    }

    [Theory]
    [InlineData("token-only")]
    [InlineData("missing-risk")]
    [InlineData("null-risk")]
    [InlineData("empty-risk")]
    [InlineData("opaque-checks")]
    [InlineData("missing-action")]
    [InlineData("empty-action")]
    [InlineData("missing-price")]
    [InlineData("wrong-price")]
    [InlineData("wrong-contract")]
    public async Task UnknownOrDifferentPreviewCannotBeSubmitted(string mutation)
    {
        var clock = new Clock();
        var provider = new Provider(clock) { Mutate = mutation, GrantScope = "perps.trade" };
        using var app = new WaltTradingClient(() => provider, clock);
        await app.ConnectAsync(provider.OpenBrowser, TestContext.Current.CancellationToken);
        await app.ListContractsAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.PrepareAsync(0, "long", 1, 1m, TestContext.Current.CancellationToken));
        Assert.Equal(0, provider.Executes);
    }

    [Fact]
    public async Task WrongRateRejectedBeforePrepareAndFreshPositionIdentityCheckedBeforeClose()
    {
        var clock = new Clock();
        var provider = new Provider(clock) { RateMarket = "ETH-USD-PERP" };
        using var app = new WaltTradingClient(() => provider, clock);
        await app.ConnectAsync(provider.OpenBrowser, TestContext.Current.CancellationToken);
        await app.ListContractsAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.PrepareAsync(0, "long", 1, 1m, TestContext.Current.CancellationToken));
        Assert.Equal(0, provider.Prepares);
        provider.RateMarket = "BTC-USD-PERP";
        provider.Mutate = "wrong-position";
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.PrepareCloseAsync(0, TestContext.Current.CancellationToken));
        provider.Mutate = null;
        var p = await app.PrepareCloseAsync(0, TestContext.Current.CancellationToken);
        provider.PositionId = "replaced-on-wallet";
        await Assert.ThrowsAsync<WaltNotDispatchedException>(() => app.ExecuteAsync(p.Id, TestContext.Current.CancellationToken));
        p = await app.PrepareCloseAsync(0, TestContext.Current.CancellationToken);
        provider.PositionSize = "0.005";
        await Assert.ThrowsAsync<WaltNotDispatchedException>(() => app.ExecuteAsync(p.Id, TestContext.Current.CancellationToken));
        Assert.Equal(0, provider.Executes);
    }

    [Fact]
    public async Task Reconnect_UsesSamePublicClientAndPort_ButRequiresAFreshGrant()
    {
        string dir = Directory.CreateTempSubdirectory("walt-offline-").FullName;
        string path = Path.Combine(dir, "public-client.json");
        try
        {
            var clock = new Clock();
            var first = new Provider(clock);
            using (var app = new WaltTradingClient(() => first, clock, path))
            {
                await app.ConnectAsync(first.OpenBrowser, TestContext.Current.CancellationToken);
            }
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal(2, saved.RootElement.EnumerateObject().Count());
            Assert.Equal("public-rocketmon", saved.RootElement.GetProperty("ClientId").GetString());
            Assert.InRange(saved.RootElement.GetProperty("Port").GetInt32(), 1024, 65535);
            var next = new Provider(clock);
            using var again = new WaltTradingClient(() => next, clock, path);
            await again.ConnectAsync(next.OpenBrowser, TestContext.Current.CancellationToken);
            Assert.Equal(1, first.Registrations);
            Assert.Equal(0, next.Registrations);
            Assert.Equal(1, next.Exchanges);
            Assert.Equal(new[] { "perps_guide" }, next.Calls);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task WrongOAuthState_NeverExchangesTokenOrReachesTools()
    {
        var clock = new Clock();
        var provider = new Provider(clock) { WrongState = true };
        using var app = new WaltTradingClient(() => provider, clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.ConnectAsync(provider.OpenBrowser, TestContext.Current.CancellationToken));
        Assert.Equal(0, provider.Exchanges);
        Assert.Empty(provider.Calls);
    }
}
