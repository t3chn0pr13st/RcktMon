using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace RcktMon.Client.Core.Studio;

/// <summary>Optional bundled, read-only data service. Never takes ownership of a pre-existing listener.</summary>
public static class StudioCompanion
{
    private static readonly Uri Health = new("http://127.0.0.1:47831/api/health");

    // This task runs independently of the UI. Existing/foreign services and installation errors
    // simply leave the data page responsible for its normal disconnected/retry presentation.
    public static async Task RunAsync(string executable, string? recordingsDirectory, CancellationToken shutdown)
    {
        if (shutdown.IsCancellationRequested) return;
        using var http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        }) { Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 4096 };
        try
        {
            await RunCoreAsync(executable, recordingsDirectory, ct => ProbeAsync(http, ct),
                File.Exists, StartOwnedChild, shutdown).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or Win32Exception or UnauthorizedAccessException or ArgumentException)
        {
            // A missing/non-executable local install must not prevent the main application opening.
        }
    }

    internal enum Endpoint { Healthy, OccupiedOrUnknown, Refused }

    internal static async Task<Endpoint> ProbeAsync(HttpClient http, CancellationToken token)
    {
        try
        {
            using var response = await http.GetAsync(Health, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return Endpoint.OccupiedOrUnknown;
            byte[] body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            if (body.Length > 4096) return Endpoint.OccupiedOrUnknown;
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("application", out var name) && name.ValueKind == JsonValueKind.String &&
                name.GetString() == "RocketMon Signal Studio" &&
                root.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String &&
                mode.GetString() == "local-read-only"
                ? Endpoint.Healthy : Endpoint.OccupiedOrUnknown;
        }
        catch (HttpRequestException e) when (
            e.HttpRequestError == HttpRequestError.ConnectionError &&
            e.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
        {
            return Endpoint.Refused;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Endpoint.OccupiedOrUnknown; }
        catch (Exception e) when (e is HttpRequestException or JsonException or IOException)
        {
            return Endpoint.OccupiedOrUnknown;
        }
    }

    internal interface IOwnedChild : IDisposable
    {
        Task WaitAsync(CancellationToken ct);
        void Stop();
    }

    internal static async Task RunCoreAsync(string executable, string? recordingsDirectory,
        Func<CancellationToken, Task<Endpoint>> probe, Func<string, bool> exists,
        Func<ProcessStartInfo, IOwnedChild> start, CancellationToken token)
    {
        if (token.IsCancellationRequested || await probe(token).ConfigureAwait(false) != Endpoint.Refused) return;
        if (!Path.IsPathFullyQualified(executable) || !exists(executable) ||
            recordingsDirectory is not null && !Path.IsPathFullyQualified(recordingsDirectory)) return;
        var info = new ProcessStartInfo
        {
            FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false, CreateNoWindow = true,
        };
        // Do not copy parent OAuth, broker, proxy, LD_/DYLD_, runtime overrides or other application secrets.
        // The shipped apphost is self-contained; no shell, interpreter, PATH lookup or SDK is involved.
        info.Environment.Clear();
        info.Environment["RCKTMON_STUDIO_PORT"] = "47831";
        if (recordingsDirectory is not null) info.Environment["RCKTMON_STUDIO_RECORDINGS"] = recordingsDirectory;
        token.ThrowIfCancellationRequested();
        using var owned = start(info);
        // Stop only the exact Process object returned by Start. No kill-by-name/port, launchctl removal,
        // automatic restart or takeover if another instance wins the bind race.
        void OnExit(object? sender, EventArgs args) => owned.Stop();
        AppDomain.CurrentDomain.ProcessExit += OnExit;
        try
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            var exit = owned.WaitAsync(lifetime.Token);
            try
            {
                using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
                startup.CancelAfter(TimeSpan.FromSeconds(12));
                while (!exit.IsCompleted)
                {
                    var state = await probe(startup.Token).ConfigureAwait(false);
                    if (state == Endpoint.Healthy) { await exit.ConfigureAwait(false); return; }
                    if (state != Endpoint.Refused) return;
                    var delay = Task.Delay(TimeSpan.FromMilliseconds(250), startup.Token);
                    if (await Task.WhenAny(exit, delay).ConfigureAwait(false) == delay)
                        await delay.ConfigureAwait(false);
                }
                await exit.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { /* readiness timed out */ }
            finally { lifetime.Cancel(); }
        }
        finally
        {
            AppDomain.CurrentDomain.ProcessExit -= OnExit;
            owned.Stop();
        }
    }

    internal static IOwnedChild StartOwnedChild(ProcessStartInfo info) => new Child(info);

    private sealed class Child(ProcessStartInfo info) : IOwnedChild
    {
        private readonly Process _process = Process.Start(info) ?? throw new IOException("Unable to start the bundled Studio service.");
        private readonly object _gate = new();
        private bool _closed;
        public Task WaitAsync(CancellationToken ct) => _process.WaitForExitAsync(ct);
        public void Stop()
        {
            lock (_gate)
            {
                if (_closed) return;
                try
                {
                    if (!_process.HasExited) { _process.Kill(entireProcessTree: false); _process.WaitForExit(2000); }
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
            }
        }
        public void Dispose() { lock (_gate) { _closed = true; _process.Dispose(); } }
    }
}
