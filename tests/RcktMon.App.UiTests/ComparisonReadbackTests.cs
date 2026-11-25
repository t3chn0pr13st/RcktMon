using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RcktMon.App.Controls;
using RcktMon.App.Localization;
using RcktMon.App.ViewModels;
using RcktMon.App.Views;
using Xunit;

namespace RcktMon.App.UiTests;

/// <summary>Opt-in release acceptance of the persisted 89-day local result, read-only. No API writes or credentials.</summary>
public sealed class ComparisonReadbackTests(HeadlessSessionFixture fixture) : IClassFixture<HeadlessSessionFixture>
{
    public static bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RCKTMON_COMPARISON_89_READBACK"));

    [Theory(Skip = "Requires the local comparison acceptance run.", SkipUnless = nameof(Enabled))]
    [InlineData(1440, 950)]
    [InlineData(390, 844)]
    public void Persisted_comparison_is_reachable_in_full_shell(int width, int height)
    {
        var id = Environment.GetEnvironmentVariable("RCKTMON_COMPARISON_89_READBACK")!;
        var dir = Environment.GetEnvironmentVariable("RCKTMON_UI_ARTIFACTS")!;
        Assert.True(Guid.TryParseExact(id, "N", out _));
        Directory.CreateDirectory(dir);
        using var paths = new TempPaths();
        fixture.Run(() =>
        {
            using var model = new MainWindowViewModel(paths, secretStore: new UnifiedShellTests.FixtureSecrets());
            Localizer.Instance.Use("ru");
            var win = new MainWindow(UnifiedShellTests.CreateStudio) { DataContext = model, Width = width, Height = height };
            try
            {
                win.Show();
                var studio = win.GetVisualDescendants().OfType<StudioView>().First(x => x.IsEffectivelyVisible);
                studio.FindControl<TabControl>("ModeTabs")!.SelectedIndex = 2;
                Wait(() => studio.GetVisualDescendants().OfType<ComparisonPane>().Any(x => x.IsEffectivelyVisible));
                var pane = studio.GetVisualDescendants().OfType<ComparisonPane>().Single(x => x.IsEffectivelyVisible);
                T Get<T>(string n) where T : Control => pane.FindControl<T>(n)!;
                var jobs = Get<ComboBox>("CompareRuns");
                var rows = Get<ListBox>("VariantRows");
                Wait(() => jobs.Items.Count > 0);
                var job = jobs.Items.Cast<object>().Single(x => x.ToString()!.Contains(id[..6], StringComparison.Ordinal));
                jobs.SelectedItem = job;
                Wait(() => rows.Items.Count == 8);
                var proxy = rows.Items.Cast<VariantDisplay>().First(x => x.Row is {Rule:"original",ExitRed:1,CooldownHours:0});
                Assert.Equal(-3511.27m, decimal.Round(proxy.Row.ProxyNet!.Value, 2));
                var book = Get<ComboBox>("ExecutionModel"); book.SelectedIndex = 1;
                var full = rows.Items.Cast<VariantDisplay>().First(x => x.Row is {Rule:"full",ExitRed:3,CooldownHours:0});
                Assert.Equal(260.23m, decimal.Round(full.Row.BookNet!.Value, 2));
                var start = Get<Button>("CompareStart");
                Assert.True(start.Focus(NavigationMethod.Tab));
                var scroll = Get<ScrollViewer>("ComparisonScroll");
                scroll.ScrollToHome();
                Frame("overview");

                var table = Get<Border>("ResultCard");
                void Align(Control control)
                {
                    Dispatcher.UIThread.RunJobs();
                    var top = control.TranslatePoint(new Point(0,0), scroll)!.Value.Y;
                    scroll.Offset = new Vector(0, Math.Max(0,scroll.Offset.Y+top-4));
                    Dispatcher.UIThread.RunJobs();
                }
                var best = rows.Items.Cast<VariantDisplay>().First(x => x.Row is {Rule:"full",ExitRed:3,CooldownHours:24});
                rows.SelectedItem = best;
                Assert.Equal(305.33m, decimal.Round(best.Row.BookNet!.Value, 2));
                var choices = Get<ComboBox>("TradePicker");
                Get<ComboBox>("TickerFilter").SelectedItem = "NVDA";
                choices.SelectedItem = choices.Items.Cast<object>().Single(x => x.ToString()!.Contains("21:55:44.286", StringComparison.Ordinal));
                Wait(() => Get<HistoryTradeChart>("TradeChart").IsEffectivelyVisible);
                Align(table); Frame("profit");
                var original = rows.Items.Cast<VariantDisplay>().First(x => x.Row is {Rule:"original",ExitRed:1,CooldownHours:0});
                rows.SelectedItem = original;
                var ticker = Get<ComboBox>("TickerFilter"); ticker.SelectedItem = "NVDA";
                var trades = Get<ComboBox>("TradePicker");
                trades.SelectedItem = trades.Items.Cast<object>().Single(x => x.ToString()!.Contains("21:55:42.656", StringComparison.Ordinal));
                var chart = Get<HistoryTradeChart>("TradeChart");
                Wait(() => chart.IsEffectivelyVisible && Get<TextBlock>("TradeFacts").Text!.Contains("21:55:42.656"));
                Align(Get<Border>("DetailCard")); Frame("nvda-nov23");
                Assert.False(model.MonitorStarted);
                void Frame(string state)
                {
                    Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var pixels = win.CaptureRenderedFrame();
                    Assert.NotNull(pixels);
                    pixels.Save(Path.Combine(dir, $"shell-{state}-{width}.png"), new PngBitmapEncoderOptions());
                }
            }
            finally { win.Close(); }
        });
    }

    private static void Wait(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(25);
        while (!condition() && DateTime.UtcNow < until)
        { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
        Assert.True(condition());
    }
}
