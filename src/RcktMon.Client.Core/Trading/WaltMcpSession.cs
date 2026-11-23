using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Web;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RcktMon.Client.Core.Trading;

/// <summary>Official Wallet MCP, its own OAuth grant, never the user's Codex connection.</summary>
internal sealed class WaltMcpSession(McpClient client, HttpClientTransport transport, WaltRequestGuard guard) : IAsyncDisposable
{
    internal static readonly Uri Resource = new("https://walletbot.me/bot-relay/public-api/mcp");
    private const string AuthorizationEndpoint = "https://walletbot.me/bot-relay/public-api/oauth/v1/authorize";
    private static readonly string[] Required = ["perps_guide", "perps_list_contracts", "perps_latest_rate", "perps_prepare_order_place", "perps_list_positions", "perps_prepare_position_close", "perps_execute_operation"];

    public static async Task<WaltMcpSession> ConnectAsync(HttpMessageHandler network, Action<Uri> openBrowser, string? registrationPath, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(4));
        // Bind a single ephemeral loopback port BEFORE DCR. Keep it reserved until the code has been exchanged.
        var registration = WaltPublicRegistration.Read(registrationPath);
        using var callback = new TcpListener(IPAddress.Loopback, registration?.Port ?? 0);
        callback.Start(4);
        var redirect = new Uri($"http://127.0.0.1:{((IPEndPoint)callback.LocalEndpoint).Port}/callback");
        var guard = new WaltRequestGuard(network);
        var http = new HttpClient(guard) { Timeout = TimeSpan.FromMinutes(4) };
        var opts = new HttpClientTransportOptions
        {
            Name = "Walt",
            Endpoint = Resource,
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromMinutes(4),
            MaxReconnectionAttempts = 0,
            EnableStandaloneGetStream = false,
            OAuth = new ClientOAuthOptions
            {
                RedirectUri = redirect,
                ClientId = registration?.ClientId,
                ScopeSelector = _ => ["perps.trade"],
                DynamicClientRegistration = new()
                {
                    ClientName = "RocketMon Desktop",
                    ResponseDelegate = (res, _) =>
                    {
                        // Walt advertises endpoint_auth_method=none. Never persist a secret, token, or grant.
                        if (!string.IsNullOrEmpty(res.ClientSecret) || string.IsNullOrWhiteSpace(res.ClientId))
                        {
                            throw new InvalidDataException("Требуется публичный OAuth-клиент Walt.");
                        }
                        WaltPublicRegistration.Save(registrationPath, new(res.ClientId, redirect.Port));
                        return Task.CompletedTask;
                    },
                },
                AuthorizationCallbackHandler = async (ctx, token) =>
                {
                    if (guard.Connected || ctx.AuthorizationUri.GetLeftPart(UriPartial.Path) != AuthorizationEndpoint ||
                        ctx.RedirectUri != redirect)
                    {
                        throw new InvalidOperationException("Неожиданный адрес авторизации Walt.");
                    }
                    openBrowser(ctx.AuthorizationUri);
                    return await ReadCallbackAsync(callback, redirect, token).ConfigureAwait(false);
                },
            },
        };
        var transport = new HttpClientTransport(opts, http, ownsHttpClient: true);
        McpClient? client = null;
        try
        {
            client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                InitializationTimeout = TimeSpan.FromMinutes(4),
                ClientInfo = new Implementation { Name = "RocketMon Desktop", Version = "1.0" },
            }, cancellationToken: deadline.Token).ConfigureAwait(false);
            if (!guard.TradeGranted) { throw new InvalidOperationException("Нет разрешения perps.trade."); }
            guard.Connected = true;
            var catalog = await client.ListToolsAsync(cancellationToken: deadline.Token).ConfigureAwait(false);
            if (Required.Except(catalog.Select(t => t.Name), StringComparer.Ordinal).Any())
            {
                throw new InvalidOperationException("Walt не предоставил необходимые инструменты.");
            }
            var guide = await client.CallToolAsync("perps_guide", cancellationToken: deadline.Token).ConfigureAwait(false);
            if (guide.IsError == true || !guide.Content.OfType<TextContentBlock>().Any(t => !string.IsNullOrWhiteSpace(t.Text)))
            {
                throw new InvalidOperationException("Справочник Walt недоступен.");
            }
            return new WaltMcpSession(client, transport, guard);
        }
        catch (Exception)
        {
            if (client is not null) { await client.DisposeAsync().ConfigureAwait(false); }
            await transport.DisposeAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Не удалось подключить Walt. Завершите вход в браузере и проверьте разрешение perps.trade.");
        }
    }

    private static async Task<AuthorizationResult> ReadCallbackAsync(TcpListener listener, Uri redirect, CancellationToken ct)
    {
        using TcpClient connection = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
        using NetworkStream stream = connection.GetStream();
        // Bounded, local-only HTTP GET. Neither auth code, callback URI nor tokens are logged.
        byte[] bytes = new byte[8192];
        int length = 0;
        while (length < bytes.Length)
        {
            int n = await stream.ReadAsync(bytes.AsMemory(length), ct).ConfigureAwait(false);
            if (n == 0) { break; }
            length += n;
            if (bytes.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0) { break; }
        }
        string[] lines = Encoding.ASCII.GetString(bytes, 0, length).Split("\r\n", StringSplitOptions.None);
        string[] request = lines[0].Split(' ');
        if (length >= bytes.Length || request.Length != 3 || request[0] != "GET" ||
            !request[1].StartsWith("/callback?", StringComparison.Ordinal) ||
            !lines.Any(line => string.Equals(line, "Host: " + redirect.Authority, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Неверный callback Walt.");
        }
        var query = HttpUtility.ParseQueryString(new Uri(redirect, request[1]).Query);
        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\nReturn to RocketMon to see the authorization result."u8.ToArray(), ct).ConfigureAwait(false);
        return new AuthorizationResult { Code = query["code"], State = query["state"], Iss = query["iss"] };
    }

    internal async Task<JsonElement> CallJsonAsync(string tool, Dictionary<string, object?>? args, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            CallToolResult result = await client.CallToolAsync(tool, args, cancellationToken: deadline.Token).ConfigureAwait(false);
            if (result.IsError == true) { throw new InvalidDataException(); }
            if (result.StructuredContent is { ValueKind: JsonValueKind.Object } obj) { return obj; }
            string? text = result.Content.OfType<TextContentBlock>().SingleOrDefault()?.Text;
            using JsonDocument doc = JsonDocument.Parse(text ?? "");
            return doc.RootElement.Clone();
        }
        catch (Exception)
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Walt не подтвердил операцию. Проверьте подключение, права и счёт в Wallet.");
        }
    }

    internal string Redact(string value) => guard.Redact(value);

    internal async Task<JsonElement> ExecuteOnceAsync(string token, CancellationToken ct)
    {
        guard.ArmOneRequest(token);
        try
        {
            return await CallJsonAsync("perps_execute_operation", new() { ["confirmationToken"] = token }, ct).ConfigureAwait(false);
        }
        finally { guard.Disarm(); }
    }

    public async ValueTask DisposeAsync()
    {
        guard.Disarm();
        try { await client.DisposeAsync().ConfigureAwait(false); }
        finally { await transport.DisposeAsync().ConfigureAwait(false); }
    }
}

