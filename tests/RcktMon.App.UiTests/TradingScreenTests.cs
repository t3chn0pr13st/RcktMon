using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RcktMon.App.Localization;
using RcktMon.App.Views;
using RcktMon.Client.Core.Trading;
using Xunit;

namespace RcktMon.App.UiTests;

public sealed class TradingScreenTests(HeadlessSessionFixture fixture) : IClassFixture<HeadlessSessionFixture>
{
    internal sealed class Broker : HttpMessageHandler
    {
        public int Sends;
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg, CancellationToken ct)
        {
            Requests++;
            string path = msg.RequestUri!.GetComponents(UriComponents.Path, UriFormat.Unescaped);
            object result;
            if (msg.Method == HttpMethod.Post && path.EndsWith("/orders", StringComparison.Ordinal))
            {
                Sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            }
            result = path switch
            {
                "v1/sessions" => new { token = "fixture-session" },
                "v1/sessions/details" => new { account_ids = new[] { "QA" }, expires_at = DateTimeOffset.UtcNow.AddMinutes(10), @readonly = false },
                "v1/assets/SBER@MISX" => new { id = "fixture-asset", isin = "RU0009029540", ticker = "SBER", mic = "MISX", board = "TQBR",
                    type = "EQUITIES", quote_currency = "RUB", lot_size = new { value = "10" }, min_step = 1, decimals = 2 },
                "v1/assets/SBER@MISX/params" => new { symbol = "SBER@MISX", account_id = "QA", is_tradable = true, trade_lot_size = 10 },
                "v1/accounts/QA/orders" => new { orders = Array.Empty<int>() },
                "v1/accounts/QA" => new { account_id = "QA", cash = new[] {new { currency_code = "RUB", units = "9000" }} },
                _ => throw new InvalidOperationException("Unexpected fixture request")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result) });
        }
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public void Draft_review_unknown_and_restart_are_separate(int width)
    {
        fixture.Run(() =>
        {
            Localizer.Instance.Use("ru");
            using var paths = new TempPaths();
            var broker = new Broker();
            using var ticket = new TradingView(paths, () => new FinamTradingClient(broker, TimeProvider.System));
            var window = new Window { Width = width, Height = 800, Content = ticket };
            window.Show();
            T Get<T>(string id) where T : Control => ticket.FindControl<T>(id)!;
            void Click(string name) => Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                Assert.False(Get<Border>("DraftCard").IsVisible);
                Get<ComboBox>("Venue").SelectedIndex = 1;
                Assert.True(Get<TextBox>("Secret").PasswordChar != '\0');
                Frame(window, "finam-connect", width);
                Get<TextBox>("Secret").Text = "offline-fixture-only";
                Click("ConnectButton");
                Until(() => Get<Border>("DraftCard").IsVisible);
                Assert.True(string.IsNullOrEmpty(Get<TextBox>("Secret").Text));
                Get<ComboBox>("Account").SelectedIndex = 0;
                Get<ComboBox>("Side").SelectedIndex = 0;
                Get<TextBox>("Ticker").Text = "SBER";
                Get<TextBox>("PriceField").Text = "250,4";
                Get<TextBox>("SizeField").Text = "1";
                Click("PreviewButton");
                Until(() => Get<Border>("ReviewCard").IsVisible);
                Assert.Contains("2504", Get<TextBlock>("ReviewBody").Text);
                Assert.False(Get<Button>("ConfirmButton").IsEnabled);
                Assert.Equal(0, broker.Sends);
                Frame(window, "review", width);
                ticket.DismissReview();
                Assert.False(Get<Border>("ReviewCard").IsVisible);
                Assert.False(Get<Button>("ConfirmButton").IsEnabled);
                Click("PreviewButton");
                Until(() => Get<Border>("ReviewCard").IsVisible);
                Get<CheckBox>("Consent").IsChecked = true;
                Assert.True(Get<Button>("ConfirmButton").IsEnabled);
                Click("ConfirmButton"); Click("ConfirmButton");
                Until(() => Get<Border>("PendingCard").IsVisible);
                Assert.Equal(1, broker.Sends);
                Assert.False(Get<Border>("DraftCard").IsVisible);
                Assert.False(Get<Button>("ClearButton").IsEnabled);
                Frame(window, "unknown", width);
                using var reopened = new TradingView(paths, () => throw new InvalidOperationException("No automatic connections"));
                reopened.FindControl<ComboBox>("Venue")!.SelectedIndex = 1;
                Assert.True(reopened.FindControl<Border>("PendingCard")!.IsVisible);
                Assert.Equal(1, broker.Sends);
            }
            finally { window.Close(); }
        });
    }
    private static void Until(Func<bool> predicate)
    {
        var finish = DateTime.UtcNow.AddSeconds(4);
        while (!predicate() && DateTime.UtcNow < finish) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Assert.True(predicate());
    }
    private static void Frame(Window window, string state, int width)
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var img = window.CaptureRenderedFrame();
        Assert.NotNull(img);
        if (Environment.GetEnvironmentVariable("RCKTMON_UI_ARTIFACTS") is not { Length: > 0 } dest) return;
        Directory.CreateDirectory(dest);
        img.Save(Path.Combine(dest, $"trade-{state}-{width}.png"), new PngBitmapEncoderOptions());
    }
}
