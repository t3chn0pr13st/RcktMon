using Google.Protobuf.WellKnownTypes;
using RcktMon.Client.Core.Market;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;
using Xunit;

namespace RcktMon.Client.Core.Tests;

public class QuoteStoreTests
{
    private const string Uid = "uid-1";

    [Fact]
    public void Queued_messages_are_not_visible_until_drained()
    {
        // The reader thread must never mutate rows directly: everything is applied in one
        // pass on the UI thread. If Enqueue applied immediately, bindings would be raised
        // off-thread and Avalonia would throw.
        using var store = new QuoteStore();

        store.Enqueue(Snapshot(price: 100m, dayChange: 0.01m));

        Assert.Equal(0, store.Count);

        int applied = store.Drain();

        Assert.Equal(1, applied);
        Assert.Equal(1, store.Count);
        Assert.Equal(100m, store.Lookup(Uid)!.Price);
    }

    [Fact]
    public void Absent_delta_fields_mean_unchanged_not_zero()
    {
        // This is the contract that makes deltas safe. A price of 0 is legitimate — an
        // instrument that has not traded today — so "absent" and "zero" cannot share an
        // encoding.
        using var store = new QuoteStore();
        store.Enqueue(Snapshot(price: 250m, dayChange: 0.05m));
        store.Drain();

        store.Enqueue(new QuotesMessage
        {
            Batch = new QuoteBatch
            {
                Seq = 2,
                Deltas =
                {
                    new QuoteDelta
                    {
                        InstrumentUid = Uid,
                        // Only the price is present.
                        LastPrice = 260m.ToProto(),
                    },
                },
            },
        });
        store.Drain();

        InstrumentRow row = store.Lookup(Uid)!;

        Assert.Equal(260m, row.Price);
        Assert.Equal(0.05m, row.DayChange);
    }

    [Fact]
    public void Explicitly_zero_scalar_delta_is_applied()
    {
        using var store = new QuoteStore();
        store.Enqueue(Snapshot(price: 250m, dayChange: 0.05m, dayVolume: 5_000));
        store.Drain();

        var delta = new QuoteDelta { InstrumentUid = Uid, DayVolume = 0 };
        Assert.True(delta.HasDayVolume);

        store.Enqueue(new QuotesMessage { Batch = new QuoteBatch { Seq = 3, Deltas = { delta } } });
        store.Drain();

        Assert.Equal(0, store.Lookup(Uid)!.DayVolume);
    }

    [Fact]
    public void Sort_signal_fires_only_when_the_active_column_moves()
    {
        using var store = new QuoteStore();
        store.SetSortColumn(MonitorColumn.DayChange);
        store.Enqueue(Snapshot(price: 100m, dayChange: 0.01m));
        store.Drain();

        InstrumentRow row = store.Lookup(Uid)!;
        int signals = 0;
        using IDisposable subscription = row.SortKeyChanged.Subscribe(_ => signals++);

        // Price moves but DayChange does not: no reordering is possible, so no signal.
        store.Enqueue(Delta(new QuoteDelta { InstrumentUid = Uid, LastPrice = 111m.ToProto() }));
        store.Drain();
        Assert.Equal(0, signals);

        // DayChange moves: this can reorder the grid.
        store.Enqueue(Delta(new QuoteDelta { InstrumentUid = Uid, DayChange = 0.09m.ToProto() }));
        store.Drain();
        Assert.Equal(1, signals);

        // Same value again is not a change.
        store.Enqueue(Delta(new QuoteDelta { InstrumentUid = Uid, DayChange = 0.09m.ToProto() }));
        store.Drain();
        Assert.Equal(1, signals);
    }

    [Fact]
    public void Switching_sort_column_switches_which_changes_signal()
    {
        using var store = new QuoteStore();
        store.SetSortColumn(MonitorColumn.DayChange);
        store.Enqueue(Snapshot(price: 100m, dayChange: 0.01m));
        store.Drain();

        InstrumentRow row = store.Lookup(Uid)!;
        int signals = 0;
        using IDisposable subscription = row.SortKeyChanged.Subscribe(_ => signals++);

        store.SetSortColumn(MonitorColumn.Price);

        store.Enqueue(Delta(new QuoteDelta { InstrumentUid = Uid, LastPrice = 120m.ToProto() }));
        store.Drain();

        Assert.Equal(1, signals);
    }

    [Fact]
    public void Text_columns_never_signal_because_they_cannot_change()
    {
        using var store = new QuoteStore();
        store.SetSortColumn(MonitorColumn.Ticker);
        store.Enqueue(Snapshot(price: 100m, dayChange: 0.01m));
        store.Drain();

        InstrumentRow row = store.Lookup(Uid)!;
        int signals = 0;
        using IDisposable subscription = row.SortKeyChanged.Subscribe(_ => signals++);

        store.Enqueue(Delta(new QuoteDelta { InstrumentUid = Uid, DayChange = 0.5m.ToProto() }));
        store.Drain();

        Assert.Equal(0, signals);
    }

