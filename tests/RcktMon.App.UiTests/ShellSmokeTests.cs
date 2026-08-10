using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RcktMon.App.ViewModels;
using RcktMon.App.Views;
using RcktMon.Client.Core.Connection;
using RcktMon.Client.Core.Settings;
using Xunit;

namespace RcktMon.App.UiTests;

/// <summary>
/// The Phase 3 gate, made executable: the whole shell running against the fake server,
/// rendered for real, with a frame captured per page.
/// </summary>
/// <remarks>
/// Every page is visited because the failure this catches is a page that resolves, lays
/// out and draws nothing — a broken template or a missing resource key produces exactly
/// that, and it is invisible to a view-model test.
/// </remarks>
public sealed class ShellSmokeTests(HeadlessSessionFixture fixture) : IClassFixture<HeadlessSessionFixture>
{
    /// <summary>Where captured frames are written for a human to look at.</summary>
    private static string ArtifactDirectory =>
        Environment.GetEnvironmentVariable("RCKTMON_UI_ARTIFACTS")
        ?? Path.Combine(Path.GetTempPath(), "rcktmon-ui");

    [Fact]
    public void Every_page_renders_against_the_fake_server()
    {
        using FakeServerHost server = FakeServerHost.Start();
        using var paths = new TempPaths();

        WriteSettings(paths, server.Address);

        // Everything inside runs on the session's UI thread, synchronously. Awaiting
        // inside the dispatched action would deadlock: the continuation needs the very
        // dispatcher loop the action is occupying.
        fixture.Run(
            () =>
            {
                RcktMonApp.Paths = paths;

                var model = new MainWindowViewModel(paths, TestLogging.Factory);
                var window = new MainWindow { DataContext = model };

                window.Show();

                MonitorViewModel monitor = model.Pages.OfType<MonitorViewModel>().First();

                bool loaded = Pump(
                    model,
                    () => monitor.TotalCount > 0 && monitor.Rows.Count > 0,
                    TimeSpan.FromSeconds(40));

                Assert.True(loaded, "the monitor never received any instruments");
                Assert.NotEmpty(monitor.CurrencyChips);
                Assert.True(monitor.Rows[0].Price > 0m, "rows should carry a price");

                // Signals: the seeded backlog arrives over ListSignals on connect.
                SignalsViewModel signals = model.Pages.OfType<SignalsViewModel>().First();

                Assert.True(
                    Pump(model, () => signals.MatchCount > 0, TimeSpan.FromSeconds(20)),
                    $"the signal feed never received the seeded history " +
                    $"(state {model.Status.State}, detail '{model.Status.Detail}', " +
                    $"rows {monitor.TotalCount}, today {signals.TodayCount}, " +
                    $"badge {signals.Badge}, items {signals.Items.Count})");

                Directory.CreateDirectory(ArtifactDirectory);

                foreach (PageViewModel page in model.Pages)
                {
                    model.CurrentPage = page;

                    Pump(model, static () => false, TimeSpan.FromSeconds(1));
                    Capture(window, page.Key);
                }

                // The inspector is a panel inside the monitor rather than a page, so it
                // gets its own pass.
                model.CurrentPage = monitor;
                monitor.SelectedRow = monitor.Rows[0];

                Pump(model, () => monitor.Inspector.IsOpen, TimeSpan.FromSeconds(5));
                Pump(model, static () => false, TimeSpan.FromSeconds(2));

                Assert.True(monitor.Inspector.IsOpen);
                Capture(window, "inspector");

                // The interface language switches in place, with no restart: every
                // translated string in the window is bound to the localizer.
                Assert.Equal("Monitor", monitor.Title);

                // Switching language re-establishes the session, because the server
                // renders alert text in the session's language. The pages are rebuilt, so
                // everything below re-reads them.
                model.ApplyLanguage("ru");

                Assert.True(
                    Pump(
                        model,
                        () => model.Pages.OfType<MonitorViewModel>().FirstOrDefault() is { Rows.Count: > 0 },
                        TimeSpan.FromSeconds(30)),
                    "the session never came back after the language change");

                monitor = model.Pages.OfType<MonitorViewModel>().First();
                Pump(model, static () => false, TimeSpan.FromSeconds(1));

                Assert.Equal("Монитор", monitor.Title);
                Capture(window, "monitor-ru");

                // Navigating re-applies the page template, so this view is built after the
                // switch. It is the case that a naive implementation gets wrong: the first
                // instance follows the language and later ones are stuck in the old one.
                model.CurrentPage = model.Pages.First(page => page.Key == "settings");
                Pump(model, static () => false, TimeSpan.FromSeconds(1));
                Capture(window, "settings-ru");

                Assert.Contains(
                    window.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Подключение");

                window.Close();
                model.Dispose();
            });
    }

    /// <summary>
    /// The shell survives the stream being killed under it and comes back on its own.
    /// </summary>
    /// <remarks>
    /// Chaos mode drops the quote stream every few seconds. This is the whole reason the
    /// fake server has that mode: reconnect behaviour built against imagined failure
    /// breaks the first time a laptop lid closes.
    /// </remarks>
    [Fact]
    public void Shell_recovers_after_the_stream_is_dropped()
    {
        using FakeServerHost server = FakeServerHost.Start(instruments: 60, chaos: true);
        using var paths = new TempPaths();

        WriteSettings(paths, server.Address);

        fixture.Run(
            () =>
            {
                RcktMonApp.Paths = paths;

                var model = new MainWindowViewModel(paths);
                var window = new MainWindow { DataContext = model };
                window.Show();

                MonitorViewModel monitor = model.Pages.OfType<MonitorViewModel>().First();

                Assert.True(
                    Pump(model, () => monitor.Rows.Count > 0, TimeSpan.FromSeconds(30)),
                    "the monitor never received any instruments");

                // Chaos kills the stream on its own timer; the supervisor should notice.
                Assert.True(
                    Pump(
                        model,
                        () => model.Status.State is ConnectionState.Reconnecting,
                        TimeSpan.FromSeconds(30)),
                    "the dropped stream never surfaced as a reconnect");

                Assert.True(
                    Pump(
                        model,
                        () => model.Status.State is ConnectionState.Connected or ConnectionState.Degraded,
                        TimeSpan.FromSeconds(40)),
                    "the client never recovered after the drop");

                // And the grid still has its rows: a reconnect must not empty the screen.
                Assert.NotEmpty(monitor.Rows);

                window.Close();
                model.Dispose();
            });
    }

    private static void Capture(Window window, string name)
    {
        using Bitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException($"no frame captured for {name}");

        frame.Save(Path.Combine(ArtifactDirectory, $"{name}.png"), new PngBitmapEncoderOptions());
    }

    /// <summary>
    /// Pumps the dispatcher and the render loop until a condition holds or time runs out.
    /// </summary>
    /// <remarks>
    /// Both are needed: the dispatcher runs the drain timer and the bindings, and the
    /// render timer is what makes the compositor produce a frame to capture.
    /// </remarks>
    private static bool Pump(MainWindowViewModel model, Func<bool> until, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (until())
            {
                return true;
            }

            Thread.Sleep(20);

            // The shell's own drain is driven directly rather than through its dispatcher
            // timer: a headless dispatcher that is being pumped by hand does not raise
            // timers on a wall clock, and the point of the test is the drain, not the
            // timer that schedules it.
            model.Drain();

            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        return until();
    }

    private static void WriteSettings(TempPaths paths, string address)
    {
        var settings = new ClientSettings
        {
            ServerAddress = address,
            DeviceId = Guid.NewGuid().ToString("D"),
            OnboardingCompleted = true,
            StartPage = "monitor",
            ShowSparklines = true,
            TickIntervalMs = 250,

            // English, so the captured frames show the neutral resource set. The test
            // switches to Russian at the end and captures that too.
            Locale = "en",
        };

        File.WriteAllText(
            Path.Combine(paths.ConfigDirectory, "settings.json"),
            JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            }));
    }
}
