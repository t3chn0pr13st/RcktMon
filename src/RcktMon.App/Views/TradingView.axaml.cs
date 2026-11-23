using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RcktMon.App.Localization;
using RcktMon.Client.Core.Platform;
using RcktMon.Client.Core.Trading;

namespace RcktMon.App.Views;

/// <summary>Native, manually operated ticket. There is deliberately no signal, replay or timer path to Send.</summary>
public sealed partial class TradingView : UserControl, IDisposable
{
    private sealed record Choice<T>(string Label, T Value) { public override string ToString() => Label; }
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _expiryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Func<FinamTradingClient> _makeFinam;
    private readonly IAppPaths _paths;
    private WaltTradingClient? _walt;
    private FinamTradingClient? _finam;
    private WaltPreview? _wp;
    private FinamOrderReview? _fp;
    private string? _ticketTerms;
    private bool _ready, _busy, _disposed, _connected, _ledgerFault;
    private long _generation;
    private PendingSubmission? _pending;
    private TradeVenue SelectedVenue => Venue.SelectedIndex == 1 ? TradeVenue.Finam : TradeVenue.Walt;
    private bool CanSubmit => IsWalt || _finam?.IsReadOnly == false;
    private bool IsWalt => SelectedVenue == TradeVenue.Walt;
    private DateTimeOffset? Deadline => _wp?.ExpiresAt ?? _fp?.ExpiresAt;
    private SubmissionJournal Journal => new(Path.Combine(_paths.ConfigDirectory, "manual-orders"), SelectedVenue);
    private static string S(string key) => Localizer.Instance["Trade_" + key];

