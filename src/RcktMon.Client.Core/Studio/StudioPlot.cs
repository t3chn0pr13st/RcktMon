namespace RcktMon.Client.Core.Studio;

public readonly record struct StudioPoint(long Time, double? Perp, double? Stock, bool BreakBefore);

/// <summary>Rebase each series on its first observed value within the capture (not the zoom window).
/// Signals are instantaneous; use actual close timestamps, never bucket start, for prices.</summary>
public static class StudioPlot
{
    public static StudioPoint[] Normalize(StudioBar[] bars, long bucketMs)
    {
        decimal? perpBase = null, stockBase = null;
        long? previousBucket = null;
        var points = new List<StudioPoint>(bars.Length);
        foreach (var bar in bars)
        {
            if (bar.Close > 0) perpBase ??= bar.Close;
            if (bar.StockMidUsd is > 0) stockBase ??= bar.StockMidUsd;
            bool gap = previousBucket.HasValue && bar.TimeMs - previousBucket.Value != bucketMs;
            points.Add(new(bar.CloseMs, Delta(bar.Close, perpBase), Delta(bar.StockMidUsd, stockBase), gap));
            previousBucket = bar.TimeMs;
        }
        return points.ToArray();
    }

    private static double? Delta(decimal? value, decimal? basis) =>
        value is > 0 && basis is > 0 ? (double)((value.Value / basis.Value - 1) * 100) : null;

    public static (long Start, long End) Range(long from, long until, long? selected, int seconds)
    {
        until = Math.Max(until, from + 1);
        if (!selected.HasValue || seconds <= 0 || until - from <= seconds * 1000L) return (from, until);
        var width = seconds * 1000L;
        var left = Math.Clamp(selected.Value - width / 2, from, until - width);
        return (left, left + width);
    }
}
