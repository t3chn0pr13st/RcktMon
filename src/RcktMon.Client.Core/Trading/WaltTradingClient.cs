using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RcktMon.Client.Core.Platform;

namespace RcktMon.Client.Core.Trading;

public sealed record WaltContract(int Id, string Symbol, string MarketId, int MaxLeverage, decimal MinMargin,
    decimal MaxMargin, decimal TakerFee, decimal Slippage, decimal HourlyFunding);

/// <summary>Local review handle only. No OAuth credential or remote confirmation token is exposed to the UI.</summary>
public sealed record WaltPreview(Guid Id, string Summary, DateTimeOffset ExpiresAt);

/// <summary>The transport has provably not dispatched an execution request for this intent.</summary>
public sealed class WaltNotDispatchedException(string message) : InvalidOperationException(message);

/// <summary>One private, in-memory MCP connection for manual market orders. No implicit onboarding or background trading.</summary>
public sealed class WaltTradingClient : IDisposable
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly SemaphoreSlim _serial = new(1);
    private readonly TimeProvider _time;
    private readonly string? _registrationPath;
    private readonly Func<HttpMessageHandler> _newHttp;
    private WaltMcpSession? _session;
    private Dictionary<int, WaltContract> _contracts = [];
    private sealed record Position(int ContractId, string Id, string Side, decimal Units);
    private (WaltPreview View, string Token, Position? Close)? _pending;
    private volatile bool _disposed;

    public WaltTradingClient() : this(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        // A new HTTP/1.1 connection for each POST also avoids replay on a stale pooled connection.
        PooledConnectionLifetime = TimeSpan.Zero,
    }, TimeProvider.System, Path.Combine(new AppPaths().ConfigDirectory, "walt-public-client.json"))
    { }

    /// <summary>Inject an isolated HTTP transport and clock for protocol/replay tests.</summary>
    public WaltTradingClient(Func<HttpMessageHandler> newHttp, TimeProvider time, string? registrationPath = null)
    {
        _newHttp = newHttp;
        _time = time;
        _registrationPath = registrationPath;
    }

    public async Task ConnectAsync(Action<Uri> openBrowser, CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pending = null;
            _contracts.Clear();
            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session = null;
            }
            _session = await WaltMcpSession.ConnectAsync(_newHttp(), openBrowser, _registrationPath, cancellationToken);
        }
        finally { _serial.Release(); }
    }

    public async Task<IReadOnlyList<WaltContract>> ListContractsAsync(CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken);
        try
        {
            var session = Session;
            _contracts.Clear();
            _pending = null;
            JsonElement data = await session.CallJsonAsync("perps_list_contracts", null, cancellationToken);
            WaltContract[] contracts = data.GetProperty("contracts").EnumerateArray()
                .Where(c => c.GetProperty("is_active").GetBoolean() &&
                    c.GetProperty("quote_asset").GetString() == "USDC" &&
                    c.GetProperty("venue_status").GetString() == "active")
                .Select(c => new WaltContract(c.GetProperty("contract_id").GetInt32(),
                    c.GetProperty("contract_name").GetString()!,
                    c.GetProperty("base_asset").GetString() + "-USD-PERP", c.GetProperty("max_leverage").GetInt32(),
                    Number(c, "min_size_quote"), Number(c, "max_size_quote"), Number(c, "taker_fee_pct"),
                    Number(c, "price_slippage"), Number(c, "funding_rate_hourly")))
                .OrderBy(c => c.Symbol, StringComparer.Ordinal).ToArray();
            _contracts = contracts.ToDictionary(c => c.Id);
            return contracts;
        }
        finally { _serial.Release(); }
    }

    public async Task<WaltPreview> PrepareAsync(int contractId, string side, int leverage, decimal marginUsdc,
        CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken);
        try
        {
            _pending = null;
            var session = Session;
            if (!_contracts.TryGetValue(contractId, out WaltContract? c) || side is not ("long" or "short") ||
                leverage < 1 || leverage > c.MaxLeverage || marginUsdc <= 0 ||
                marginUsdc < c.MinMargin || marginUsdc > c.MaxMargin)
            {
                throw new InvalidOperationException("Проверьте контракт, сторону, плечо и пределы маржи USDC.");
            }
            JsonElement rate = await session.CallJsonAsync("perps_latest_rate", new() { ["marketId"] = c.MarketId }, cancellationToken);
            if (rate.GetProperty("market_id").GetString() != c.MarketId || Number(rate, "rate") is not > 0)
            {
                throw new InvalidOperationException("Walt не дал валидную цену этого контракта.");
            }
            decimal price = Number(rate, "rate");
            var args = new Dictionary<string, object?>
            {
                ["contractId"] = c.Id,
                ["side"] = side,
                ["type"] = "market",
                ["leverage"] = leverage,
                ["amount"] = marginUsdc.ToString("G29", Invariant),
                ["price"] = price.ToString("G29", Invariant),
            };
            DateTimeOffset start = _time.GetUtcNow();
            JsonElement data = await session.CallJsonAsync("perps_prepare_order_place", args, cancellationToken);
            string summary = FormattableString.Invariant(
                $"Walt · {c.Symbol} (#{c.Id}) · {side.ToUpperInvariant()} · MARKET\nМаржа: {marginUsdc:G29} USDC · плечо: {leverage}× · ориентир: {price:G29} USDC\nКомиссия taker: {c.TakerFee:P4} · проскальзывание контракта: {c.Slippage:P2} · funding за час: {c.HourlyFunding:P4}");
            return AcceptReview("order.place", args, start, data, summary);
        }
        finally { _serial.Release(); }
    }

    /// <summary>Human-initiated, contract-scoped review; never auto-discover the wallet's other positions.</summary>
    public async Task<WaltPreview> PrepareCloseAsync(int contractId, CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken);
        try
        {
            _pending = null;
            var session = Session;
            if (!_contracts.TryGetValue(contractId, out var c))
            {
                throw new InvalidOperationException("Выберите контракт Walt из списка.");
            }
            var (position, p) = await OpenPositionAsync(session, contractId, cancellationToken);
            string side = position.Side;
            decimal units = position.Units;
            JsonElement rate = await session.CallJsonAsync("perps_latest_rate", new() { ["marketId"] = c.MarketId }, cancellationToken);
            if (rate.GetProperty("market_id").GetString() != c.MarketId || Number(rate, "rate") <= 0)
            {
                throw new InvalidOperationException("Нет валидной цены выбранного контракта.");
            }
            decimal reference = Number(rate, "rate");
            string liquidation = "нет данных";
            if (p.TryGetProperty("liquidation_price", out var lp) &&
                decimal.TryParse(lp.ToString(), NumberStyles.Float, Invariant, out decimal liq) && liq > 0)
            {
                decimal distance = (side == "long" ? reference - liq : liq - reference) / reference;
                liquidation = FormattableString.Invariant($"{liq:G29} USDC (расстояние {distance:P2})");
            }
            string baseAsset = c.MarketId[..^"-USD-PERP".Length];
            string summary = FormattableString.Invariant(
                $"Walt · ЗАКРЫТЬ ВСЮ ПОЗИЦИЮ · {c.Symbol} (#{c.Id})\nID позиции: {position.Id} · Позиция {side.ToUpperInvariant()}: {units:G29} {baseAsset} · ориентир {reference:G29} USDC\nЛиквидация: {liquidation} · funding за час {c.HourlyFunding:P4} · taker {c.TakerFee:P4} · проскальзывание {c.Slippage:P2}");
            var args = new Dictionary<string, object?> { ["contractId"] = contractId };
            DateTimeOffset start = _time.GetUtcNow();
            JsonElement data = await session.CallJsonAsync("perps_prepare_position_close", args, cancellationToken);
            return AcceptReview("position.close", args, start, data, summary, position);
        }
        finally { _serial.Release(); }
    }

    private WaltPreview AcceptReview(string expectedKind, Dictionary<string, object?> args,
        DateTimeOffset start, JsonElement data, string summary, Position? closing = null)
    {
        if (!data.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !data.TryGetProperty("confirmationToken", out var t) || t.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(t.GetString()) ||
            !data.TryGetProperty("expiresAt", out var exp) || exp.ValueKind != JsonValueKind.String ||
            !exp.TryGetDateTimeOffset(out DateTimeOffset expiry))
        {
            throw new InvalidOperationException("Walt не подтвердил предпросмотр. Проверьте права, торговый счёт и параметры в Wallet.");
        }
        if (!data.TryGetProperty("action", out var action) || !MatchesAction(action, args, expectedKind, closing) ||
            (data.TryGetProperty("kind", out var kind) && kind.ToString() != expectedKind))
        {
            throw new InvalidOperationException("Walt не вернул точное действие и параметры исходной заявки.");
        }
        if (!data.TryGetProperty("risk", out var risk) || !VerifiedRisk(risk))
        {
            throw new InvalidOperationException("Нет положительного результата проверок риска Walt. Ничего не отправлено.");
        }
        string token = t.GetString()!;
        if (expiry > start.AddSeconds(90)) { expiry = start.AddSeconds(90); }
        if (_time.GetUtcNow() >= expiry) { throw new InvalidOperationException("Предпросмотр Walt уже истёк."); }
        // The one-time token is minted by Walt for this exact call on this connection. Never accept a token
        // from the UI or serialize the raw tool result. Retain the actual provider risk assessment for review.
        foreach (string name in new[] { "summary", "action", "risk", "warnings" })
        {
            if (data.TryGetProperty(name, out JsonElement block))
            {
                summary += "\n" + name + ": " + SafeReview(block, value => Session.Redact(value.Replace(token, "[скрыто]", StringComparison.Ordinal)));
            }
        }
        var view = new WaltPreview(Guid.NewGuid(), Session.Redact(summary.Replace(token, "[скрыто]", StringComparison.Ordinal)), expiry);
        _pending = (view, token, closing);
        return view;
    }

    private static async Task<(Position, JsonElement)> OpenPositionAsync(WaltMcpSession session, int contractId, CancellationToken ct)
    {
        JsonElement found = await session.CallJsonAsync("perps_list_positions",
            new() { ["contractId"] = contractId, ["statuses"] = new[] { "open" } }, ct);
        if (!found.TryGetProperty("positions", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() != 1)
        {
            throw new InvalidOperationException("Для закрытия нужна ровно одна открытая позиция выбранного контракта.");
        }
        var p = list[0];
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("contract_id", out var contract) ||
            !contract.TryGetInt32(out var id) || id != contractId || !p.TryGetProperty("status", out var status) ||
            status.ToString() != "open" || !p.TryGetProperty("side", out var side) || side.ToString() is not ("long" or "short") ||
            !p.TryGetProperty("value_base", out var amt) || !decimal.TryParse(amt.ToString(), NumberStyles.Float, Invariant, out var units) || units <= 0 ||
            !(p.TryGetProperty("position_id", out var pid) || p.TryGetProperty("id", out pid)) ||
            pid.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) || !Regex.IsMatch(pid.ToString(), @"\A[A-Za-z0-9_.:-]{1,128}\z"))
        {
            throw new InvalidOperationException("Walt не подтвердил идентификатор, контракт, сторону и объём открытой позиции.");
        }
        return (new Position(contractId, pid.ToString(), side.ToString(), units), p);
    }

    private static bool VerifiedRisk(JsonElement risk)
    {
        if (risk.ValueKind != JsonValueKind.Object || IsBlockedRisk(risk)) { return false; }
        if (risk.TryGetProperty("checks", out var checks))
        {
            return checks.ValueKind == JsonValueKind.Array && checks.GetArrayLength() > 0 &&
                checks.EnumerateArray().All(ExplicitPass);
        }
        return ExplicitPass(risk);
    }

    private static bool ExplicitPass(JsonElement node) => node.ValueKind == JsonValueKind.Object && node.EnumerateObject().Any(p =>
        (p.Name is "ok" or "passed" or "allowed" or "pass" && p.Value.ValueKind == JsonValueKind.True) ||
        (p.Name is "status" or "verdict" or "result" && p.Value.ToString() is "ok" or "pass" or "passed" or "allowed"));

    private static bool IsBlockedRisk(JsonElement risk)
    {
        // A new schema cannot silently override a known failed check, even if the top-level ok flag disagrees.
        if (risk.ValueKind == JsonValueKind.Array) { return risk.EnumerateArray().Any(IsBlockedRisk); }
        if (risk.ValueKind != JsonValueKind.Object) { return false; }
        foreach (var prop in risk.EnumerateObject())
        {
            if (prop.Name is "ok" or "allowed" or "passed" or "pass" && prop.Value.ValueKind == JsonValueKind.False ||
                prop.Name is "blocked" or "blocking" && prop.Value.ValueKind == JsonValueKind.True ||
                prop.Name is "status" or "verdict" or "result" && prop.Value.ToString() is "blocked" or "fail" or "failed" or "denied" or "rejected" ||
                IsBlockedRisk(prop.Value)) { return true; }
        }
        return false;
    }

    /// <summary>Only call from a separate human confirmation of the unedited preview. No retries or synthetic fills.</summary>
    public async Task<string> ExecuteAsync(Guid previewId, CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken);
        try
        {
            var session = Session;
            var preview = _pending;
            _pending = null;
            if (preview is not { } p || p.View.Id != previewId || _time.GetUtcNow() >= p.View.ExpiresAt)
            {
                throw new WaltNotDispatchedException("Нет действующего предпросмотра. Ничего не отправлено.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (p.Close is { } closing)
            {
                // The remote close request is keyed only by contract, not by position ID. Never close a
                // replacement position silently if the user traded in Wallet since reviewing this one.
                try
                {
                    var (current, _) = await OpenPositionAsync(session, closing.ContractId, cancellationToken);
                    if (current != closing || _time.GetUtcNow() >= p.View.ExpiresAt) { throw new InvalidDataException(); }
                }
                catch (Exception)
                {
                    throw new WaltNotDispatchedException("Позиция изменилась или сверка недоступна. Закрытие не отправлено.");
                }
            }
            try
            {
                JsonElement result = await session.ExecuteOnceAsync(p.Token, cancellationToken);
                if (result.TryGetProperty("ok", out var ok) && ok.ValueKind != JsonValueKind.True)
                {
                    throw new InvalidDataException();
                }
                string? id = OrderId(result);
                if (id is null && result.TryGetProperty("result", out var sub)) { id = OrderId(sub); }
                if (id is null && result.TryGetProperty("data", out sub)) { id = OrderId(sub); }
                if (id is null || id.Contains(p.Token, StringComparison.Ordinal) || session.Redact(id) != id) { throw new InvalidDataException(); }
                return "Walt принял заявку № " + id + ". Исполнение не подтверждено; проверьте статус в Wallet.";
            }
            catch (Exception)
            {
                // Never forward a tool response or an exception that might contain credentials.
                throw new InvalidOperationException("Результат отправки Walt не подтверждён. Сверьте заявки и позиции в Wallet; не отправляйте повторно.");
            }
        }
        finally { _serial.Release(); }
    }

    private static bool MatchesAction(JsonElement action, Dictionary<string, object?> requested, string expectedKind, Position? closing)
    {
        if (action.ValueKind != JsonValueKind.Object || !action.TryGetProperty("kind", out var kind) ||
            kind.ValueKind != JsonValueKind.String || kind.GetString() != expectedKind) { return false; }

        // Do not mistake the token or a friendly summary for a verifiable intent. All the parameters
        // sent to the preview endpoint must be echoed by the server. Unknown schemas disable submission.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool Read(JsonElement obj)
        {
            if (obj.ValueKind != JsonValueKind.Object) { return false; }
            foreach (var prop in obj.EnumerateObject())
            {
                if (prop.Name is "args" or "params" or "arguments" or "request" or "payload")
                {
                    if (!Read(prop.Value)) { return false; }
                    continue;
                }
                if (prop.Name == "kind" && prop.Value.ToString() != expectedKind) { return false; }
                if (prop.Name is "autoClose" or "auto_close" && prop.Value.ValueKind != JsonValueKind.Null) { return false; }
                if (closing is not null &&
                    ((prop.Name is "positionId" or "position_id" && prop.Value.ToString() != closing.Id) ||
                     (prop.Name == "side" && prop.Value.ToString() != closing.Side) ||
                     (prop.Name == "value_base" && (!decimal.TryParse(prop.Value.ToString(), NumberStyles.Float, Invariant, out var size) || size != closing.Units)))) { return false; }
                string key = prop.Name switch { "contract_id" => "contractId", "order_type" or "orderType" => "type", _ => prop.Name };
                if (!requested.TryGetValue(key, out object? expected)) { continue; }
                string want = Convert.ToString(expected, Invariant)!;
                string actual = prop.Value.ToString();
                if (key is "side" or "type")
                {
                    if (actual != want) { return false; }
                }
                else if (prop.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) ||
                    !decimal.TryParse(actual, NumberStyles.Float, Invariant, out var num) ||
                    num != decimal.Parse(want, NumberStyles.Float, Invariant)) { return false; }
                seen.Add(key);
            }
            return true;
        }
        return Read(action) && requested.Keys.All(seen.Contains);
    }

    private static string? OrderId(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object ||
            !(obj.TryGetProperty("order_id", out var id) || obj.TryGetProperty("orderId", out id))) { return null; }
        string? value = id.ValueKind switch { JsonValueKind.String => id.GetString(), JsonValueKind.Number => id.GetRawText(), _ => null };
        return value is not null && Regex.IsMatch(value, @"\A[A-Za-z0-9_.:-]{1,128}\z") ? value : null;
    }

    private static string SafeReview(JsonElement element, Func<string, string> redact)
    {
        // Provider data is displayed as inert text; do not serialize credential properties, including nested ones.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) { WriteReview(writer, element, redact); }
        string text = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        // Never hide later warnings by truncating to fit a card; fail closed on an unexpected payload.
        return text.Length <= 16_384 ? text : throw new InvalidDataException("Слишком длинный ответ Walt.");
    }

    private static void WriteReview(Utf8JsonWriter writer, JsonElement node, Func<string, string> redact)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in node.EnumerateObject())
                {
                    if (prop.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                        prop.Name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                        prop.Name.Contains("authorization", StringComparison.OrdinalIgnoreCase)) { continue; }
                    writer.WritePropertyName(redact(prop.Name));
                    WriteReview(writer, prop.Value, redact);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in node.EnumerateArray()) { WriteReview(writer, item, redact); }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(redact(node.GetString()!));
                break;
            default: node.WriteTo(writer); break;
        }
    }

    private WaltMcpSession Session => !_disposed && _session is { } session ? session :
        throw new InvalidOperationException("Сначала подключите Walt для этой сессии RocketMon.");

    private static decimal Number(JsonElement node, string name) =>
        decimal.Parse(node.GetProperty(name).ToString(), NumberStyles.Float, Invariant);

    public void Dispose()
    {
        _disposed = true;
        // Do not block the UI thread (a browser callback can still be in flight).
        _ = CloseAsync();
    }

    private async Task CloseAsync()
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            _pending = null;
            if (_session is not null) { await _session.DisposeAsync().ConfigureAwait(false); }
            _session = null;
        }
        finally { _serial.Release(); }
    }
}
