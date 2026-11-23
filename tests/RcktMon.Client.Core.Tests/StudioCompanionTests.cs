using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using RcktMon.Client.Core.Studio;
using Xunit;
using Endpoint = RcktMon.Client.Core.Studio.StudioCompanion.Endpoint;

namespace RcktMon.Client.Core.Tests;

public sealed class StudioCompanionTests
{
    [Theory]
    [InlineData(200, "{\"application\":\"RocketMon Signal Studio\",\"mode\":\"local-read-only\"}", true)]
    [InlineData(200, "{\"application\":\"RocketMon Signal Studio\",\"mode\":\"live-trading\"}", false)]
    [InlineData(200, "{\"application\":\"Other\",\"mode\":\"local-read-only\"}", false)]
    [InlineData(200, "invalid", false)]
    [InlineData(200, "[]", false)]
    [InlineData(302, "", false)]
    [InlineData(401, "unauthorized", false)]
    public async Task Probe_checks_both_identity_and_mode(int status, string body, bool healthy)
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body)
        }));
        Assert.Equal(healthy ? Endpoint.Healthy : Endpoint.OccupiedOrUnknown,
            await StudioCompanion.ProbeAsync(client, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused, true)]
    [InlineData(SocketError.ConnectionReset, false)]
    [InlineData(SocketError.TimedOut, false)]
    public async Task Only_explicit_refusal_may_start_a_new_process(SocketError error, bool refused)
    {
        using var client = new HttpClient(new Handler(_ => throw new HttpRequestException(HttpRequestError.ConnectionError,
            "fixture", new SocketException((int)error))));
        Assert.Equal(refused ? Endpoint.Refused : Endpoint.OccupiedOrUnknown,
            await StudioCompanion.ProbeAsync(client, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Does_not_touch_existing_or_ambiguous_process(bool ours)
    {
        await StudioCompanion.RunCoreAsync("/invalid/fixture", null,
            _ => Task.FromResult(ours ? Endpoint.Healthy : Endpoint.OccupiedOrUnknown), _ => throw new Exception(),
            _ => throw new Exception(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Lifecycle_uses_only_the_owned_child_and_has_no_ambient_environment()
    {
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var child = new MockChild();
        string abs = Path.GetFullPath("fixture app/readonly-studio");
        string data = Path.GetFullPath("recordings with spaces");
        var samples = new Queue<Endpoint>([Endpoint.Refused, Endpoint.Healthy]);
        var task = StudioCompanion.RunCoreAsync(abs, data, _ => Task.FromResult(samples.Dequeue()), p => p == abs, info =>
        {
            Assert.Equal(abs, info.FileName);
            Assert.False(info.UseShellExecute);
            Assert.Empty(info.ArgumentList);
            Assert.Equal(Path.GetDirectoryName(abs), info.WorkingDirectory);
            Assert.Equal("47831", info.Environment["RCKTMON_STUDIO_PORT"]);
            Assert.Equal(data, info.Environment["RCKTMON_STUDIO_RECORDINGS"]);
            Assert.Equal(2, info.Environment.Count);
            return child;
        }, shutdown.Token);
        Assert.False(task.IsCompleted);
        Assert.Empty(samples);
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, child.Stops);
        Assert.True(child.Disposed);
    }

    [Fact]
    public async Task Losing_a_bind_race_stops_only_the_child_we_started()
    {
        var child = new MockChild();
        var samples = new Queue<Endpoint>([Endpoint.Refused, Endpoint.OccupiedOrUnknown]);
        await StudioCompanion.RunCoreAsync(Path.GetFullPath("test-apphost"), null,
            _ => Task.FromResult(samples.Dequeue()), _ => true, _ => child, TestContext.Current.CancellationToken);
        Assert.Empty(samples);
        Assert.Equal(1, child.Stops);
        Assert.True(child.Disposed);
    }

    [Fact]
    public async Task Cancellation_reaps_a_real_offline_child_with_an_apphost_path_containing_spaces()
    {
        if (OperatingSystem.IsWindows()) return;
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        var temp = Directory.CreateTempSubdirectory("rocketmon-lifecycle-");
        try
        {
            string exe = Path.Combine(temp.FullName, "inert apphost");
            // Just a sleeping fixture: no sockets, service, broker, repository binary or external APIs.
            await File.WriteAllTextAsync(exe, "#!/bin/sh\nprintf '%s' \"$$\" > \"$0.pid\"\nexec /bin/sleep 30\n", deadline.Token);
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var samples = new Queue<Endpoint>([Endpoint.Refused, Endpoint.Healthy]);
            var running = StudioCompanion.RunCoreAsync(exe, null, _ => Task.FromResult(samples.Dequeue()),
                File.Exists, StudioCompanion.StartOwnedChild, shutdown.Token);
            try
            {
                int pid = 0;
                while (!File.Exists(exe + ".pid") || !int.TryParse(await File.ReadAllTextAsync(exe + ".pid", deadline.Token), out pid))
                    await Task.Delay(20, deadline.Token);
                using var spawned = Process.GetProcessById(pid);
                Assert.False(spawned.HasExited);
                shutdown.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
                await spawned.WaitForExitAsync(deadline.Token);
                Assert.True(spawned.HasExited);
            }
            finally { shutdown.Cancel(); try { await running; } catch (OperationCanceledException) { } }
        }
        finally { temp.Delete(recursive: true); }
    }

    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg, CancellationToken token)
        {
            Assert.Equal("http://127.0.0.1:47831/api/health", msg.RequestUri?.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, msg.Method);
            return Task.FromResult(respond(msg));
        }
    }

    private sealed class MockChild : StudioCompanion.IOwnedChild
    {
        public int Stops;
        public bool Disposed;
        public Task WaitAsync(CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);
        public void Stop() => ++Stops;
        public void Dispose() => Disposed = true;
    }
}