    [Fact]
    public void Tick_direction_is_set_only_on_a_real_move()
    {
        using var store = new QuoteStore();
        store.Enqueue(Snapshot(price: 100m, dayChange: 0m));
        store.Drain();

        InstrumentRow row = store.Lookup(Uid)!;
        Assert.Equal(0, row.LastTickDirection);

        store.Enqueue(Delta(new QuoteDelta { InstrumentUid = Uid, LastPrice = 101m.ToProto() }));
        store.Drain();
        Assert.Equal(1, row.LastTickDirection);

        store.Enqueue(Delta(new QuoteDelta { InstrumentUid = Uid, LastPrice = 99m.ToProto() }));
        store.Drain();
        Assert.Equal(-1, row.LastTickDirection);

        // Re-sending the same price happens constantly on illiquid names and must not
        // flash the cell.
        row.LastTickDirection = 0;
        store.Enqueue(Delta(new QuoteDelta { InstrumentUid = Uid, LastPrice = 99m.ToProto() }));
        store.Drain();
        Assert.Equal(0, row.LastTickDirection);
    }

    [Fact]
    public void Deltas_for_unknown_instruments_are_ignored()
    {
        // Better to wait for the next snapshot than to invent a row with no ticker, no
        // name and no currency that would render as a blank line in the grid.
        using var store = new QuoteStore();

        store.Enqueue(Delta(new QuoteDelta { InstrumentUid = "never-seen", LastPrice = 5m.ToProto() }));
        store.Drain();

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Final_snapshot_chunk_prunes_instruments_outside_the_selector()
    {
        using var store = new QuoteStore();
        store.Enqueue(Snapshot(price: 100m, dayChange: 0m));
        store.Drain();
        Assert.Equal(1, store.Count);

        // A fresh snapshot that no longer mentions uid-1.
        store.Enqueue(new QuotesMessage
        {
            Snapshot = new QuoteSnapshotChunk
            {
                Seq = 10,
                IsLast = true,
                Reason = SnapshotReason.SelectorChanged,
                Quotes = { new Quote { InstrumentUid = "uid-2", LastPrice = 7m.ToProto() } },
            },
        });
        store.Drain();

        Assert.Equal(1, store.Count);
        Assert.Null(store.Lookup(Uid));
        Assert.NotNull(store.Lookup("uid-2"));
    }

    [Fact]
    public void Control_messages_carry_seq_and_dropped_ticks()
    {
        using var store = new QuoteStore();

        store.Enqueue(new QuotesMessage
        {
            Control = new StreamControl
            {
                Seq = 99,
                DroppedTicks = 4,
                ServerTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch),
            },
        });
        store.Drain();

        Assert.Equal(99ul, store.LastSeq);
        Assert.Equal(4u, store.DroppedTicks);
        Assert.Equal(DateTimeOffset.UnixEpoch, store.LastServerTime);
    }

    [Fact]
    public void Catalog_and_quotes_can_arrive_in_either_order()
    {
        // The two streams are independent; whichever lands first creates the row.
        using var store = new QuoteStore();

        store.Enqueue(Snapshot(price: 100m, dayChange: 0m));
        store.Drain();

        store.ApplyCatalog([new Instrument { Uid = Uid, Ticker = "SBER", Name = "Сбер Банк", Currency = "RUB" }]);

        InstrumentRow row = store.Lookup(Uid)!;
        Assert.Equal("SBER", row.Ticker);
        Assert.Equal("RUB", row.Currency);
        Assert.Equal(100m, row.Price);
    }

    private static QuotesMessage Snapshot(decimal price, decimal dayChange, long dayVolume = 1_000) =>
        new()
        {
            Snapshot = new QuoteSnapshotChunk
            {
                Seq = 1,
                IsLast = true,
                Reason = SnapshotReason.Initial,
                Quotes =
                {
                    new Quote
                    {
                        InstrumentUid = Uid,
                        LastPrice = price.ToProto(),
                        DayOpen = price.ToProto(),
                        DayChange = dayChange.ToProto(),
                        DayVolume = dayVolume,
                        Status = TradingStatus.Normal,
                    },
                },
            },
        };

    private static QuotesMessage Delta(QuoteDelta delta) =>
        new() { Batch = new QuoteBatch { Seq = 5, Deltas = { delta } } };
}

public class InstrumentRowComparerTests
{
    [Fact]
    public void Ties_are_broken_by_uid_so_the_order_is_total()
    {
        // Without a tiebreak, the very common case of many instruments at exactly 0.00%
        // lets rows swap places between sorts, which reads as the grid jittering.
        var a = new InstrumentRow("uid-a", "AAA", "A") { DayChange = 0m };
        var b = new InstrumentRow("uid-b", "BBB", "B") { DayChange = 0m };

        IComparer<InstrumentRow> comparer = InstrumentRowComparer.For(MonitorColumn.DayChange, descending: true);

        Assert.True(comparer.Compare(a, b) != 0);
        Assert.Equal(-Math.Sign(comparer.Compare(a, b)), Math.Sign(comparer.Compare(b, a)));
    }

    [Fact]
    public void Descending_puts_the_biggest_mover_first()
    {
        var small = new InstrumentRow("uid-a", "AAA", "A") { DayChange = 0.01m };
        var big = new InstrumentRow("uid-b", "BBB", "B") { DayChange = 0.42m };

        IComparer<InstrumentRow> comparer = InstrumentRowComparer.For(MonitorColumn.DayChange, descending: true);

        Assert.True(comparer.Compare(big, small) < 0);
    }

    [Fact]
    public void Text_sorting_is_ordinal_so_it_does_not_vary_by_locale()
    {
        var a = new InstrumentRow("uid-a", "AFLT", "Аэрофлот");
        var b = new InstrumentRow("uid-b", "SBER", "Сбер Банк");

        IComparer<InstrumentRow> comparer = InstrumentRowComparer.For(MonitorColumn.Ticker, descending: false);

        Assert.True(comparer.Compare(a, b) < 0);
    }
}
