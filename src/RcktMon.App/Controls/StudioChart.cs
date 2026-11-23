using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RcktMon.App.Localization;
using RcktMon.App.ViewModels;
using RcktMon.Client.Core.Studio;

namespace RcktMon.App.Controls;

/// <summary>Time-scaled cash/perpetual lines with signal markers. A missing SIP price or bucket breaks the line.</summary>
public sealed class StudioChart : Control
{
    public static readonly StyledProperty<StudioReplay?> ReplayProperty = AvaloniaProperty.Register<StudioChart, StudioReplay?>(nameof(Replay));
    public static readonly StyledProperty<ReplayItem?> SelectedProperty = AvaloniaProperty.Register<StudioChart, ReplayItem?>(nameof(Selected));
    public static readonly StyledProperty<bool> ZoomProperty = AvaloniaProperty.Register<StudioChart, bool>(nameof(Zoom));
    public StudioReplay? Replay { get => GetValue(ReplayProperty); set => SetValue(ReplayProperty, value); }
    public ReplayItem? Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public bool Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    static StudioChart() => AffectsRender<StudioChart>(ReplayProperty, SelectedProperty, ZoomProperty);
    private StudioReplay? _cached;
    private StudioPoint[] _points = [];

    private IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;
    private static FormattedText Label(string text, IBrush ink) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, ink);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var ink = Brush("Brush.Text.Secondary", Brushes.Gray);
        var grid = new Pen(Brush("Brush.Border", Brushes.Gray), 1);
        var orange = Brush("Brush.Accent", Brushes.Orange);
        var market = new Pen(orange, 2);
        var stock = new Pen(Brush("Brush.Text.Primary", Brushes.LightGray), 1.5, DashStyle.Dash);
        var marker = new Pen(Brush("Brush.Positive", Brushes.Green), 1, DashStyle.Dot);
        if (Replay != _cached)
        {
            _cached = Replay;
            _points = _cached is null ? [] : StudioPlot.Normalize(_cached.Bars, _cached.BucketMs);
        }
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 100 || h < 60) return;
        const double left = 53, top = 8;
        double right = w - 7, bottom = h - 22;
        if (Replay is not { } r) return;
        var (from, until) = StudioPlot.Range(r.Capture.From.ToUnixTimeMilliseconds(), r.Capture.Until.ToUnixTimeMilliseconds(), Selected?.Event.SignalMs, Zoom ? 60 : 0);
        var points = _points.Where(p => p.Time >= from && p.Time <= until).ToArray();
        var values = points.SelectMany(p => new[] { p.Perp, p.Stock }).OfType<double>().ToArray();
        if (values.Length == 0)
        {
            context.DrawText(Label(Localizer.Instance["Studio_NoChart"], ink), new(left, h / 2 - 8));
            return;
        }
        double lo = values.Min(), hi = values.Max(), pad = Math.Max((hi - lo) * .08, .01);
        lo -= pad; hi += pad;
        double X(long t) => left + (right - left) * (t - from) / (until - from);
        double Y(double v) => bottom - (bottom - top) * (v - lo) / (hi - lo);
        for (int i = 0; i < 4; i++)
        {
            double v = lo + (hi - lo) * i / 3, y = Y(v);
            context.DrawLine(grid, new(left, y), new(right, y));
            context.DrawText(Label(v.ToString("0.00'%'", CultureInfo.CurrentCulture), ink), new(0, y - 7));
        }
        foreach (var e in r.Events.Where(e => e.SignalMs >= from && e.SignalMs <= until))
        {
            var x = X(e.SignalMs);
            context.DrawLine(marker, new(x, top + 5), new(x, bottom));
            context.DrawEllipse(marker.Brush, null, new(x, top + 3), e.SignalMs == Selected?.Event.SignalMs ? 4 : 2, 3);
        }
        Point? a = null, b = null;
        foreach (var p in points)
        {
            if (p.BreakBefore) { a = null; b = null; }
            Trace(p.Perp, market, ref a);
            Trace(p.Stock, stock, ref b);
            void Trace(double? pct, Pen pen, ref Point? previous)
            {
                if (pct is not { } v) { previous = null; return; }
                var current = new Point(X(p.Time), Y(v));
                if (previous is { } prev) context.DrawLine(pen, prev, current);
                else context.DrawEllipse(pen.Brush, null, current, 1.3, 1.3);
                previous = current;
            }
        }
        for (int i = 0; i < 3; i++)
        {
            long t = from + (until - from) * i / 2;
            var label = Label(DateTimeOffset.FromUnixTimeMilliseconds(t).UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + (i == 2 ? " UTC" : ""), ink);
            context.DrawText(label, new(Math.Clamp(X(t) - label.Width / 2, left, right - label.Width), bottom + 4));
        }
    }
}
