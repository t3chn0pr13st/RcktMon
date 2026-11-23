using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

[assembly: InternalsVisibleTo("RcktMon.Client.Core.Tests")]

namespace RcktMon.Client.Core.Trading;

public enum FinamSide { Buy, Sell }

/// <summary>Recorded means that a server-side order was positively identified, not that it filled.</summary>
public enum FinamOutcome { Recorded, Rejected, Unknown }

public sealed record FinamOrderResult(FinamOutcome Outcome, string? OrderId, string Status);

public sealed record FinamOrderReview(
    string AccountId, string Symbol, string Board, FinamSide Side, int Lots, long LotSize,
    long Shares, decimal LimitPrice, decimal Notional, string Currency, string ClientOrderId,
    DateTimeOffset ExpiresAt, string AssetId, string Isin, decimal PriceStep);

/// <summary>
/// Manual Finam Trade API v1 client. Both demo and brokerage accounts use the same
/// production host; we never infer that an account is paper from its ID. No background
/// trading, order retries, secret storage, unrestricted URLs or default account selection.
/// Contract: github.com/FinamWeb/finam-trade-api/blob/main/docs/swagger/api.swagger.json.
/// </summary>
public sealed class FinamTradingClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private string[] _accounts = [];
    private DateTimeOffset _expiresAt;
    private FinamOrderReview? _review;

    /// <summary>
    /// True until TokenDetails explicitly returns readonly=false. In particular a gateway
    /// omitting the proto3 default does NOT establish permission to trade on this client.
    /// </summary>
    public bool IsReadOnly { get; private set; } = true;

    public FinamTradingClient() : this(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.Zero
    }, TimeProvider.System) { }

    internal FinamTradingClient(HttpMessageHandler handler, TimeProvider clock)
    {
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.finam.ru/v1/"),
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = 4 * 1024 * 1024
        };
        _clock = clock;
    }

    public Task<IReadOnlyList<string>> ConnectAsync(string secret, CancellationToken ct = default) =>
        LockedAsync<IReadOnlyList<string>>(async () =>
        {
            ClearSession();
            if (string.IsNullOrWhiteSpace(secret)) throw Invalid("Введите персональный токен Finam.");
            using var auth = await ReadAsync("sessions", ct, body: new { secret });
            string token = RequiredText(auth.RootElement, "token");
            if (token.Length > 16384 || token.Any(c => c < 0x21 || c > 0x7e))
                throw Invalid("Получен недопустимый токен сессии Finam.");
            using var details = await ReadAsync("sessions/details", ct, body: new { token });
            var root = details.RootElement;
            var accounts = Items(root, "account_ids").Select(a => a.GetString() ?? "").Distinct().ToArray();
            if (accounts.Length == 0 || accounts.Any(a => !IsId(a)))
                throw Invalid("Список доступных счетов Finam пуст или некорректен.");
            var expires = Required(root, "expires_at");
            if (!expires.TryGetDateTimeOffset(out var end) || end <= _clock.GetUtcNow())
                throw Invalid("Сессия Finam истекла. Подключитесь заново.");

            // Missing/unknown privilege must never enable sending an order.
            IsReadOnly = !root.TryGetProperty("readonly", out var flag) || flag.ValueKind != JsonValueKind.False;
            _token = token;
            _accounts = accounts;
            _expiresAt = end;
            return Array.AsReadOnly(accounts);
        }, ct);

    public Task<FinamOrderReview> PrepareAsync(
        string accountId, string ticker, FinamSide side, int lots, decimal limitPrice, CancellationToken ct = default) =>
        LockedAsync(async () =>
        {
            _review = null;
            RequireAccount(accountId);
            string name = (ticker ?? "").Trim().ToUpperInvariant();
            if (name.EndsWith("@MISX", StringComparison.Ordinal)) name = name[..^5];
            if (name.Length is 0 or > 24 || !name.All(char.IsAsciiLetterOrDigit))
                throw Invalid("Введите тикер акции Мосбиржи, например SBER.");
            if (side is not (FinamSide.Buy or FinamSide.Sell) || lots < 1 || limitPrice <= 0)
                throw Invalid("Укажите покупку или продажу, положительное число лотов и лимитную цену.");
            var asset = await InstrumentAsync(accountId, name, ct);
            if (limitPrice % asset.Step != 0) throw Invalid("Лимитная цена не кратна шагу цены.");
            long shares = checked(lots * asset.Lot);
            decimal notional = checked(limitPrice * shares);
            string symbol = name + "@MISX";
            await CheckCoverageAsync(accountId, symbol, side, shares, notional, ct);
            DateTimeOffset now = _clock.GetUtcNow();
            if (now >= _expiresAt) throw Invalid("Сессия Finam истекла. Подключитесь заново.");
            var expiry = now.AddSeconds(60);
            _review = new FinamOrderReview(accountId, symbol, "TQBR", side, lots, asset.Lot, shares,
                limitPrice, notional, "RUB", Convert.ToHexString(RandomNumberGenerator.GetBytes(10)),
                expiry < _expiresAt ? expiry : _expiresAt, asset.Id, asset.Isin, asset.Step);
            return _review;
        }, ct);

    // Only the exact object last returned by this instance can be submitted, once. UI
    // persists its client order ID BEFORE calling this method, including on first attempt.
    public Task<FinamOrderResult> SubmitAsync(FinamOrderReview review, CancellationToken ct = default) =>
        LockedAsync(async () =>
        {
            if (review == null || !ReferenceEquals(_review, review))
                return NotSent("Предпросмотр недействителен или уже использован.");
            _review = null;
            if (IsReadOnly) return NotSent("Токен только для чтения.");
            try
            {
                RequireAccount(review.AccountId);
                if (review.ExpiresAt <= _clock.GetUtcNow()) return NotSent("Предпросмотр устарел.");
                var asset = await InstrumentAsync(review.AccountId, review.Symbol[..^5], ct);
                if (asset.Lot != review.LotSize || asset.Step != review.PriceStep ||
                    asset.Id != review.AssetId || asset.Isin != review.Isin)
                    return NotSent("Изменились параметры инструмента.");
                await CheckCoverageAsync(review.AccountId, review.Symbol, review.Side, review.Shares, review.Notional, ct);
                ct.ThrowIfCancellationRequested();
                if (review.ExpiresAt <= _clock.GetUtcNow()) return NotSent("Предпросмотр устарел.");
            }
            catch (InvalidOperationException ex) { return NotSent(ex.Message); }
            catch (Exception ex) when (IsTransportOrFormatError(ex))
            {
                return NotSent("Не удалось перепроверить счёт или инструмент.");
            }

            using var request = MakeRequest(AccountPath(review.AccountId) + "/orders", _token, new
            {
                symbol = review.Symbol,
                quantity = new { value = review.Shares.ToString(CultureInfo.InvariantCulture) },
                side = review.Side == FinamSide.Buy ? "SIDE_BUY" : "SIDE_SELL",
                type = "ORDER_TYPE_LIMIT",
                time_in_force = "TIME_IN_FORCE_DAY",
                limit_price = new { value = review.LimitPrice.ToString(CultureInfo.InvariantCulture) },
                client_order_id = review.ClientOrderId
            });
            try
            {
                using var response = await _http.SendAsync(request, ct);
                // Only definitive, documented input/auth/not-found denials, not conflicts,
                // timeouts, rate limits, redirects or gateway errors, count as rejections.
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                    return new FinamOrderResult(FinamOutcome.Rejected, null, $"HTTP {(int)response.StatusCode}");
                if (response.StatusCode != HttpStatusCode.OK) return Unknown();
                using var data = await ParseAsync(response, ct);
                return Identify(data.RootElement, review.AccountId, review.ClientOrderId, review);
            }
            catch (Exception ex) when (IsTransportOrFormatError(ex) || ex is InvalidOperationException)
            {
                // A dropped ACK is not a rejected order. The only next operation is lookup.
                return Unknown();
            }
        }, ct);

    /// <summary>Never interprets absence from the order listing as proof an order failed.</summary>
    public Task<FinamOrderResult> LookupAsync(string accountId, string clientOrderId, CancellationToken ct = default) =>
        LockedAsync(async () =>
        {
            RequireAccount(accountId);
            if (!IsId(clientOrderId) || clientOrderId.Length > 20)
                throw Invalid("Некорректный ID сверки.");
            using var data = await ReadAsync(AccountPath(accountId) + "/orders", ct, _token);
            var matches = Items(data.RootElement, "orders").Where(row =>
                row.TryGetProperty("order", out var order) && OptionalText(order, "client_order_id") == clientOrderId)
                .Take(2).ToList();
            return matches.Count == 1 ? Identify(matches[0], accountId, clientOrderId) : Unknown();
        }, ct);

    private sealed record Instrument(string Id, string Isin, long Lot, decimal Step);

    private async Task<Instrument> InstrumentAsync(string accountId, string ticker, CancellationToken ct)
    {
        string symbol = ticker + "@MISX";
        string path = "assets/" + Uri.EscapeDataString(symbol);
        string query = "?account_id=" + Uri.EscapeDataString(accountId);
        using var definition = await ReadAsync(path + query, ct, _token);
        var asset = definition.RootElement;
        if (OptionalText(asset, "ticker") != ticker || OptionalText(asset, "mic") != "MISX" ||
            OptionalText(asset, "board") != "TQBR" ||
            !string.Equals(OptionalText(asset, "type"), "EQUITIES", StringComparison.OrdinalIgnoreCase) ||
            OptionalText(asset, "quote_currency") != "RUB")
            throw Invalid("Допускаются только акции MISX / TQBR с котировкой в RUB.");
        string id = RequiredText(asset, "id"), isin = RequiredText(asset, "isin");
        if (isin.Length != 12 || !isin.StartsWith("RU", StringComparison.Ordinal) || !isin.All(char.IsAsciiLetterOrDigit))
            throw Invalid("Допускаются только акции с российским ISIN.");
        long step = Integral(asset, "min_step"), digits = Integral(asset, "decimals");
        if (step <= 0 || digits is < 0 or > 12) throw Invalid("Неизвестный или переменный шаг цены.");
        decimal tick = step;
        for (int i = 0; i < digits; i++) tick /= 10m;

        using var parameters = await ReadAsync(path + "/params" + query, ct, _token);
        var trade = parameters.RootElement;
        if (OptionalText(trade, "symbol") != symbol || OptionalText(trade, "account_id") != accountId ||
            !trade.TryGetProperty("is_tradable", out var tradable) || tradable.ValueKind != JsonValueKind.True)
            throw Invalid("По выбранному счёту торговля инструментом не подтверждена.");
        long lot = Integral(trade, "trade_lot_size");
        if (lot < 1 || DecimalValue(asset, "lot_size") != lot)
            throw Invalid("Торговый размер лота отсутствует или отличается от биржевого.");
        return new Instrument(id, isin, lot, tick);
    }

    private async Task CheckCoverageAsync(string account, string symbol, FinamSide side, long shares, decimal notional, CancellationToken ct)
    {
        string path = AccountPath(account);
        using var orders = await ReadAsync(path + "/orders", ct, _token);
        // Finam's position may include theoretical quantities from unfilled orders.
        // Refuse an account with any unresolved order rather than count it as owned cash/stock.
        if (Items(orders.RootElement, "orders").Any(o => OptionalText(o, "status") is not
            ("ORDER_STATUS_FILLED" or "ORDER_STATUS_EXECUTED" or "ORDER_STATUS_CANCELED" or
             "ORDER_STATUS_EXPIRED" or "ORDER_STATUS_REJECTED" or "ORDER_STATUS_DENIED_BY_BROKER" or
             "ORDER_STATUS_REJECTED_BY_EXCHANGE")))
            throw Invalid("На счёте есть незавершённые заявки. Доступное покрытие не подтверждено.");
        using var data = await ReadAsync(path, ct, _token);
        var snapshot = data.RootElement;
        if (OptionalText(snapshot, "account_id") != account) throw Invalid("Счёт в ответе не совпадает с выбранным.");
        if (side == FinamSide.Sell)
        {
            var positions = Items(snapshot, "positions").Where(p => OptionalText(p, "symbol") == symbol).Take(2).ToList();
            if (positions.Count != 1 || DecimalValue(positions[0], "quantity") < shares)
                throw Invalid("Недостаточно собственных акций для продажи без шорта.");
        }
        else
        {
            var wallets = Items(snapshot, "cash").Where(p => OptionalText(p, "currency_code") == "RUB").Take(2).ToList();
            if (wallets.Count != 1) throw Invalid("Остаток собственных средств в RUB не подтверждён.");
            long units = Integral(wallets[0], "units");
            long nanos = wallets[0].TryGetProperty("nanos", out _) ? Integral(wallets[0], "nanos") : 0;
            if (Math.Abs((decimal)nanos) >= 1_000_000_000m || units < 0 || nanos < 0 ||
                units + nanos / 1_000_000_000m <= notional)
                throw Invalid("Недостаточно собственных RUB с остатком на комиссию.");
        }
    }

    private static FinamOrderResult Identify(JsonElement state, string account, string clientId, FinamOrderReview? review = null)
    {
        string? status = OptionalText(state, "status"), id = OptionalText(state, "order_id");
        if (!IsId(id) || string.IsNullOrEmpty(status) || !status.StartsWith("ORDER_STATUS_", StringComparison.Ordinal)
            || status == "ORDER_STATUS_UNSPECIFIED" || !state.TryGetProperty("order", out var order)
            || OptionalText(order, "account_id") != account || OptionalText(order, "client_order_id") != clientId)
            return Unknown();
        if (review != null && (OptionalText(order, "symbol") != review.Symbol ||
            OptionalText(order, "side") != (review.Side == FinamSide.Buy ? "SIDE_BUY" : "SIDE_SELL") ||
            OptionalText(order, "type") != "ORDER_TYPE_LIMIT" || OptionalText(order, "time_in_force") != "TIME_IN_FORCE_DAY" ||
            DecimalValue(order, "quantity") != review.Shares || DecimalValue(order, "limit_price") != review.LimitPrice))
            return Unknown();
        var outcome = status is "ORDER_STATUS_REJECTED" or "ORDER_STATUS_DENIED_BY_BROKER" or "ORDER_STATUS_REJECTED_BY_EXCHANGE"
            ? FinamOutcome.Rejected : FinamOutcome.Recorded;
        return new FinamOrderResult(outcome, id, status);
    }

    private static FinamOrderResult Unknown() => new(FinamOutcome.Unknown, null, "Результат неизвестен. Сверьте заявку по ID, не повторяйте отправку.");
    private static FinamOrderResult NotSent(string reason) => new(FinamOutcome.Rejected, null, "Не отправлено: " + reason);
    private static string AccountPath(string account) => "accounts/" + Uri.EscapeDataString(account);

    private void RequireAccount(string account)
    {
        if (_token == null || _expiresAt <= _clock.GetUtcNow()) throw Invalid("Подключитесь к Finam заново.");
        if (!_accounts.Contains(account, StringComparer.Ordinal)) throw Invalid("Счёт не входит в список этого токена.");
    }

    private async Task<T> LockedAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await action(); }
        finally { _gate.Release(); }
    }

    private static bool IsId(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static InvalidOperationException Invalid(string message) => new(message);
    private static JsonElement Required(JsonElement root, string key) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value)
        ? value : throw Invalid("Отсутствует обязательное поле Finam: " + key);
    private static string? OptionalText(JsonElement root, string key) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    private static string RequiredText(JsonElement root, string key) => OptionalText(root, key) is { Length: > 0 } text
        ? text : throw Invalid("Некорректное поле Finam: " + key);
    private static JsonElement.ArrayEnumerator Items(JsonElement root, string key) => Required(root, key).EnumerateArray();

    private static long Integral(JsonElement root, string key)
    {
        var v = Required(root, key);
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n)
            ? n : throw Invalid("Некорректное целое число Finam: " + key);
    }

    private static decimal DecimalValue(JsonElement root, string key) =>
        decimal.TryParse(RequiredText(Required(root, key), "value"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var n) ? n : throw Invalid("Некорректное число Finam: " + key);

    private static bool IsTransportOrFormatError(Exception ex) =>
        ex is HttpRequestException or IOException or OperationCanceledException or JsonException or FormatException;

    private static HttpRequestMessage MakeRequest(string path, string? token, object? body)
    {
        var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
        if (token != null) request.Headers.Add("Authorization", token);
        if (body != null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<JsonDocument> ReadAsync(string path, CancellationToken ct, string? token = null, object? body = null)
    {
        using var request = MakeRequest(path, token, body);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw Invalid($"Finam HTTP {(int)response.StatusCode}. Проверьте токен, счёт и доступность API.");
        return await ParseAsync(response, ct);
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private void ClearSession()
    {
        _token = null;
        _accounts = [];
        IsReadOnly = true;
        _review = null;
        _expiresAt = default;
    }

    public void Dispose()
    {
        ClearSession();
        _http.Dispose();
    }
}