    public TradingView() : this(RcktMonApp.Paths ?? new AppPaths(), () => new FinamTradingClient()) { }
    internal TradingView(IAppPaths paths, Func<FinamTradingClient> makeFinam)
    {
        _paths = paths; _makeFinam = makeFinam;
        InitializeComponent();
        _ready = true;
        _expiryTimer.Tick += (_, _) =>
        {
            if (Deadline is { } limit && limit <= DateTimeOffset.UtcNow)
            { DismissReview(); Notice.Text = S("Expired"); }
        };
        // Reconfigure uses read-only recovery checks; never contacts the providers.
        Reconfigure();
    }
    private void VenueChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_busy) Reconfigure();
    }
    private void SideChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_ready) SizeGroup.IsVisible = PriceGroup.IsVisible = !(IsWalt && Side.SelectedIndex == 2);
    }
    private void Reconfigure()
    {
        DisconnectClients();
        Notice.Text = "";
        TokenFields.IsVisible = !IsWalt;
        ConnectButton.Content = S(IsWalt ? "ConnectWalt" : "ConnectFinam");
        ServiceHint.Text = S(IsWalt ? "WaltHint" : "FinamHint");
        SizeLabel.Text = S(IsWalt ? "Margin" : "Lots");
        PriceLabel.Text = S(IsWalt ? "Leverage" : "Price");
        Side.ItemsSource = IsWalt ? new[] { S("Long"), S("Short"), S("Close") } : [S("Buy"), S("Sell")];
        Side.SelectedIndex = -1;
        WaltForm.IsVisible = IsWalt;
        FinamForm.IsVisible = !IsWalt;
        SizeGroup.IsVisible = PriceGroup.IsVisible = true;
        SizeField.Text = Ticker.Text = null;
        PriceField.Text = IsWalt ? "1" : "";
        Refresh();
    }
    private void DisconnectClients()
    {
        DismissReview();
        _connected = false;
        CheckedProvider.IsChecked = false;
        Secret.Text = null;
        _walt?.Dispose(); _walt = null;
        _finam?.Dispose(); _finam = null;
        Account.ItemsSource = Contract.ItemsSource = LookupAccount.ItemsSource = null;
    }
    private void Disconnect(object? sender, RoutedEventArgs e) { if (!_busy) Reconfigure(); }
    public void DismissReview()
    {
        _generation++;
        _wp = null; _fp = null; _ticketTerms = null; _expiryTimer.Stop();
        if (!_ready) return;
        Consent.IsChecked = false;
        ReviewBody.Text = Until.Text = "";
        Draw();
    }
    private void Refresh()
    {
        if (_disposed) return;
        try { _pending = Journal.Read(); _ledgerFault = false; }
        catch { _pending = null; _ledgerFault = true; Notice.Text = S("StorageFault"); }
        Draw();
    }
    private void Draw()
    {
        if (!_ready || _disposed) return;
        bool review = Deadline.HasValue;
        Venue.IsEnabled = !_busy;
        Login.IsVisible = !_connected;
        Login.IsEnabled = DisconnectButton.IsEnabled = !_busy;
        DisconnectButton.IsVisible = _connected;
        DraftCard.IsVisible = _connected && !review && _pending == null && !_ledgerFault;
        DraftCard.IsEnabled = !_busy;
        ReviewCard.IsVisible = review;
        ReviewCard.IsEnabled = !_busy;
        ConfirmButton.IsEnabled = CanSubmit && !_busy && Consent.IsChecked == true && Deadline > DateTimeOffset.UtcNow;
        Consent.IsEnabled = CanSubmit;
        PendingCard.IsVisible = _pending != null;
        PendingCard.IsEnabled = !_busy;
        if (_pending != null) PendingBody.Text = $"{S("CheckAtProvider")}\n{_pending.Reference}  ·  {_pending.CreatedUtc.ToLocalTime():g}\n{_pending.Description}";
        LookupButton.IsVisible = LookupAccount.IsVisible = !IsWalt && _connected;
        ClearButton.IsEnabled = !_busy && CheckedProvider.IsChecked == true;
    }

    private async Task Run(Func<CancellationToken,Task> fn, string progress)
    {
        if (_busy || _disposed) return;
        _busy = true; Draw(); Notice.Text = S(progress);
        try { await fn(_lifetime.Token); }
        catch (OperationCanceledException) { if (!_disposed) Notice.Text = S("Cancelled"); }
        catch (InvalidOperationException ex) { if (!_disposed) Notice.Text = ex.Message; }
        catch (ArgumentException) { if (!_disposed) Notice.Text = S("Invalid"); }
        catch (Exception) { if (!_disposed) Notice.Text = S("Failure"); }
        finally { _busy = false; if (!_disposed) Refresh(); else { DisconnectClients(); _lifetime.Dispose(); } }
    }
    private async void Connect(object? sender, RoutedEventArgs e) => await Run(async ct =>
    {
        if (IsWalt)
        {
            _walt?.Dispose(); _walt = new WaltTradingClient();
            await _walt.ConnectAsync(uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }), ct);
            var contracts = await _walt.ListContractsAsync(ct);
            Contract.ItemsSource = contracts.Select(c => new Choice<int>($"{c.Symbol} · {c.MarketId} · #{c.Id}", c.Id)).ToList();
            Contract.SelectedIndex = -1;
        }
        else
        {
            string? secret = Secret.Text;
            Secret.Text = null;
            if (string.IsNullOrWhiteSpace(secret)) { Notice.Text = S("NeedToken"); return; }
            _finam?.Dispose(); _finam = _makeFinam();
            var accounts = await _finam.ConnectAsync(secret, ct);
            Account.ItemsSource = LookupAccount.ItemsSource = accounts;
            Account.SelectedIndex = LookupAccount.SelectedIndex = -1;
        }
        _connected = true; Notice.Text = S(CanSubmit ? "Connected" : "ReadOnly");
    }, "Connecting");

    private async void Prepare(object? sender, RoutedEventArgs e) => await Run(async ct =>
    {
        if (!_connected || Journal.Read() != null || _ledgerFault || (Side.SelectedIndex < 0 || Side.SelectedIndex > (IsWalt ? 2 : 1)))
        { Notice.Text = S("Invalid"); return; }
        long generation = _generation;
        if (IsWalt && Contract.SelectedItem is Choice<int> closing && Side.SelectedIndex == 2)
        {
            var preview = await _walt!.PrepareCloseAsync(closing.Value, ct);
            if (generation != _generation) return;
            _wp = preview; ReviewBody.Text = preview.Summary;
            _ticketTerms = string.Join(" · ", preview.Summary.Split('\n').Take(2));
        }
        else if (IsWalt && Contract.SelectedItem is Choice<int> contract && Integer(PriceField.Text, out int lever)
            && Number(SizeField.Text, out decimal margin))
        {
            var preview = await _walt!.PrepareAsync(contract.Value, Side.SelectedIndex == 0 ? "long" : "short", lever, margin, ct);
            if (generation != _generation) return;
            _wp = preview; ReviewBody.Text = preview.Summary;
            _ticketTerms = string.Join(" · ", preview.Summary.Split('\n').Take(2));
        }
        else if (!IsWalt && Account.SelectedItem is string account && Integer(SizeField.Text, out int lots)
            && Number(PriceField.Text, out decimal price) && !string.IsNullOrWhiteSpace(Ticker.Text))
        {
            var review = await _finam!.PrepareAsync(account, Ticker.Text.Trim().ToUpperInvariant(),
                Side.SelectedIndex == 0 ? FinamSide.Buy : FinamSide.Sell, lots, price, ct);
            if (generation != _generation) return;
            _fp = review;
            _ticketTerms = $"{review.Symbol} · {review.Side} · {review.Shares} × {review.LimitPrice} {review.Currency}";
            ReviewBody.Text = $"{S("Account")}: {review.AccountId}\n{review.Symbol} · {review.Board} · LIMIT DAY\n" +
                $"{S(review.Side == FinamSide.Buy ? "Buy" : "Sell")}: {review.Lots} × {review.LotSize} = {review.Shares}\n" +
                $"{S("UnitPrice")}: {review.LimitPrice} {review.Currency}\n{S("Notional")}: {review.Notional} {review.Currency}\n" +
                $"{S("FinamFees")}\nID: {review.ClientOrderId}";
        }
        else { Notice.Text = S("Invalid"); return; }
        Until.Text = $"{S("Expires")}: {Deadline!.Value.LocalDateTime:T}";
        Consent.IsChecked = false; _expiryTimer.Start(); Notice.Text = CanSubmit ? "" : S("ReadOnly");
    }, "Preparing");

    private async void Send(object? sender, RoutedEventArgs e)
    {
        if (_busy || _disposed || !_connected || !CanSubmit || Consent.IsChecked != true || _ledgerFault ||
            Deadline is not { } expires || expires <= DateTimeOffset.UtcNow) return;
        var wp = _wp; var fp = _fp;
        if (wp == null && fp == null) return;
        string? terms = _ticketTerms;
        string reference = wp != null ? wp.Id.ToString("N") : fp!.ClientOrderId;
        await Run(async ct =>
        {
            DismissReview(); // Consume UI intent before yielding the dispatcher, even on storage failure.
            using var lease = Journal.Begin(reference, terms);
            try
            {
                if (wp != null)
                {
                    string receipt = await _walt!.ExecuteAsync(wp.Id, ct);
                    lease.MarkDefinitive();
                    Notice.Text = receipt;
                }
                else
                {
                    var result = await _finam!.SubmitAsync(fp!, ct);
                    if (result.Outcome != FinamOutcome.Unknown) lease.MarkDefinitive();
                    Notice.Text = result.Outcome == FinamOutcome.Unknown ? "" : $"{S("Reply")}: {result.Status} {result.OrderId}";
                }
            }
            catch (WaltNotDispatchedException ex)
            {
                lease.MarkDefinitive();
                if (!_disposed) Notice.Text = ex.Message;
            }
            catch { if (!_disposed) Notice.Text = ""; }
        }, "Sending");
    }

    private async void Lookup(object? sender, RoutedEventArgs e) => await Run(async ct =>
    {
        if (_pending == null || _finam == null || LookupAccount.SelectedItem is not string account)
        { Notice.Text = S("SelectAccount"); return; }
        var result = await _finam.LookupAsync(account, _pending.Reference, ct);
        if (result.Outcome != FinamOutcome.Unknown) Journal.AcknowledgeCheckedReference(_pending.Reference);
        CheckedProvider.IsChecked = false;
        Notice.Text = $"{S("Reply")}: {result.Status} {result.OrderId}";
    }, "Checking");
    private void ClearChecked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _pending == null || CheckedProvider.IsChecked != true) return;
        try { Notice.Text = S(Journal.AcknowledgeCheckedReference(_pending.Reference) ? "Unlocked" : "StorageFault"); }
        catch { Notice.Text = S("StorageFault"); }
        CheckedProvider.IsChecked = false; Refresh();
    }
    private void ConsentChanged(object? sender, RoutedEventArgs e) => Draw();
    private void CheckedProviderChanged(object? sender, RoutedEventArgs e) => Draw();
    private void Discard(object? sender, RoutedEventArgs e) { if (!_busy) { DismissReview(); Notice.Text = ""; } }
    private static bool Number(string? text, out decimal value) => decimal.TryParse(text?.Trim().Replace(',', '.'),
        NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && value > 0;
    private static bool Integer(string? text, out int value) => int.TryParse(text?.Trim(), NumberStyles.None,
        CultureInfo.InvariantCulture, out value) && value > 0;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _ready = false; _generation++; _lifetime.Cancel(); _expiryTimer.Stop();
        // The cancelled operation owns its in-flight HTTP client until it leaves its finally.
        if (!_busy) { DisconnectClients(); _lifetime.Dispose(); }
    }
}
