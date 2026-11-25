using System.Globalization;
using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RcktMon.App.Localization;
using RcktMon.Client.Core.Studio;

namespace RcktMon.App.Views;

public sealed record VariantDisplay(ComparisonRow Row, string Rule, string Exit, string Pause, string Net,
    string Delta, string Priced, string Title, string CompactProfit, string CompactCoverage);

public sealed partial class ComparisonPane : UserControl
{
    private readonly Func<StudioClient> _factory;
    private CancellationTokenSource? _life, _selection, _chartLife;
    private StudioClient? _client;
    private ArchiveCatalog? _catalog;
    private ComparisonRun[] _jobs = [];
    private ComparisonResult? _result;
    private string? _resultId, _loadingId, _requestedId, _listSignature;
    private bool _setup, _submitting, _online, _initializedDates;
    private static string L(string key) => Localizer.Instance["Compare_" + key];
    private static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, L(key), args);
    private static string Money(decimal? value) => value?.ToString("+#,0.00;-#,0.00;0.00", CultureInfo.CurrentCulture) ?? "—";
    private static bool CommonError(Exception e) => e is HttpRequestException or OperationCanceledException or JsonException or IOException or ObjectDisposedException;
    private bool Books => ExecutionModel.SelectedIndex == 1;

    public ComparisonPane() : this(() => new StudioClient()) { }
    public ComparisonPane(Func<StudioClient> make)
    {
        _factory = make;
        InitializeComponent();
        ExecutionModel.SelectedIndex = 0;
        Layout(false);
        SizeChanged += (_, e) => Layout(e.NewSize.Width < 690);
    }

    private void Layout(bool narrow)
    {
        Resources["ComparisonWide"] = !narrow; Resources["ComparisonNarrow"] = narrow;
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _life = new(); _client = _factory();
        RunStatus.Text = L("Loading");
        _ = Poll(_client, _life.Token);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop(ref _life); Stop(ref _selection); Stop(ref _chartLife);
        _client?.Dispose(); _client = null;
        _loadingId = _listSignature = null; _online = false;
        base.OnDetachedFromVisualTree(e);
    }
    private static void Stop(ref CancellationTokenSource? src)
    {
        src?.Cancel(); src?.Dispose(); src = null;
    }

    private sealed record JobChoice(string Id, string Label) { public override string ToString() => Label; }
    private sealed record TradeChoice(ComparisonTrade Trade, string Label) { public override string ToString() => Label; }
    private async Task Poll(StudioClient client, CancellationToken ct)
    {
        var nextCatalog = DateTime.MinValue;
        string? latestFinished = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Status polling is cheap. The coverage endpoint enumerates tick archives, so refresh it on completion or occasionally.
                var runs = await client.ComparisonsAsync(ct);
                ct.ThrowIfCancellationRequested();
                _online = true; _jobs = runs;
                UpdateJobs();
                var completedId = runs.FirstOrDefault(r => r.State == "complete")?.Id;
                if (latestFinished != completedId || DateTime.UtcNow >= nextCatalog)
                {
                    latestFinished = completedId;
                    nextCatalog = DateTime.UtcNow.AddMinutes(1);
                    try
                    {
                        var cat = await client.ArchiveCatalogAsync(ct);
                        ct.ThrowIfCancellationRequested();
                        _catalog = cat;
                        ArchiveCoverage.Text = F("Cache", cat.First, cat.Last);
                        if (!_initializedDates)
                        {
                            var previous = runs.FirstOrDefault(r => r.State == "complete");
                            var last = DateOnly.ParseExact(cat.Last, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                            var first = DateOnly.ParseExact(cat.First, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                            var start = last.AddDays(-88);
                            CompareFrom.Text = previous?.FromDate ?? (first > start ? first : start).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                            CompareThrough.Text = previous?.ThroughDate ?? cat.Last;
                            _initializedDates = true;
                        }
                    }
                    catch (Exception ex) when (CommonError(ex) && !ct.IsCancellationRequested)
                    {
                        _catalog = null; ArchiveCoverage.Text = Localizer.Instance["History_Unavailable"];
                        nextCatalog = DateTime.UtcNow.AddSeconds(5);
                    }
                }
                if (SelectedJob is { State: "complete" } completed && _resultId != completed.Id && _loadingId != completed.Id)
                    _ = Load(completed.Id);
            }
            catch (Exception ex) when (CommonError(ex))
            {
                if (ct.IsCancellationRequested) return;
                _online = false; RunStatus.Text = Localizer.Instance["History_Unavailable"];
            }
            SetButtons();
            try { await Task.Delay(_jobs.Any(j => j.State == "running") ? 1200 : 4500, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
    private ComparisonRun? SelectedJob => _jobs.FirstOrDefault(r => r.Id == (CompareRuns.SelectedItem as JobChoice)?.Id);
    private string State(string state) => state switch
    {
        "running" => L("Running"), "complete" => L("Complete"), "error" => L("Failed"),
        "cancelled" => L("Cancelled"), "interrupted" => L("Interrupted"), _ => state
    };

    private void UpdateJobs()
    {
        var sig = string.Join('|', _jobs.Select(r => r.Id + r.State));
        if (_listSignature != sig || _requestedId != null)
        {
            var selected = _requestedId ?? (CompareRuns.SelectedItem as JobChoice)?.Id;
            var choices = _jobs.Select(r => new JobChoice(r.Id, $"{r.FromDate} — {r.ThroughDate} · {State(r.State)} · {r.Id[..Math.Min(6, r.Id.Length)]}")).ToArray();
            _setup = true;
            CompareRuns.ItemsSource = choices;
            CompareRuns.SelectedItem = choices.FirstOrDefault(x => x.Id == selected) ?? choices.FirstOrDefault();
            CompareRuns.IsEnabled = choices.Length > 0;
            _setup = false; _listSignature = sig;
            if (choices.Any(c => c.Id == _requestedId)) _requestedId = null;
        }
        ShowJob();
    }
    private void ShowJob()
    {
        var job = SelectedJob;
        RunStatus.Text = job == null ? L("Empty") :
            job.State == "complete" ? F("Done", job.FromDate, job.ThroughDate) :
            $"{State(job.State)} · {job.Message}" + (job.Total > 0 ? $" · {job.Done}/{job.Total}" : "");
        if (job == null || job.State != "complete" || job.Id != _resultId)
        {
            ResultCard.IsVisible = DetailCard.IsVisible = false;
        }
        else ResultCard.IsVisible = DetailCard.IsVisible = true;
    }
    private void RunSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_setup || _life == null) return;
        Stop(ref _selection); Stop(ref _chartLife); _loadingId = null;
        ShowJob(); SetButtons();
        if (SelectedJob is { State: "complete" } c && c.Id != _resultId) _ = Load(c.Id);
    }
    private async Task Load(string id)
    {
        if (_client == null || _life == null) return;
        Stop(ref _selection);
        _selection = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        var ct = _selection.Token; _loadingId = id;
        try
        {
            var result = await _client.ComparisonResultAsync(id, ct);
            if (ct.IsCancellationRequested || SelectedJob?.Id != id) return;
            _result = result; _resultId = id;
            ResultCard.IsVisible = true; RenderRows();
        }
        catch (Exception ex) when (CommonError(ex)) { if (!ct.IsCancellationRequested) RunStatus.Text = L("ResultError"); }
        finally { if (!ct.IsCancellationRequested) _loadingId = null; }
    }

    private void SetButtons()
    {
        CompareStart.IsEnabled = !_submitting && _online && _catalog != null && !_jobs.Any(j => j.State == "running");
        CompareCancel.IsVisible = SelectedJob?.State == "running";
        CompareCancel.IsEnabled = _online && !_submitting;
    }
    private void Message(string? s) { ActionMessage.Text = s; ActionMessage.IsVisible = s != null; }
    private async void Run(object? sender, RoutedEventArgs e)
    {
        if (_client == null || _life == null || !CompareStart.IsEnabled || _catalog == null) return;
        static bool Date(string? s, out DateOnly d) => DateOnly.TryParseExact(s?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        if (!Date(CompareFrom.Text, out var from) || !Date(CompareThrough.Text, out var through) || through < from ||
            through.DayNumber - from.DayNumber >= 366 || !Date(_catalog.First, out var first) || !Date(_catalog.Last, out var last) || from < first ||
            through > last.AddDays(7) || through >= DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date))
        { Message(L("Invalid")); return; }
        var ct = _life.Token;
        _submitting = true; SetButtons();
        try
        {
            var id = await _client.StartComparisonAsync(from, through, ct);
            ct.ThrowIfCancellationRequested();
            _requestedId = id;
            Message(null);
            _jobs = await _client.ComparisonsAsync(ct);
            ct.ThrowIfCancellationRequested(); UpdateJobs();
        }
        catch (Exception ex) when (CommonError(ex)) { if (!ct.IsCancellationRequested) Message(ex is HttpRequestException { StatusCode: HttpStatusCode.Conflict } ? L("Busy") : L("StartError")); }
        finally { _submitting = false; if (!ct.IsCancellationRequested) SetButtons(); }
    }
    private async void Cancel(object? sender, RoutedEventArgs e)
    {
        if (SelectedJob is not {State:"running"} job || _client == null || _life == null || _submitting) return;
        var ct = _life.Token; _submitting = true; SetButtons();
        try { await _client.CancelComparisonAsync(job.Id, ct); if (!ct.IsCancellationRequested) Message(L("Cancelling")); }
        catch (Exception ex) when (CommonError(ex)) { if (!ct.IsCancellationRequested) Message(L("StartError")); }
        finally { _submitting = false; if (!ct.IsCancellationRequested) SetButtons(); }
    }

    private void ModelChanged(object? sender, SelectionChangedEventArgs e)
    { if (_result != null) RenderRows(); }
    private void RenderRows()
    {
        if (_result == null) return;
        bool book = Books;
        var old = (VariantRows.SelectedItem as VariantDisplay)?.Row;
        ModelHelp.Text = L(book ? "BookHelp" : "PrintHelp");
        var rows = _result.Rows.OrderBy(r => r.Rule == "original" ? 0 : 1).ThenBy(r => r.ExitRed).ThenBy(r => r.CooldownHours).Select(r =>
        {
            var baseline = _result.Rows.FirstOrDefault(b => b.Rule == r.Rule && b.ExitRed == r.ExitRed && b.CooldownHours == 0);
            decimal? Net(ComparisonRow? row) => book ? row?.BookNet : row?.ProxyNet;
            int Priced(ComparisonRow row) => book ? row.BookPriced : row.ProxyPriced;
            var delta = r.CooldownHours == 0 ? "—" : Money(Net(r) - Net(baseline));
            if (delta != "—" && baseline != null && (Priced(r) != r.Entered || Priced(baseline) != baseline.Entered)) delta += "*";
            string rule = L(r.Rule == "original" ? "Original" : "Relative"), exit = L(r.ExitRed == 1 ? "First" : "Third"),
                pause = L(r.CooldownHours == 0 ? "Off" : "Day"), net = Money(Net(r)), priced = $"{Priced(r):N0} / {r.Entered:N0}";
            return new VariantDisplay(r, rule, exit, pause, net, delta, priced, $"{rule} · {exit} · {pause}",
                F("CompactProfit", net, delta), F("CompactCoverage", priced));
        }).ToArray();
        _setup = true;
        VariantRows.ItemsSource = rows;
        VariantRows.SelectedItem = rows.FirstOrDefault(r => r.Row == old) ?? rows.FirstOrDefault();
        _setup = false; RenderTickers();
    }
    private void VariantChanged(object? sender, SelectionChangedEventArgs e)
    { if (!_setup) RenderTickers(); }
    private IEnumerable<ComparisonTrade> Trades(ComparisonRow r) => _result?.Trades.Where(t =>
        t.Rule == r.Rule && t.ExitRed == r.ExitRed && t.CooldownHours == r.CooldownHours) ?? [];
    private void RenderTickers()
    {
        var row = VariantRows.SelectedItem as VariantDisplay;
        DetailCard.IsVisible = row != null;
        if (row == null) return;
        var r = row.Row;
        VariantFacts.Text = F("Facts", row.Title, r.Signals, r.Entered, Books ? r.BookWins : r.ProxyWins) +
            (r.CooldownHours > 0 ? " " + F("Skipped", r.CooldownSkipped) : "");
        var prior = TickerFilter.SelectedItem as string;
        var tickers = Trades(r).Select(t => t.Ticker).Distinct().Order(StringComparer.Ordinal).ToArray();
        _setup = true; TickerFilter.ItemsSource = tickers;
        TickerFilter.SelectedItem = tickers.FirstOrDefault(t => t == prior) ?? tickers.FirstOrDefault(t => t == "NVDA") ?? tickers.FirstOrDefault();
        _setup = false; RenderTrades();
    }
    private void TickerChanged(object? sender, SelectionChangedEventArgs e) { if (!_setup) RenderTrades(); }
    private void RenderTrades()
    {
        if (VariantRows.SelectedItem is not VariantDisplay row) return;
        var prior = (TradePicker.SelectedItem as TradeChoice)?.Trade.SignalMs;
        var items = Trades(row.Row).Where(t => t.Ticker == (TickerFilter.SelectedItem as string)).OrderByDescending(t => t.SignalMs)
            .Select(t => new TradeChoice(t, $"{Clock(t.SignalMs, "yyyy-MM-dd  HH:mm:ss.fff")}")).ToArray();
        _setup = true; TradePicker.ItemsSource = items;
        TradePicker.SelectedItem = items.FirstOrDefault(t => t.Trade.SignalMs == prior) ?? items.FirstOrDefault();
        _setup = false; RenderTrade();
    }
    private static string Clock(long? ms, string fmt = "HH:mm:ss.fff") =>
        ms is long t ? DateTimeOffset.FromUnixTimeMilliseconds(t).ToOffset(TimeSpan.FromHours(8)).ToString(fmt, CultureInfo.InvariantCulture) : "—";
    private void TradeChanged(object? sender, SelectionChangedEventArgs e) { if (!_setup) RenderTrade(); }
    private void RenderTrade()
    {
        Stop(ref _chartLife); TradeChart.IsVisible = false; ChartStatus.Text = "";
        if (_life == null || _client == null || _resultId == null || TradePicker.SelectedItem is not TradeChoice chosen)
        { TradeFacts.Text = L("NoTrades"); return; }
        var t = chosen.Trade;
        // Book timing is an as-of quote; print confirmation/exit occurs separately in the tape.
        long? en = Books ? (t.EntryVwap.HasValue ? t.SignalMs + 250 : null) : t.EntryMs;
        long? ex = Books ? (t.ExitVwap.HasValue ? t.ExitMs : null) : (t.ExitPrice.HasValue ? t.ProxyExitMs : null);
        string Price(decimal? d) => d?.ToString("0.#####", CultureInfo.CurrentCulture) ?? "—";
        TradeFacts.Text = F("TradeFacts", t.Ticker, Clock(t.SignalMs), Clock(en), Price(Books ? t.EntryVwap : t.EntryPrice),
            Clock(ex), Price(Books ? t.ExitVwap : t.ExitPrice), Money(Books ? t.BookNet : t.ProxyNet));
        var net = Books ? t.BookNet : t.ProxyNet;
        if (net == null)
        {
            string reason = (Books ? t.BookStatus : t.ProxyStatus) switch
            {
                "stale_entry" or "no_entry" => "MissingEntry",
                "stale_exit" or "no_exit" or "no_red_close" => "MissingExit",
                "entry_missing_snapshot" or "exit_missing_snapshot" => "MissingBook",
                _ => "NotPriced"
            };
            TradeFacts.Text += "\n" + L(reason);
        }
        else if ((Books ? t.BookFees : t.ProxyFees) is decimal fees && (Books ? t.BookFunding : t.ProxyFunding) is decimal funding)
            TradeFacts.Text += "\n" + F("Costs", fees, funding);
        ChartStatus.Text = L("ChartLoading");
        _chartLife = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        _ = Chart(_client, _resultId, t, en, ex, _chartLife.Token);
    }
    private async Task Chart(StudioClient client, string id, ComparisonTrade t, long? entry, long? exit, CancellationToken ct)
    {
        try
        {
            var candles = await client.ComparisonCandlesAsync(id, t, ct);
            if (ct.IsCancellationRequested) return;
            ChartStatus.Text = L(candles.Length > 0 ? "Legend" : "NoChart");
            if (candles.Length > 0 && t.ExitMs - t.SignalMs > 6 * 3600_000L) ChartStatus.Text += " " + L("LongChart");
            if (candles.Length > 0) { TradeChart.Show(candles, t.SignalMs, entry, exit); TradeChart.IsVisible = true; }
        }
        catch (Exception ex) when (CommonError(ex)) { if (!ct.IsCancellationRequested) ChartStatus.Text = L("NoChart"); }
    }
}
