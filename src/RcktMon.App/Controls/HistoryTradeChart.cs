using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RcktMon.Client.Core.Studio;

namespace RcktMon.App.Controls;

/// <summary>Actual one-minute candles. Markers use their exact event times, not rounded candle indices.</summary>
public sealed class HistoryTradeChart : Control
{
    private HistoryCandle[] _bars = [];
    private long? _signal, _entry, _exit;
    public void Show(HistoryCandle[] bars, long signal, long? entry, long? exit)
    {
        _bars = bars; _signal = signal; _entry = entry; _exit = exit;
        InvalidateVisual();
    }

    private IBrush Ink(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var val) && val is IBrush brush ? brush : fallback;
    private static FormattedText Label(string s, IBrush brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush);
    public override void Render(DrawingContext g)
    {
        base.Render(g);
        if (_bars.Length == 0 || Bounds.Width < 120) return;
        var ink = Ink("Brush.Text.Secondary", Brushes.Gray);
        var grid = new Pen(Ink("Brush.Border", Brushes.Gray));
        var up = Ink("Brush.Positive", Brushes.Green);
        var down = Ink("Brush.Negative", Brushes.Red);
        double bottom = Bounds.Height - 21, right = Bounds.Width - 5, top = 39;
        double low = (double)_bars.Min(b => b.Low), high = (double)_bars.Max(b => b.High);
        double pad = Math.Max((high - low) * .08, Math.Max(.00001, high * .0001));
        low -= pad; high += pad;
        string Price(double v) => v.ToString("0.####", CultureInfo.CurrentCulture);
        double left = Math.Max(40, Math.Max(Label(Price(low), ink).Width, Label(Price(high), ink).Width) + 6);
        if (right <= left || bottom <= top) return;
        long start = _bars[0].TimeMs, end = _bars[^1].TimeMs + 60_000;
        double X(long t) => left + (right - left) * (t - start) / (end - start);
        double Y(decimal v) => bottom - (bottom - top) * ((double)v - low) / (high - low);
        for (int i = 0; i < 4; i++)
        {
            double y = bottom - i * (bottom - top) / 3;
            g.DrawLine(grid, new(left, y), new(right, y));
            g.DrawText(Label(Price(low + (high - low) * i / 3), ink), new(0, y - 7));
        }
        double body = Math.Clamp((X(start + 60_000) - left) * .65, 1, 13);
        foreach (var bar in _bars)
        {
            var brush = bar.Close >= bar.Open ? up : down;
            double x = X(bar.TimeMs + 30_000), a = Y(bar.Open), b = Y(bar.Close);
            g.DrawLine(new Pen(brush), new(x, Y(bar.High)), new(x, Y(bar.Low)));
            g.DrawRectangle(brush, null, new(x - body / 2, Math.Min(a, b), body, Math.Max(1, Math.Abs(b - a))));
        }
        // Three vertical lanes keep signal and entry distinguishable even when only 250 ms apart.
        Marker(_signal, 0, "S", Ink("Brush.Accent", Brushes.Orange));
        Marker(_entry, 12, "E", Ink("Brush.Text.Primary", Brushes.Gray));
        Marker(_exit, 24, "X", Ink("Brush.Text.Primary", Brushes.Gray));
        void Marker(long? ms, int lane, string label, IBrush brush)
        {
            if (ms is not long t || t < start || t > end) return;
            double x = X(t);
            g.DrawLine(new Pen(brush, 1, DashStyle.Dot), new(x, top), new(x, bottom));
            g.DrawText(Label(label, brush), new(Math.Clamp(x - 3, left, right - 8), lane));
        }
        for (int i = 0; i < 3; i++)
        {
            long t = start + (end - start) * i / 2;
            var label = Label(DateTimeOffset.FromUnixTimeMilliseconds(t).ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm",
                CultureInfo.InvariantCulture), ink);
            g.DrawText(label, new(Math.Clamp(X(t) - label.Width / 2, left, right - label.Width), bottom + 3));
        }
    }
}
