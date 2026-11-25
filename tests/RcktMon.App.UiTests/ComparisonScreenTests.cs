using System.Net;
using System.Net.Http.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RcktMon.App.Controls;
using RcktMon.App.Localization;
using RcktMon.App.Views;
using RcktMon.Client.Core.Studio;
using Xunit;

namespace RcktMon.App.UiTests;

public sealed class ComparisonScreenTests(HeadlessSessionFixture fixture) : IClassFixture<HeadlessSessionFixture>
{
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 780)]
    public void Renders_saved_eight_variants_and_exact_trade_in_both_models(int width, int height)
    {
        fixture.Run(() =>
        {
            Localizer.Instance.Use("ru");
            var feed = new Feed();
            var pane = new ComparisonPane(() => new StudioClient(feed));
            var win = new Window { Width = width, Height = height, Content = pane };
            try
            {
                win.Show();
                var rows = pane.FindControl<ListBox>("VariantRows")!;
                var chart = pane.FindControl<HistoryTradeChart>("TradeChart")!;
                Pump(() => rows.Items.Count == 8 && chart.IsVisible);
                Assert.Equal(0, feed.Writes);
                var scroll = pane.FindControl<ScrollViewer>("ComparisonScroll")!;
                Assert.InRange(scroll.Extent.Width - scroll.Viewport.Width, -4, 1);
                Assert.Equal("2026-08-28", pane.FindControl<TextBox>("CompareFrom")!.Text);
                Assert.True(pane.FindControl<Button>("CompareStart")!.Focus());
                Frame(win, "overview", width);
                var originalDay = (VariantDisplay)rows.Items[1]!;
                Assert.EndsWith("*", originalDay.Delta);
                var model = pane.FindControl<ComboBox>("ExecutionModel")!;
                model.SelectedIndex = 1;
                rows.SelectedIndex = 5;
                Pump(() => chart.IsVisible && pane.FindControl<TextBlock>("TradeFacts")!.Text!.Contains("160"));
                Assert.Equal("NVDA", pane.FindControl<ComboBox>("TickerFilter")!.SelectedItem);
                var fullDay = (VariantDisplay)rows.SelectedItem!;
                Assert.True(fullDay.Delta.StartsWith('+') && !fullDay.Delta.Contains('*'));
                rows.Focus(); rows.BringIntoView();
                Frame(win, "variants", width);
                scroll.ScrollToEnd();
                Frame(win, "trade", width);
                Assert.Equal(0, feed.Writes);
                pane.FindControl<TextBox>("CompareThrough")!.Text = "not-a-date";
                Click(pane.FindControl<Button>("CompareStart")!);
                Assert.True(pane.FindControl<TextBlock>("ActionMessage")!.IsVisible);
                Assert.Equal(0, feed.Writes);
            }
            finally { win.Close(); }
        });
    }

    [Fact]
    public void Explicit_update_and_cancel_are_only_writes_and_previous_result_remains_selectable()
    {
        fixture.Run(() =>
        {
            Localizer.Instance.Use("ru");
            var feed = new Feed();
            var pane = new ComparisonPane(() => new StudioClient(feed));
            var win = new Window { Width = 970, Height = 760, Content = pane };
            try
            {
                win.Show();
                var start = pane.FindControl<Button>("CompareStart")!;
                var cancel = pane.FindControl<Button>("CompareCancel")!;
                var runs = pane.FindControl<ComboBox>("CompareRuns")!;
                Pump(() => start.IsEnabled);
                Click(start);
                Pump(() => cancel.IsVisible);
                Assert.False(start.IsEnabled);
                Assert.Equal(1, feed.Writes);
                Click(cancel);
                Pump(() => feed.Writes == 2 && !cancel.IsVisible);
                runs.SelectedIndex = 1;
                Pump(() => pane.FindControl<Border>("ResultCard")!.IsVisible);
            }
            finally { win.Close(); }
        });
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("offline")]
    public void Shows_empty_or_offline_status_and_never_assumes_gains(string mode)
    {
        fixture.Run(() =>
        {
            Localizer.Instance.Use("ru");
            var feed = new Feed { Mode = mode };
            var pane = new ComparisonPane(() => new StudioClient(feed));
            var win = new Window { Width = 390, Height = 780, Content = pane };
            try
            {
                win.Show();
                var status = pane.FindControl<TextBlock>("RunStatus")!;
                Pump(() => status.Text == Localizer.Instance[mode == "empty" ? "Compare_Empty" : "History_Unavailable"]);
                Assert.False(pane.FindControl<Border>("ResultCard")!.IsVisible);
                Frame(win, mode, 390); Assert.Equal(0, feed.Writes);
            }
            finally { win.Close(); }
        });
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Pump(Func<bool> until)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!until() && DateTime.UtcNow < limit) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(12); }
        Dispatcher.UIThread.RunJobs(); Assert.True(until());
    }
    private static void Frame(Window win, string state, int width)
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var img = win.CaptureRenderedFrame(); Assert.NotNull(img);
        if (Environment.GetEnvironmentVariable("RCKTMON_UI_ARTIFACTS") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        img.Save(Path.Combine(dir, $"comparison-{state}-{width}.png"), new PngBitmapEncoderOptions());
    }
    private sealed class Feed : HttpMessageHandler
    {
        public int Writes { get; private set; }
        public string Mode { get; init; } = "saved";
        private string? _next;
        private const string Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", NewId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string First = "2026-08-28", Last = "2026-11-24";
        private static readonly long Signal = new DateTimeOffset(2026, 11, 23, 13, 55, 42, 656, TimeSpan.Zero).ToUnixTimeMilliseconds();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post)
            {
                Assert.Equal("1", Assert.Single(req.Headers.GetValues("X-RocketMon-History"))); Writes++;
                if (path == "/api/history/comparisons") { Assert.Contains(First, await req.Content!.ReadAsStringAsync(ct)); _next = "running"; return Reply(new {id = NewId}); }
                Assert.Equal($"/api/history/comparisons/{NewId}/cancel", path); _next = "cancelled"; return Reply(new { });
            }
            Assert.Equal(HttpMethod.Get, req.Method);
            if (Mode == "offline") return new(HttpStatusCode.ServiceUnavailable);
            switch (path)
            {
                case "/api/history/catalog": return Reply(new ArchiveCatalog("2026-08-24", Last, [new("NVDA", DateTimeOffset.UnixEpoch)]));
                case "/api/history/comparisons":
                    var jobs = new List<ComparisonRun>();
                    if (_next != null) jobs.Add(new(NewId, First, Last, _next, "Тест", 1, 5));
                    if (Mode != "empty") jobs.Add(new(Id, First, Last, "complete", "", 1, 1));
                    return Reply(jobs);
                case "/api/history/comparisons/" + Id + "/result":
                    List<ComparisonRow> rows = []; List<ComparisonTrade> trades = [];
                    foreach (var rule in new[] { "original", "full" })
                    foreach (var exit in new[] { 1, 3 })
                    foreach (var pause in new[] { 0, 24 })
                    {
                        int entries = pause > 0 ? 6 : 23, priced = rule == "original" ? entries - 2 : entries;
                        rows.Add(new(rule, exit, pause, entries, entries, priced, 4, 71.23m + pause, priced, 4, 98.4m + pause));
                        trades.Add(new(rule, exit, pause, "NVDA", Signal, Signal + 254, Signal + 340_000, 138.4m, 159.49m,
                            150.61m, 159.45m, 139.4m, 160.01m, Signal + 340_340));
                    }
                    return Reply(new ComparisonResult(First, Last, rows.ToArray(), trades.ToArray()));
                case "/api/history/comparisons/" + Id + "/candles":
                    Assert.Contains("ticker=NVDA", req.RequestUri.Query);
                    long minute = Signal - Signal % 60000;
                    return Reply(Enumerable.Range(-3, 14).Select(i => new HistoryCandle(minute + i * 60000,
                        130 + i * 2, 136 + i * 2, 128 + i * 2, 130 + i * 2 + (i % 3 == 1 ? -1 : 3))).ToArray());
                default: throw new InvalidOperationException("Unexpected route: " + path);
            }
        }
        private static HttpResponseMessage Reply<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
