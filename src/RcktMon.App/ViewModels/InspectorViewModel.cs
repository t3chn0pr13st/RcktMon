using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcktMon.App.Localization;
using RcktMon.Client.Core.Market;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;

namespace RcktMon.App.ViewModels;

/// <summary>
/// The instrument detail panel, docked to the right of the monitor grid.
/// </summary>
/// <remarks>
/// A panel rather than a window on purpose: the pre-rewrite app opened separate windows
/// for detail, which on macOS scatters across spaces and on every platform loses the
/// context of the row you selected. It also carries the order-ticket placeholder, which
/// is the deliberate seam for client-side trading later — reserving the space now means
/// the information architecture does not have to be redone when it arrives.
/// </remarks>
public sealed partial class InspectorViewModel : ObservableObject, IDisposable
{
    /// <summary>Minutes of one-minute candles requested for the detail chart.</summary>
    private const int ChartMinutes = 120;

    private readonly ClientSession _session;

    private CancellationTokenSource? _chartLoad;

    public InspectorViewModel(ClientSession session) => _session = session;

    /// <summary>Raised when the user asks for a value to be put on the clipboard.</summary>
    /// <remarks>
    /// The clipboard lives on <c>TopLevel</c>, which is a view concern; the pre-rewrite
    /// code called <c>System.Windows.Clipboard</c> directly from a view model and was
    /// Windows-only as a result.
    /// </remarks>
    public event Action<string>? CopyRequested;

    /// <summary>Signals this instrument produced recently, newest first.</summary>
    public ObservableCollection<SignalEntry> RecentSignals { get; } = [];

    [ObservableProperty] private InstrumentRow? _row;

    [ObservableProperty] private bool _isOpen;

    /// <summary>Intraday closes for the detail chart, oldest first.</summary>
    [ObservableProperty] private double[] _chart = [];

    [ObservableProperty] private bool _isChartLoading;

    [ObservableProperty] private string? _chartMessage;

    /// <summary>Month baselines, when the server was asked for them.</summary>
    public MonthStats? Month => Row?.Month;

    /// <summary>Bid/ask spread as a fraction of the last price.</summary>
    public decimal SpreadFraction =>
        Row is { Price: > 0m, Spread: > 0m } row ? row.Spread / row.Price : 0m;

    /// <summary>Shows an instrument, or closes the panel when given null.</summary>
    public void Show(InstrumentRow? row)
    {
        Row = row;
        IsOpen = row is not null;

        RecentSignals.Clear();
        Chart = [];
        ChartMessage = null;

        if (row is null)
        {
            _chartLoad?.Cancel();
            return;
        }

        foreach (SignalEntry signal in _session.Signals.ForInstrument(row.Uid, 12))
        {
            RecentSignals.Add(signal);
        }

        _ = LoadChartAsync(row);
    }

    /// <summary>Re-reads values that change under the panel while it is open.</summary>
    public void Refresh()
    {
        if (Row is null)
        {
            return;
        }

        OnPropertyChanged(nameof(Month));
        OnPropertyChanged(nameof(SpreadFraction));
    }

    /// <summary>Rebuilds the signal list so its cards pick up the new language.</summary>
    public void LanguageChanged()
    {
        if (Row is { } row)
        {
            RecentSignals.Clear();

            foreach (SignalEntry signal in _session.Signals.ForInstrument(row.Uid, 12))
            {
                RecentSignals.Add(signal);
            }
        }
    }

    [RelayCommand]
    private void Close() => Show(null);

    [RelayCommand]
    private void CopyTicker()
    {
        if (Row is { } row)
        {
            CopyRequested?.Invoke(row.Ticker);
        }
    }

    [RelayCommand]
    private void CopyIsin()
    {
        if (Row is { Isin.Length: > 0 } row)
        {
            CopyRequested?.Invoke(row.Isin);
        }
    }

    private async Task LoadChartAsync(InstrumentRow row)
    {
        if (_session.Connection.History is not { } history)
        {
            ChartMessage = Strings.Inspector_NoConnection;
            return;
        }

        _chartLoad?.Cancel();
        _chartLoad?.Dispose();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _chartLoad = cts;

        IsChartLoading = true;

        try
        {
            IReadOnlyList<Candle> candles = await history.IntradayAsync(row.Uid, ChartMinutes, cts.Token);

            // The panel may have moved on to another instrument while this was in flight.
            if (cts.IsCancellationRequested || !ReferenceEquals(Row, row))
            {
                return;
            }

            if (candles.Count == 0)
            {
                ChartMessage = row.FirstCandleDate.Length > 0
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Inspector_NoChartSince, row.FirstCandleDate)
                    : Strings.Inspector_NoChart;
                return;
            }

            Chart = [.. candles.Select(c => (double)c.Close.ToDecimal())];
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection, or the panel closed.
        }
        catch (Grpc.Core.RpcException ex)
        {
            ChartMessage = string.Format(CultureInfo.CurrentCulture, Strings.Inspector_ChartFailed, ex.Status.Detail);
        }
        finally
        {
            if (ReferenceEquals(_chartLoad, cts))
            {
                IsChartLoading = false;
            }
        }
    }

    public void Dispose()
    {
        _chartLoad?.Cancel();
        _chartLoad?.Dispose();
    }
}
