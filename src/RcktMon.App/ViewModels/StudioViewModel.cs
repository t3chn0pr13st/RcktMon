using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RcktMon.App.Localization;
using RcktMon.Client.Core.Studio;

namespace RcktMon.App.ViewModels;

public sealed record RadarItem(string Ticker, string Price, string Buy, string Share, string Move, string State);
public sealed record SignalItem(string Ticker, string At, string Price, string Buy, string Move);
public sealed record ReplayItem(StudioEvent Event, string At, string State, string Entry, string Exit, string Fees, string Net);

/// <summary>Owned by one native Studio view. All observable state changes resume on the UI context.</summary>
public sealed partial class StudioViewModel : ObservableObject
{
    private static string T(string key) => Localizer.Instance["Studio_" + key];
    private static string StateLabel(string key)
    {
        var label = T("State_" + key);
        return label.StartsWith("Studio_", StringComparison.Ordinal) ? key : label;
    }
    private static string F(decimal? value, string format) => value?.ToString(format, CultureInfo.CurrentCulture) ?? "—";
    private static string Net(decimal? value) => F(value, "+0.00;-0.00;0.00");
    private static string Price(decimal? value) => F(value, "#,##0.00##");
    private static string Percent(decimal? value) => F(value, "+0.00'%';-0.00'%';0.00'%'");
    private static string Time(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private readonly Func<StudioClient> _create;
    public StudioViewModel() : this(static () => new StudioClient()) { }
    public StudioViewModel(Func<StudioClient> create) => _create = create;
    private StudioClient? _client;
    private CancellationTokenSource? _life, _libraryRequest, _run;
    [ObservableProperty] private string _liveState = T("Loading");
    [ObservableProperty] private string _asOf = "—", _subscribed = "—", _fresh = "—", _prints = "—";
    [ObservableProperty] private string _liveHint = T("Loading"), _feedHint = T("Loading");
    [ObservableProperty] private RadarItem[] _radar = [];
    [ObservableProperty] private SignalItem[] _feed = [];
    [ObservableProperty] private StudioCapture[] _captures = [];
    [ObservableProperty] private StudioCapture? _selected;
    [ObservableProperty] private bool _flowOnly;
    [ObservableProperty] private decimal? _minBuy = 100000m, _slip = 10m, _move = 0.25m;
    [ObservableProperty] private string _replayHint = T("Loading");
    [ObservableProperty] private StudioReplay? _result;
    [ObservableProperty] private string _coverage = "", _modelAssumptions = "", _counts = "—", _netResult = "—", _mean = "—", _win = "—";
    [ObservableProperty] private ReplayItem[] _entries = [];
    [ObservableProperty] private ReplayItem? _selectedEntry;
    [ObservableProperty] private bool _zoom;
    [ObservableProperty] private bool _canRun;

    public void Start()
    {
        if (_life is not null) return;
        LiveState = LiveHint = FeedHint = T("Loading");
        Radar = []; Feed = []; Subscribed = Fresh = Prints = AsOf = "—";
        _life = new(); _client = _create();
        _ = PollAsync(_client, _life.Token);
    }

    public void Stop()
    {
        _life?.Cancel(); _life?.Dispose(); _life = null;
        _libraryRequest?.Cancel(); _run?.Cancel();
        _client?.Dispose(); _client = null;
    }

    private async Task PollAsync(StudioClient client, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var live = await client.LiveAsync(stop);
                    stop.ThrowIfCancellationRequested();
                    var age = DateTimeOffset.UtcNow - live.AsOf;
                    bool expired = age.TotalSeconds is > 10 or < -2
                        || (live.Status == "live" && (live.LastMessageAt is not { } message
                            || (live.AsOf - message).TotalSeconds is > 30 or < -2));
                    LiveState = expired ? T("Stale") : StateLabel(live.Status);
                    AsOf = $"{T("AsOf")} {live.AsOf.UtcDateTime:HH:mm:ss} UTC";
                    Subscribed = live.SubscribedCount.ToString("N0", CultureInfo.CurrentCulture);
                    Fresh = expired ? "—" : live.FreshCount.ToString("N0", CultureInfo.CurrentCulture);
                    Prints = live.TotalPrints.ToString("N0", CultureInfo.CurrentCulture);
                    Radar = expired ? [] : live.Instruments.OrderByDescending(x => x.BuyUsdt5s)
                        .ThenBy(x => x.Ticker, StringComparer.Ordinal).Take(12)
                        .Select(x => new RadarItem(x.Ticker, Price(x.LastPrice), F(x.BuyUsdt5s, "N0"),
                            F(x.BuyShare5s, "P0"), Percent(x.Move5sPct), StateLabel(x.State))).ToArray();
                    Feed = live.Signals.OrderByDescending(x => x.At).Take(12).Select(x => new SignalItem(
                        x.Ticker, x.At.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Price(x.Price), F(x.BuyUsdt5s, "N0"), Percent(x.Move5sPct))).ToArray();
                    LiveHint = expired ? T("Stale") : Radar.Length == 0 ? T("NoRadar") : T("RadarHint");
                    FeedHint = Feed.Length == 0 ? T("NoSignals") : T("FeedHint");
                }
                catch (Exception ex) when (!stop.IsCancellationRequested && IsReadError(ex))
                {
                    LiveState = T("Offline"); AsOf = "—"; Subscribed = Fresh = Prints = "—";
                    Radar = []; Feed = []; LiveHint = FeedHint = T("OfflineHint");
                }
                await Task.Delay(TimeSpan.FromSeconds(2), stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
    }

    private static bool IsReadError(Exception e) => e is HttpRequestException or TaskCanceledException
        or System.Text.Json.JsonException or System.IO.IOException or ArgumentException or InvalidDataException;

    public async Task RefreshLibraryAsync()
    {
        if (_client is not { } client || _life is null) return;
        _libraryRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        _libraryRequest = request;
        var old = Selected?.Id;
        Selected = null; Invalidate(); Captures = []; ReplayHint = T("Loading");
        try
        {
            var list = await client.RecordingsAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            Captures = list;
            Selected = list.FirstOrDefault(x => x.Id == old) ?? list.FirstOrDefault();
            if (Selected is null) ReplayHint = T("NoLibrary");
            else await CalculateAsync();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception e) when (!request.IsCancellationRequested && IsReadError(e)) { ReplayHint = T("LibraryError"); }
        finally { if (_libraryRequest == request) _libraryRequest = null; }
    }

    partial void OnSelectedChanged(StudioCapture? value) => Invalidate();
    partial void OnMinBuyChanged(decimal? value) => Invalidate();
    partial void OnSlipChanged(decimal? value) => Invalidate();
    partial void OnMoveChanged(decimal? value) => Invalidate();
    partial void OnFlowOnlyChanged(bool value) => Invalidate();

    private void Invalidate()
    {
        _run?.Cancel(); Result = null; Entries = []; SelectedEntry = null; Zoom = false;
        Counts = NetResult = Mean = Win = "—"; ModelAssumptions = "";
        Coverage = Selected is { } c ? string.Format(CultureInfo.CurrentCulture, T("CaptureFacts"),
            c.Instrument, c.Source, c.CryptoPrints, c.StockQuotes) : "";
        ReplayHint = T("RunHint");
        CanRun = Selected is not null && MinBuy is >= 1 and <= 10_000_000_000 && (FlowOnly || Move is >= 0 and <= 1000) && Slip is >= 0 and <= 1000;
    }

    public async Task CalculateAsync()
    {
        if (!CanRun || Selected is not { } selected || _client is not { } client || _life is null) return;
        Invalidate();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        _run = request; CanRun = false; ReplayHint = T("Loading");
        try
        {
            var data = await client.ReplayAsync(selected.Id, new(FlowOnly, MinBuy!.Value, FlowOnly ? 0 : Move!.Value, Slip!.Value), request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (data.Capture.Id != selected.Id) throw new InvalidDataException("Recording mismatch");
            Result = data;
            if (data.Options is { } o) ModelAssumptions = string.Format(CultureInfo.CurrentCulture, T("ExecutionFacts"),
                o.NominalUsdt, o.LatencyMs, o.FeeBpsPerSide, o.SlippageBpsPerSide);
            var s = data.Summary;
            Counts = $"{s.Signals} / {s.Closed}";
            NetResult = Net(s.TotalNetUsdt); Mean = Net(s.MeanNetUsdt); Win = F(s.PositiveFraction, "P0");
            Entries = data.Events.Select(e => new ReplayItem(e, Time(e.SignalMs), StateLabel(e.Status), Price(e.ModeledEntryPrice),
                Price(e.ModeledExitPrice), F(e.ModeledFeesUsdt, "0.0000"), Net(e.AfterFeesAndModeledSlippageUsdt))).ToArray();
            SelectedEntry = Entries.FirstOrDefault();
            ReplayHint = Entries.Length == 0 ? T("NoEvents") : s.Closed == 0 ? T("NoneClosed") : "";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception e) when (!request.IsCancellationRequested && IsReadError(e)) { ReplayHint = T("ReplayError"); }
        finally
        {
            if (_run == request)
            {
                _run = null;
                if (!request.IsCancellationRequested) CanRun = Selected is not null;
            }
        }
    }
}