/// <summary>Prevent even an SDK re-auth/reconnect from replaying an already-dispatched execute.</summary>
internal sealed class WaltRequestGuard(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private string? _approved, _bearer, _refresh;
    internal string Redact(string value)
    {
        if (!string.IsNullOrEmpty(_bearer)) { value = value.Replace(_bearer, "[скрыто]", StringComparison.Ordinal); }
        if (!string.IsNullOrEmpty(_refresh)) { value = value.Replace(_refresh, "[скрыто]", StringComparison.Ordinal); }
        return value;
    }
    internal volatile bool Connected;
    internal bool TradeGranted { get; private set; }
    private static readonly Uri TokenEndpoint = new("https://walletbot.me/bot-relay/public-api/oauth/v1/token");
    private static readonly Uri RegistrationEndpoint = new("https://walletbot.me/bot-relay/public-api/oauth/v1/register");

    internal void ArmOneRequest(string token) => Interlocked.Exchange(ref _approved, token);
    internal void Disarm() => Interlocked.Exchange(ref _approved, null);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is not { Scheme: "https", Host: "walletbot.me", Port: 443 } uri ||
            !string.IsNullOrEmpty(uri.UserInfo) || (Connected && uri != WaltMcpSession.Resource) ||
            (request.Method == HttpMethod.Post && uri != WaltMcpSession.Resource && uri != TokenEndpoint && uri != RegistrationEndpoint))
        {
            throw new InvalidOperationException("Адрес вне сессии Walt.");
        }
        if (uri == WaltMcpSession.Resource && request.Method == HttpMethod.Post)
        {
            var content = request.Content ?? throw new InvalidDataException();
            byte[] bytes = await content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(bytes);
            var msg = doc.RootElement;
            if (msg.TryGetProperty("method", out var m) && m.GetString() == "tools/call")
            {
                var args = msg.GetProperty("params");
                switch (args.GetProperty("name").GetString())
                {
                    case "perps_execute_operation":
                        var expected = Interlocked.Exchange(ref _approved, null);
                        if (expected is null || expected != args.GetProperty("arguments").GetProperty("confirmationToken").GetString())
                        {
                            throw new InvalidOperationException("Нет разового подтверждения Walt.");
                        }
                        break;
                    case "perps_prepare_position_close":
                    case "perps_list_positions":
                    case "perps_latest_rate":
                    case "perps_list_contracts":
                    case "perps_prepare_order_place":
                    case "perps_guide":
                        break;
                    default:
                        // In particular never call readiness: with trading scope it can create a perps account.
                        throw new InvalidOperationException("Инструмент вне ручного торгового сценария.");
                }
            }
        }
        HttpResponseMessage res = await base.SendAsync(request, ct).ConfigureAwait(false);
        if (uri == TokenEndpoint && res.IsSuccessStatusCode)
        {
            TradeGranted = false;
            try
            {
                // RFC 6749 §5.1: an omitted scope means the requested scope. A returned scope is authoritative.
                // Validate the actual grant, not the advertised tool list, before allowing the SDK to use the token.
                await res.Content.LoadIntoBufferAsync(65_536, ct).ConfigureAwait(false);
                using var grant = JsonDocument.Parse(await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));
                var body = grant.RootElement;
                if (body.ValueKind != JsonValueKind.Object ||
                    !body.TryGetProperty("access_token", out var at) || at.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(at.GetString()) ||
                    !body.TryGetProperty("token_type", out var type) || type.ValueKind != JsonValueKind.String || !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase) ||
                    (body.TryGetProperty("scope", out var scope) &&
                     (scope.ValueKind != JsonValueKind.String || !scope.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("perps.trade", StringComparer.Ordinal))))
                {
                    throw new InvalidOperationException("Walt не выдал торговое разрешение perps.trade.");
                }
                _bearer = at.GetString();
                _refresh = body.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String ? rt.GetString() : null;
                TradeGranted = true;
            }
            catch { res.Dispose(); throw; }
        }
        if (Connected && res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            res.Dispose();
            throw new InvalidOperationException("Сессия Walt завершена. Подключитесь вручную.");
        }
        return res;
    }
}

/// <summary>Only the public dynamic client registration, so reconnecting uses the same RocketMon identity.</summary>
internal sealed record WaltPublicRegistration(string ClientId, int Port)
{
    internal static WaltPublicRegistration? Read(string? path)
    {
        if (path is null || !File.Exists(path)) { return null; }
        var reg = JsonSerializer.Deserialize<WaltPublicRegistration>(File.ReadAllText(path));
        return reg is { Port: > 1023 and <= 65535, ClientId.Length: > 0 and <= 4096 } ? reg :
            throw new InvalidDataException("Некорректная регистрация публичного клиента Walt.");
    }

    internal static void Save(string? path, WaltPublicRegistration reg)
    {
        if (path is null) { return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(reg));
            File.Move(tmp, path, overwrite: false);
        }
        finally { File.Delete(tmp); }
    }
}
