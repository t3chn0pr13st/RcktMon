using System.Reactive.Subjects;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using RcktMon.Client.Core.Market;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;

namespace RcktMon.Client.Core.Connection;

/// <summary>
/// Owns the gRPC channel and keeps the quote, signal and status streams alive for as long
/// as the app is running.
/// </summary>
/// <remarks>
/// <para>
/// The supervisor is written by hand rather than delegating to a transport's automatic
/// reconnect, because resume has to be explicit. Every message carries a sequence number
/// and the client asks to resume from the last one it applied; if the server can still
/// serve that point from its ring it replays the difference, otherwise it sends a fresh
/// snapshot. A transport that silently drops in-flight messages on reconnect would lose
/// alerts without anyone noticing, which is the worst failure this product can have.
/// </para>
/// <para>
/// The three streams live and die together. That is deliberate: they share one channel, a
/// failure on one almost always means the connection is gone, and a client showing live
/// quotes next to a frozen signal feed would be lying about which alerts have fired.
/// </para>
/// </remarks>
public sealed class RcktMonConnection : IAsyncDisposable
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    /// <summary>How long quotes may go unrefreshed before the connection counts as degraded.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);

    /// <summary>How far back the signal feed is backfilled on the first connection.</summary>
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(3);

    private readonly ConnectionSettings _settings;
    private readonly QuoteStore _store;
    private readonly SignalStore? _signals;
    private readonly MarketStatusStore? _status;
    private readonly ILogger<RcktMonConnection> _logger;
    private readonly BehaviorSubject<ConnectionStatus> _state = new(new ConnectionStatus(ConnectionState.Disconnected));

    private GrpcChannel? _channel;
    private string _catalogEtag = string.Empty;
    private uint _lastDroppedTicks;
    private bool _historyLoaded;

    public RcktMonConnection(
        ConnectionSettings settings,
        QuoteStore store,
        ILogger<RcktMonConnection> logger,
        SignalStore? signals = null,
        MarketStatusStore? status = null)
    {
        _settings = settings;
        _store = store;
        _logger = logger;
        _signals = signals;
        _status = status;
    }

    /// <summary>Current and subsequent connection states.</summary>
    public IObservable<ConnectionStatus> Status => _state;

    /// <summary>The most recent state, for callers that just want to read it once.</summary>
    public ConnectionStatus CurrentStatus => _state.Value;

    /// <summary>Server handshake result, available once connected at least once.</summary>
    public HelloResponse? Session { get; private set; }

    /// <summary>Alert-profile calls, available once connected.</summary>
    public ProfileClient? Profiles { get; private set; }

    /// <summary>Candle history for the inspector chart, available once connected.</summary>
    public HistoryClient? History { get; private set; }

    /// <summary>True when the server advertised the given capability for this account.</summary>
    public bool Can(string capability) => Session?.Capabilities.Contains(capability) is true;

    /// <summary>
    /// Runs until <paramref name="ct"/> is cancelled or the failure is terminal
    /// (a rejected key, or a client too old for the server).
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        int attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            string? failure;

            try
            {
                Publish(attempt == 0 ? ConnectionState.Connecting : ConnectionState.Reconnecting);

                await SessionAndStreamAsync(ct).ConfigureAwait(false);

                // A clean end of stream is still a disconnect; reconnect promptly rather
                // than treating it as success.
                attempt = 0;
                failure = "Сервер закрыл поток";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException rpc) when (rpc.StatusCode is StatusCode.Unauthenticated or StatusCode.PermissionDenied)
            {
                _logger.LogError("Access key rejected: {Status} {Detail}", rpc.StatusCode, rpc.Status.Detail);
                Publish(ConnectionState.Unauthorized, rpc.Status.Detail);
                return;
            }
            catch (ProtocolVersionMismatchException ex)
            {
                _logger.LogError("{Message}", ex.Message);
                Publish(ConnectionState.VersionMismatch, ex.Message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Quote stream failed");
                failure = ex is RpcException rpcEx ? rpcEx.Status.Detail : ex.Message;
            }

            _status?.Reset();

            TimeSpan delay = Backoff(++attempt);
            Publish(ConnectionState.Reconnecting, failure, delay);

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Publish(ConnectionState.Disconnected);
    }

    private async Task SessionAndStreamAsync(CancellationToken ct)
    {
        GrpcChannel channel = EnsureChannel();
        CallInvoker invoker = channel.Intercept(new AuthInterceptor(_settings));

        await HandshakeAsync(invoker, ct).ConfigureAwait(false);

        Profiles = new ProfileClient(invoker);
        History = new HistoryClient(invoker);

        await LoadCatalogAsync(invoker, ct).ConfigureAwait(false);
        await LoadSignalHistoryAsync(invoker, ct).ConfigureAwait(false);

        // One cancellation source for all three streams: whichever fails first takes the
        // others down, and the supervisor restarts the whole set together.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var streams = new List<Task> { StreamQuotesAsync(invoker, linked.Token) };

        if (_signals is not null)
        {
            streams.Add(StreamSignalsAsync(invoker, linked.Token));
        }

        if (_status is not null)
        {
            streams.Add(StreamStatusAsync(invoker, linked.Token));
        }

        await Task.WhenAny(streams).ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);

        // Observe every task so none of them faults unobserved, but let a real failure
        // through: cancellation of the siblings is expected and carries no information.
        foreach (Task stream in streams)
        {
            try
            {
                await stream.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (RpcException rpc) when (rpc.StatusCode == StatusCode.Cancelled)
            {
            }
        }
    }

    private async Task HandshakeAsync(CallInvoker invoker, CancellationToken ct)
    {
        var session = new SessionService.SessionServiceClient(invoker);

        HelloResponse hello = await session.HelloAsync(
            new HelloRequest
            {
                ProtocolMajor = ProtocolVersion.Major,
                ClientVersion = ClientVersion.Current,
                DeviceId = _settings.DeviceId,
                Platform = CurrentPlatform(),
                Locale = _settings.Locale,
            },
            cancellationToken: ct).ConfigureAwait(false);

        if (ProtocolVersion.Major < hello.MinSupportedClientMajor)
        {
            throw new ProtocolVersionMismatchException(
                $"Этой версии RcktMon требуется обновление: сервер поддерживает протокол " +
                $"не ниже v{hello.MinSupportedClientMajor}, клиент реализует v{ProtocolVersion.Major}.");
        }

        Session = hello;
        _logger.LogInformation(
            "Connected to RcktMon {ServerVersion} as {Account} (protocol {Major}.{Minor})",
            hello.ServerVersion, hello.AccountDisplayName, hello.ProtocolMajor, hello.ProtocolMinor);
    }

    private async Task LoadCatalogAsync(CallInvoker invoker, CancellationToken ct)
    {
        var catalog = new CatalogService.CatalogServiceClient(invoker);

        using AsyncServerStreamingCall<CatalogChunk> call =
            catalog.GetCatalog(new GetCatalogRequest { Etag = _catalogEtag }, cancellationToken: ct);

        var batch = new List<Instrument>(capacity: 2048);

        await foreach (CatalogChunk chunk in call.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (chunk.NotModified)
            {
                _logger.LogDebug("Catalog unchanged ({Etag})", _catalogEtag);
                return;
            }

            batch.AddRange(chunk.Instruments);

            if (!string.IsNullOrEmpty(chunk.Etag))
            {
                _catalogEtag = chunk.Etag;
            }
        }

        _store.ApplyCatalog(batch);
        _logger.LogInformation("Catalog loaded: {Count} instruments ({Etag})", batch.Count, _catalogEtag);
    }

    /// <summary>
    /// Backfills the signal feed once, so it does not start empty on a fresh launch.
    /// </summary>
    /// <remarks>
    /// Only on the first successful connection. Reconnects resume from the last applied
    /// id instead, which is both cheaper and gapless.
    /// </remarks>
    private async Task LoadSignalHistoryAsync(CallInvoker invoker, CancellationToken ct)
    {
        if (_signals is null || _historyLoaded || !Can(Capabilities.SignalHistory))
        {
            return;
        }

        var client = new SignalService.SignalServiceClient(invoker);

        ListSignalsResponse response = await client.ListSignalsAsync(
            new ListSignalsRequest
            {
                From = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(
                    DateTimeOffset.UtcNow - HistoryWindow),
                To = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                PageSize = 500,
            },
            cancellationToken: ct).ConfigureAwait(false);

        _signals.EnqueueRange(response.Signals);
        _historyLoaded = true;

        _logger.LogInformation(
            "Signal history loaded: {Count} of {Total}", response.Signals.Count, response.TotalCount);
    }

    private async Task StreamQuotesAsync(CallInvoker invoker, CancellationToken ct)
    {
        var marketData = new MarketDataService.MarketDataServiceClient(invoker);

        var request = new StreamQuotesRequest
        {
            Selector = new InstrumentSelector(),
            TickIntervalMs = _settings.TickIntervalMs,
            ResumeFromSeq = _store.LastSeq,
            Fields = new QuoteFields
            {
                Orderbook = _settings.WantOrderBook,
                MonthStats = _settings.WantMonthStats,
                Sparkline = _settings.WantSparkline,
            },
        };

        using AsyncServerStreamingCall<QuotesMessage> call =
            marketData.StreamQuotes(request, cancellationToken: ct);

        _logger.LogInformation("Quote stream open (resume from seq {Seq})", request.ResumeFromSeq);

        var lastMessageAt = DateTimeOffset.UtcNow;

        await foreach (QuotesMessage message in call.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
        {
            // Hand off immediately; the UI thread drains and applies on its own timer.
            _store.Enqueue(message);

            lastMessageAt = DateTimeOffset.UtcNow;

            if (message.PayloadCase == QuotesMessage.PayloadOneofCase.Control)
            {
                EvaluateHealth(message.Control, lastMessageAt);
            }
            else
            {
                Publish(ConnectionState.Connected);
            }
        }
    }

    private async Task StreamSignalsAsync(CallInvoker invoker, CancellationToken ct)
    {
        var client = new SignalService.SignalServiceClient(invoker);

        using AsyncServerStreamingCall<Signal> call = client.StreamSignals(
            new StreamSignalsRequest { SinceId = _signals!.LastId },
            cancellationToken: ct);

        _logger.LogInformation("Signal stream open (since id {Id})", _signals.LastId);

        await foreach (Signal signal in call.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
        {
            _signals.Enqueue(signal);
        }
    }

    private async Task StreamStatusAsync(CallInvoker invoker, CancellationToken ct)
    {
        var client = new StatusService.StatusServiceClient(invoker);

        using AsyncServerStreamingCall<ServerStatus> call =
            client.StreamStatus(new StreamStatusRequest(), cancellationToken: ct);

        await foreach (ServerStatus status in call.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
        {
            _status!.Enqueue(status);
        }
    }

    private void EvaluateHealth(StreamControl control, DateTimeOffset lastMessageAt)
    {
        // The heartbeat arrives every few seconds even in a quiet market, so a gap here
        // means the socket is wedged rather than that nothing is trading.
        bool stale = DateTimeOffset.UtcNow - lastMessageAt > StaleAfter;
        bool dropping = control.DroppedTicks > _lastDroppedTicks;

        _lastDroppedTicks = control.DroppedTicks;

        if (dropping)
        {
            Publish(
                ConnectionState.Degraded,
                $"Сервер пропускает обновления для этого клиента ({control.DroppedTicks})");
        }
        else if (stale)
        {
            Publish(ConnectionState.Degraded, "Котировки не обновлялись более 10 секунд");
        }
        else
        {
            Publish(ConnectionState.Connected);
        }
    }

    private GrpcChannel EnsureChannel()
    {
        if (_channel is not null)
        {
            return _channel;
        }

        if (!_settings.AllowInsecureHttp && _settings.ServerUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"Отказ от незашифрованного подключения к {_settings.ServerUri}. " +
                "Используйте https или явно включите AllowInsecureHttp для локальной разработки.");
        }

        _channel = GrpcChannel.ForAddress(_settings.ServerUri, new GrpcChannelOptions
        {
            // The initial catalog snapshot is the largest single message; 2000 instruments
            // is comfortably under this, and the server chunks anyway.
            MaxReceiveMessageSize = 16 * 1024 * 1024,
            HttpHandler = new SocketsHttpHandler
            {
                // Keep-alive pings surface a dead connection in seconds rather than
                // waiting for a TCP timeout that can run into minutes.
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
                EnableMultipleHttp2Connections = true,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            },
        });

        return _channel;
    }

    private static TimeSpan Backoff(int attempt)
    {
        // Exponential with full jitter. Jitter matters even for a single client: without
        // it, every client that dropped during the same server restart comes back in
        // lockstep and knocks it over again.
        double exponential = Math.Min(
            MaxBackoff.TotalMilliseconds,
            MinBackoff.TotalMilliseconds * Math.Pow(2, Math.Min(attempt, 10)));

        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * exponential);
    }

    private static string CurrentPlatform() =>
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos"
        : OperatingSystem.IsLinux() ? "linux"
        : "unknown";

    private void Publish(ConnectionState state, string? detail = null, TimeSpan? retryIn = null)
    {
        var next = new ConnectionStatus(state, detail, retryIn);

        // Avoid waking the UI on every heartbeat when nothing actually changed.
        if (_state.Value != next)
        {
            _state.OnNext(next);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _state.OnCompleted();
        _state.Dispose();

        if (_channel is not null)
        {
            await _channel.ShutdownAsync().ConfigureAwait(false);
            _channel.Dispose();
        }
    }

    /// <summary>Adds the access key and device id to every outgoing call.</summary>
    private sealed class AuthInterceptor(ConnectionSettings settings) : Interceptor
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
            => continuation(request, Authorize(context));

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
            => continuation(request, Authorize(context));

        private ClientInterceptorContext<TRequest, TResponse> Authorize<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context)
            where TRequest : class
            where TResponse : class
        {
            var headers = context.Options.Headers ?? [];
            headers.Add("authorization", $"Bearer {settings.AccessKey}");
            headers.Add("x-rcktmon-device", settings.DeviceId);

            return new ClientInterceptorContext<TRequest, TResponse>(
                context.Method,
                context.Host,
                context.Options.WithHeaders(headers));
        }
    }
}

/// <summary>Thrown when the server requires a newer client than this one.</summary>
public sealed class ProtocolVersionMismatchException(string message) : Exception(message);

/// <summary>The client's own version string, taken from the assembly.</summary>
public static class ClientVersion
{
    /// <summary>
    /// Informational version, stamped by MinVer from the git tag.
    /// </summary>
    /// <remarks>
    /// Read from assembly metadata rather than from <c>Process.MainModule.FileVersionInfo</c>,
    /// which the pre-rewrite updater used and which is unreliable outside Windows.
    /// </remarks>
    public static string Current { get; } =
        typeof(ClientVersion).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion
        ?? typeof(ClientVersion).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
