using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RcktMon.App.Localization;
using RcktMon.App.ViewModels;
using RcktMon.App.Views;
using RcktMon.Client.Core.Connection;
using RcktMon.Client.Core.Security;
using RcktMon.Client.Core.Settings;
using RcktMon.Client.Core.Studio;
using RcktMon.Client.Core.Trading;
using Xunit;

namespace RcktMon.App.UiTests;

public sealed class UnifiedShellTests(HeadlessSessionFixture fixture) : IClassFixture<HeadlessSessionFixture>
{
    // Never consult the developer's Keychain. A setup action, not page navigation, must own writes.
    internal sealed class FixtureSecrets(string? accessKey = null) : ISecretStore
    {
        public string? Read(string key) => key == SecretKeys.AccessKey ? accessKey : null;
        public void Write(string key, string value) => throw new InvalidOperationException("Unexpected secret write.");
        public void Delete(string key) => throw new InvalidOperationException("Unexpected secret deletion.");
    }

    internal static StudioView CreateStudio() =>
        new(new StudioViewModel(() => new StudioClient(new StudioScreenTests.FixtureFeed())));

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1000, 760)]
    [InlineData(390, 780)]
    [InlineData(360, 720)]
    public void Fresh_shell_embeds_observations_and_manual_ticket_in_the_same_window(int width, int height)
    {
        using var paths = new TempPaths();
        fixture.Run(() =>
        {
            using var model = new MainWindowViewModel(paths, secretStore: new FixtureSecrets());
            Localizer.Instance.Use("ru");
            var broker = new TradingScreenTests.Broker();
            var window = new MainWindow(CreateStudio, () => new TradingView(paths, () => new FinamTradingClient(broker, TimeProvider.System)))
                { DataContext = model, Width = width, Height = height };
            try
            {
                window.Show();
                Assert.False(model.IsOnboarding);
                Assert.False(model.MonitorStarted);
                Assert.Equal("studio", model.CurrentPage?.Key);
                Assert.Contains(model.Pages, p => p.Key == "monitor");
                Assert.Contains(model.Pages, p => p.Key == "signals");
                Assert.Contains(model.Pages, p => p.Key == "trading");
                var studio = Active<StudioView>(window);
                Until(() => studio.DataContext is StudioViewModel { Radar.Length: > 0 });
                var nav = Navigation(window);
                // A ListBox delegates keyboard focus to the selected item. The compact picker
                // is focusable itself. Verify the actual focus target, not just the VM selection.
                var focusTarget = nav is ListBox list ? list.ContainerFromIndex(list.SelectedIndex)! : nav;
                Assert.True(focusTarget.Focus(NavigationMethod.Tab));
                Assert.Same(focusTarget, window.FocusManager!.GetFocusedElement());
                Fit(window, nav);
                Fit(window, focusTarget);
                Assert.InRange(window.ClientSize.Width, width - 1.0, width + 1.0);
                Frame(window, "radar", width);
                var tabs = studio.FindControl<TabControl>("ModeTabs")!;
                tabs.SelectedIndex = 1;
                Until(() => studio.DataContext is StudioViewModel { Result: not null });
                Frame(window, "replay", width);

                // Select through the real rail/compact picker rather than directly manipulating the VM.
                if (nav is ListBox)
                {
                    Assert.True(focusTarget.Focus(NavigationMethod.Tab));
                    window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                    window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                    Until(() => model.CurrentPage?.Key == "trading");
                }
                else Select(nav, model, "trading");
                var ticket = Active<TradingView>(window);
                Assert.Empty(window.OwnedWindows);
                Assert.False(ticket.FindControl<Border>("DraftCard")!.IsEffectivelyVisible);
                Assert.False(ticket.FindControl<Button>("ConfirmButton")!.IsEffectivelyEnabled);
                var venue = ticket.FindControl<ComboBox>("Venue")!;
                Fit(window, venue);
                venue.SelectedIndex = 1;
                Assert.Equal(0, broker.Requests);
                Frame(window, "trading", width);

                // A completely local HTTP fixture can safely exercise an in-progress preview. A real
                // provider is unreachable. Navigating away and returning must drop consent, not submit.
                ticket.FindControl<TextBox>("Secret")!.Text = "ui-fixture-only";
                Click(ticket, "ConnectButton");
                Until(() => ticket.FindControl<Border>("DraftCard")!.IsEffectivelyVisible);
                ticket.FindControl<ComboBox>("Account")!.SelectedIndex = 0;
                ticket.FindControl<ComboBox>("Side")!.SelectedIndex = 0;
                ticket.FindControl<TextBox>("Ticker")!.Text = "SBER";
                ticket.FindControl<TextBox>("PriceField")!.Text = "250";
                ticket.FindControl<TextBox>("SizeField")!.Text = "1";
                Click(ticket, "PreviewButton");
                Until(() => ticket.FindControl<Border>("ReviewCard")!.IsEffectivelyVisible);
                ticket.FindControl<CheckBox>("Consent")!.IsChecked = true;
                Assert.True(ticket.FindControl<Button>("ConfirmButton")!.IsEnabled);

                Select(nav, model, "settings");
                Active<SettingsView>(window);
                Select(nav, model, "trading");
                ticket = Active<TradingView>(window);
                Assert.False(ticket.FindControl<Border>("ReviewCard")!.IsEffectivelyVisible);
                Assert.False(ticket.FindControl<Button>("ConfirmButton")!.IsEnabled);
                Assert.Equal(0, broker.Sends);
                Assert.False(Directory.Exists(Path.Combine(paths.ConfigDirectory, "manual-orders")));

                Select(nav, model, "monitor");
                Assert.True(model.ShowOfflineMonitor);
                var offlineCard = window.FindControl<Border>("OfflineCard")!;
                Until(() => offlineCard.IsEffectivelyVisible);
                Fit(window, offlineCard);
                Frame(window, "monitor-offline", width);
                var setupButton = Assert.Single(offlineCard.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible);
                Fit(window, setupButton);
                setupButton.Command!.Execute(setupButton.CommandParameter);
                Until(() => model.IsOnboarding);
                Active<OnboardingView>(window);
                Frame(window, "setup", width);
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Until(() => !model.IsOnboarding);
                Select(Navigation(window), model, "studio");
                Active<StudioView>(window);
                Assert.Empty(window.OwnedWindows);
                Assert.False(model.Status.IsUsable);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Missing_key_does_not_probe_a_previously_configured_monitor_server()
    {
        using var paths = new TempPaths();
        using var trap = new TcpListener(IPAddress.Loopback, 0);
        trap.Start();
        var address = $"http://127.0.0.1:{((IPEndPoint)trap.LocalEndpoint).Port}";
        var settings = new ClientSettings { ServerAddress = address, OnboardingCompleted = true, StartPage = "monitor" };
        File.WriteAllText(Path.Combine(paths.ConfigDirectory, "settings.json"), JsonSerializer.Serialize(settings, JsonSerializerOptions.Web));
        fixture.Run(() =>
        {
            using var model = new MainWindowViewModel(paths, secretStore: new FixtureSecrets());
            foreach (string page in new[] { "studio", "trading", "settings", "monitor", "studio" }) model.Navigate(page);
            var until = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < until)
            {
                Dispatcher.UIThread.RunJobs(); model.Drain(); Thread.Sleep(10);
                Assert.False(trap.Pending(), "A shell with no access key attempted a monitor connection.");
            }
            Assert.False(model.Status.IsUsable);
        });
    }

    [Fact]
    public void Editing_the_server_never_sends_the_previous_servers_key_to_the_new_address()
    {
        using var paths = new TempPaths();
        using var oldServer = FakeServerHost.Start(instruments: 2);
        using var newOrigin = new TcpListener(IPAddress.Loopback, 0);
        newOrigin.Start();
        var settings = new ClientSettings { ServerAddress = oldServer.Address, OnboardingCompleted = true, StartPage = "monitor" };
        File.WriteAllText(Path.Combine(paths.ConfigDirectory, "settings.json"), JsonSerializer.Serialize(settings, JsonSerializerOptions.Web));
        fixture.Run(() =>
        {
            using var model = new MainWindowViewModel(paths, secretStore: new FixtureSecrets("rm_for_old_server_only"));
            var window = new MainWindow(CreateStudio) { DataContext = model, Width = 390, Height = 780 };
            window.Show();
            try
            {
                Assert.True(model.MonitorStarted);
                Assert.Equal("monitor", model.CurrentPage?.Key);
                Until(() => { model.Drain(); return model.Status.IsUsable; });
                var monitor = Assert.IsType<MonitorViewModel>(model.CurrentPage);
                Until(() => { model.Drain(); return monitor.Rows.Count > 0; });
                var monitorView = Active<MonitorView>(window);
                Until(() => { model.Drain(); return !monitor.IsEmpty &&
                    monitorView.GetVisualDescendants().OfType<DataGridRow>().Any(row => row.IsEffectivelyVisible); });
                var scroller = window.FindControl<ScrollViewer>("RemoteScroller")!;
                Until(() => scroller.Extent.Width > scroller.Viewport.Width + 50);
                // The shell's horizontal scroller must not shrink server-grid columns to a few pixels.
                var grid = monitorView.FindControl<DataGrid>("Grid")!;
                Until(() => Math.Abs(grid.Columns[0].ActualWidth - monitor.Columns[0].Width) <= 1, () =>
                    $"Unusable ticker column: window={window.ClientSize.Width}, view={monitorView.Bounds.Width}, grid={grid.Bounds.Width}, " +
                    $"extent={scroller.Extent.Width}, model widths=[{string.Join(",", monitor.Columns.Select(c => c.Width))}], " +
                    $"actual widths=[{string.Join(",", grid.Columns.Select(c => c.ActualWidth))}]");
                Frame(window, "monitor-connected", 390);
                scroller.Offset = new Vector(scroller.Extent.Width, 0);
                Until(() => scroller.Offset.X > 100);
                Frame(window, "monitor-scrolled", 390);
                model.Navigate("settings");
                Until(() => scroller.Offset.X == 0);
                var form = Assert.IsType<SettingsViewModel>(model.CurrentPage);
                form.ServerAddress = $"http://127.0.0.1:{((IPEndPoint)newOrigin.LocalEndpoint).Port}";
                Assert.Equal("", form.AccessKey);
                Assert.True(model.Settings.MonitorConnectionPending);
                model.Reconnect();
                Until(() => model.IsOnboarding);
                Assert.Equal("", model.Onboarding!.AccessKey);
                Assert.Equal(form.ServerAddress, model.Onboarding.ServerAddress);
                var end = DateTime.UtcNow.AddMilliseconds(300);
                while (DateTime.UtcNow < end)
                {
                    Dispatcher.UIThread.RunJobs(); model.Drain(); Thread.Sleep(10);
                    Assert.False(newOrigin.Pending(), "A previously cached access key must not be used against this address.");
                }
                model.DismissOnboarding();
                Assert.False(model.IsOnboarding);
            }
            finally { window.Close(); }
        });
    }

    private static Control Navigation(MainWindow window)
    {
        var rail = window.FindControl<ListBox>("PageRail")!;
        return rail.IsEffectivelyVisible ? rail : window.FindControl<ComboBox>("PagePicker")!;
    }

    private static void Select(Control navigation, MainWindowViewModel model, string key)
    {
        var page = Assert.Single(model.Pages, page => page.Key == key);
        switch (navigation)
        {
            case ListBox rail: rail.SelectedItem = page; break;
            case ComboBox picker: picker.SelectedItem = page; break;
            default: throw new InvalidOperationException("Missing shell navigation.");
        }
        Until(() => model.CurrentPage?.Key == key);
    }

    private static T Active<T>(MainWindow window) where T : Control
    {
        T? result = null;
        Until(() => (result = window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.IsEffectivelyVisible)) != null);
        Assert.Same(window, TopLevel.GetTopLevel(result!));
        return result!;
    }

    private static void Fit(Window window, Control nav)
    {
        Assert.True(nav.IsEffectivelyVisible);
        var pos = nav.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(pos);
        Assert.InRange(pos.Value.X, -1.0, window.ClientSize.Width);
        Assert.InRange(pos.Value.X + nav.Bounds.Width, 1.0, window.ClientSize.Width + 1.0);
    }

    private static void Click(TradingView ticket, string button) =>
        ticket.FindControl<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Until(Func<bool> predicate, Func<string>? diagnostic = null)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        do
        {
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (predicate()) return;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < end);
        Assert.True(predicate(), diagnostic?.Invoke());
    }

    private static void Frame(Window window, string name, int width)
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("RCKTMON_UI_ARTIFACTS") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"unified-{name}-{width}.png"), new PngBitmapEncoderOptions());
    }
}
