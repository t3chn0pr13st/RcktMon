using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using RcktMon.Client.Core.Market;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;
using Xunit;

namespace RcktMon.Client.Core.Tests;

/// <summary>
/// The DynamicData pipeline that replaces WPF's live-sorting <c>CollectionViewSource</c>.
/// </summary>
/// <remarks>
/// This is the one behaviour the monitor grid exists for, and the one the pre-rewrite app
/// got for free. It gets its own test at the shape the view model actually uses —
/// per-row refresh signals, a coalescing buffer, a comparer subject and
/// <c>SortAndBind</c> — because a subtle ordering fault here looks exactly like "the grid
/// is a bit laggy" and would never be reported as a bug.
/// </remarks>
public sealed class LiveSortPipelineTests
{
    private const int Instruments = 400;

    [Fact]
    public async Task Rows_stay_sorted_after_thousands_of_updates()
    {
        using var store = new QuoteStore();
        store.SetSortColumn(MonitorColumn.DayChange);

        var filter = new BehaviorSubject<Func<InstrumentRow, bool>>(static _ => true);
        var comparer = new BehaviorSubject<IComparer<InstrumentRow>>(
            InstrumentRowComparer.For(MonitorColumn.DayChange, descending: true));

        // Same shape as MonitorViewModel, with the UI scheduler swapped for an immediate
        // one so the test does not need a dispatcher.
        using IDisposable pipeline = store.Connect()
            .Filter(filter)
            .AutoRefreshOnObservable(
                static row => row.SortKeyChanged,
                changeSetBuffer: TimeSpan.FromMilliseconds(50),
                scheduler: TaskPoolScheduler.Default)
            .ObserveOn(CurrentThreadScheduler.Instance)
            .SortAndBind(
                out ReadOnlyObservableCollection<InstrumentRow> rows,
                comparer,
                new SortAndBindOptions { UseBinarySearch = true, ResetThreshold = 64 })
            .Subscribe();

        store.Enqueue(Snapshot(Instruments));
        store.Drain();

        Assert.Equal(Instruments, rows.Count);

        var random = new Random(1234);

        // Twenty rounds of a quarter of the universe moving, which is roughly what an
        // active minute looks like at the fake server's stress settings.
        for (int round = 0; round < 20; round++)
        {
            store.Enqueue(Batch(random, Instruments / 4, round));
            store.Drain();

            await Task.Delay(80, TestContext.Current.CancellationToken);
        }

        // Let the last buffered refresh land.
        await Task.Delay(300, TestContext.Current.CancellationToken);
        store.Drain();
        await Task.Delay(300, TestContext.Current.CancellationToken);

        AssertSorted(rows, descending: true);
    }

    [Fact]
    public async Task Switching_the_sort_column_reorders_everything()
    {
        using var store = new QuoteStore();
        store.SetSortColumn(MonitorColumn.DayChange);

        var filter = new BehaviorSubject<Func<InstrumentRow, bool>>(static _ => true);
        var comparer = new BehaviorSubject<IComparer<InstrumentRow>>(
            InstrumentRowComparer.For(MonitorColumn.DayChange, descending: true));

        using IDisposable pipeline = store.Connect()
            .Filter(filter)
            .AutoRefreshOnObservable(
                static row => row.SortKeyChanged,
                changeSetBuffer: TimeSpan.FromMilliseconds(50),
                scheduler: TaskPoolScheduler.Default)
            .ObserveOn(CurrentThreadScheduler.Instance)
            .SortAndBind(
                out ReadOnlyObservableCollection<InstrumentRow> rows,
                comparer,
                new SortAndBindOptions { UseBinarySearch = true, ResetThreshold = 64 })
            .Subscribe();

        store.Enqueue(Snapshot(Instruments));
        store.Drain();

        store.SetSortColumn(MonitorColumn.DayVolume);
        comparer.OnNext(InstrumentRowComparer.For(MonitorColumn.DayVolume, descending: true));

        await Task.Delay(200, TestContext.Current.CancellationToken);

        AssertSorted(rows, descending: true, MonitorColumn.DayVolume);
    }

    private static void AssertSorted(
        IReadOnlyList<InstrumentRow> rows,
        bool descending,
        MonitorColumn column = MonitorColumn.DayChange)
    {
        for (int i = 1; i < rows.Count; i++)
        {
            decimal previous = rows[i - 1].GetNumericSortValue(column) ?? 0m;
            decimal current = rows[i].GetNumericSortValue(column) ?? 0m;

            bool ordered = descending ? previous >= current : previous <= current;

            Assert.True(
                ordered,
                $"rows {i - 1} and {i} are out of order: {rows[i - 1].Ticker} {previous} " +
                $"then {rows[i].Ticker} {current}");
        }
    }

    private static QuotesMessage Snapshot(int count)
    {
        var chunk = new QuoteSnapshotChunk { Seq = 1, IsLast = true, Reason = SnapshotReason.Initial };
        var random = new Random(7);

        for (int i = 0; i < count; i++)
        {
            decimal open = 100m + i;

            chunk.Quotes.Add(new Quote
            {
                InstrumentUid = $"uid-{i:D4}",
                LastPrice = open.ToProto(),
                DayOpen = open.ToProto(),
                DayChange = ((decimal)(random.NextDouble() - 0.5) / 10m).ToProto(),
                DayVolume = random.Next(1000, 5_000_000),
                Status = TradingStatus.Normal,
            });
        }

        return new QuotesMessage { Snapshot = chunk };
    }

    private static QuotesMessage Batch(Random random, int count, int seq)
    {
        var batch = new QuoteBatch { Seq = (ulong)(seq + 2) };

        for (int i = 0; i < count; i++)
        {
            int index = random.Next(Instruments);

            batch.Deltas.Add(new QuoteDelta
            {
                InstrumentUid = $"uid-{index:D4}",
                DayChange = ((decimal)(random.NextDouble() - 0.5) / 5m).ToProto(),
                LastPrice = ((decimal)(random.NextDouble() * 500)).ToProto(),
            });
        }

        return new QuotesMessage { Batch = batch };
    }
}
