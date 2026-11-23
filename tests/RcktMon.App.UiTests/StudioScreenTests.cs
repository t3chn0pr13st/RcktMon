using System.Net;
using System.Net.Http.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RcktMon.App.Localization;
using RcktMon.App.ViewModels;
using RcktMon.App.Views;
using RcktMon.Client.Core.Studio;
using Xunit;

namespace RcktMon.App.UiTests;

public sealed class StudioScreenTests(HeadlessSessionFixture fixture) : IClassFixture<HeadlessSessionFixture>
{
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 780)]
    public void Native_radar_and_replay_render_at_each_width(int width, int height)
    {
        fixture.Run(() =>
        {
            Localizer.Instance.Use("ru");
            var feed = new FixtureFeed();
            var vm = new StudioViewModel(() => new StudioClient(feed));
            var view = new StudioView(vm);
            var window = new Window { Width = width, Height = height, Content = view };
            try
            {
                window.Show();
                Pump(() => vm.Radar.Length != 0);
                Assert.Equal("NVDA", vm.Radar[0].Ticker);
                Assert.Equal(width < 690, view.Classes.Contains("compact"));
                Frame(window, "live", width);
                view.FindControl<TabControl>("ModeTabs")!.SelectedIndex = 1;
                Pump(() => vm.Result != null);
                Assert.Equal("1 / 1", vm.Counts);
                Frame(window, "replay", width);
                var scroll = view.FindControl<ScrollViewer>("ReplayScroller")!;
                vm.Zoom = true;
                scroll.ScrollToEnd();
                Frame(window, "zoom-entry", width);
                vm.Zoom = false;
                scroll.ScrollToHome();
                var run = view.FindControl<Button>("ReplayRun")!;
                Assert.True(run.Focus());
                Assert.True(run.IsFocused);
                Frame(window, "keyboard", width);
                vm.Slip = 2;
                Assert.Null(vm.Result);
                Assert.Equal("—", vm.NetResult);
                vm.FlowOnly = true;
                _ = vm.CalculateAsync();
                Pump(() => vm.Result != null);
                Assert.Empty(vm.Entries);
                Assert.Equal("—", vm.NetResult);
                Frame(window, "zero", width);
                feed.Fail = true;
                _ = vm.RefreshLibraryAsync();
                Pump(() => vm.ReplayHint == Localizer.Instance["Studio_LibraryError"]);
                Assert.Null(vm.Result);
                Assert.False(vm.CanRun);
                Frame(window, "offline", width);
            }
            finally { window.Close(); }
        });
    }

    private static void Frame(Window window, string state, int width)
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var image = window.CaptureRenderedFrame();
        Assert.NotNull(image);
        if (Environment.GetEnvironmentVariable("RCKTMON_UI_ARTIFACTS") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        image.Save(Path.Combine(dir, $"studio-{state}-{width}.png"), new PngBitmapEncoderOptions());
    }

    private static void Pump(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < end && !condition())
        {
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.True(condition());
    }

    internal sealed class FixtureFeed : HttpMessageHandler
    {
        public bool Fail { get; set; }
        private readonly DateTimeOffset _start = new(2026, 11, 23, 13, 50, 0, TimeSpan.Zero);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var now = DateTimeOffset.UtcNow;
            var cap = new StudioCapture(new string('a', 64), "NVDA", "NVDA-USDT-SWAP", "fixture / SIP",
                _start, _start.AddMinutes(2), true, 442124, 102849);
            long from = _start.ToUnixTimeMilliseconds();
            StudioEvent signal = new(from + 55000, from + 55200, from + 110000, 163m, "closed", 163.1m, 166.2m, .1m, 1.74m);
            var net = !request.RequestUri!.Query.Contains("flowOnly=true", StringComparison.Ordinal);
            object payload = request.RequestUri.AbsolutePath switch
            {
                "/api/live" => new StudioLive("live", now, now, 5, 5, 54123, new[] { "NVDA", "AMD", "AAPL", "TSLA", "INTC" }.Select((ticker, i) =>
                    new StudioInstrument(ticker, ticker + "-USDT-SWAP", "fresh", 165.22m + i, now,
                        127400m - i * 2150m, 23500m, .88m, 150 + i, .52m)).ToArray(),
                    [new("NVDA", now, 165.22m, 127400m, .52m, 5)]),
                "/api/recordings" => new[] { cap },
                _ => new StudioReplay(cap, 1000,
                    Enumerable.Range(0, 120).Select(i => new StudioBar(from + i * 1000, from + i * 1000 + 500,
                        160m + (i > 55 ? 5 : 0) + i * .03m, 160m + (i > 49 ? 5 : 0) + i * .029m)).ToArray(),
                    net ? [signal] : [], new(net ? 1 : 0, net ? 1 : 0, net ? 1 : 0, net ? 1 : null,
                        net ? 1.74m : null, net ? 1.74m : null, net ? 1.74m : null), new(100, 250, 5, 10))
            };
            return Task.FromResult(Fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload, payload.GetType()) });
        }
    }
}
