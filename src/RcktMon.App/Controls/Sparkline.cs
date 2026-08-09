using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RcktMon.App.Controls;

/// <summary>
/// A line of recent values, drawn directly.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a charting library. One of these is realised per visible grid row, so
/// the cost of a cell has to be a handful of line segments and one cached geometry — a
/// general-purpose chart control brings axes, legends, hit testing and layout passes that
/// are all wasted at 90×20 pixels, and it is the reason per-row charts are usually the
/// thing that makes a grid stutter.
/// </para>
/// <para>
/// The same control draws the inspector's detail chart, with an area fill and a thicker
/// stroke. Two renderers for one shape would be two things to keep looking alike.
/// </para>
/// </remarks>
public sealed class Sparkline : Control
{
    /// <summary>Values to draw, oldest first. Accepts int (basis points) or double.</summary>
    public static readonly StyledProperty<IEnumerable?> SourceProperty =
        AvaloniaProperty.Register<Sparkline, IEnumerable?>(nameof(Source));

    /// <summary>Line colour. Defaults to positive or negative depending on the trend.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 1.25);

    /// <summary>Fills the area under the line, for the larger detail chart.</summary>
    public static readonly StyledProperty<bool> ShowAreaProperty =
        AvaloniaProperty.Register<Sparkline, bool>(nameof(ShowArea));

    /// <summary>Brush for the trend-up case when <see cref="Stroke"/> is unset.</summary>
    public static readonly StyledProperty<IBrush?> PositiveBrushProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(PositiveBrush));

    /// <summary>Brush for the trend-down case when <see cref="Stroke"/> is unset.</summary>
    public static readonly StyledProperty<IBrush?> NegativeBrushProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(NegativeBrush));

    static Sparkline()
    {
        AffectsRender<Sparkline>(
            SourceProperty,
            StrokeProperty,
            StrokeThicknessProperty,
            ShowAreaProperty,
            PositiveBrushProperty,
            NegativeBrushProperty);
    }

    public IEnumerable? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool ShowArea
    {
        get => GetValue(ShowAreaProperty);
        set => SetValue(ShowAreaProperty, value);
    }

    public IBrush? PositiveBrush
    {
        get => GetValue(PositiveBrushProperty);
        set => SetValue(PositiveBrushProperty, value);
    }

    public IBrush? NegativeBrush
    {
        get => GetValue(NegativeBrushProperty);
        set => SetValue(NegativeBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        double[] values = Read(Source);

        // Two points is the minimum that describes a direction; anything less is drawn as
        // nothing rather than as a misleading flat line.
        if (values.Length < 2 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        double min = values[0];
        double max = values[0];

        foreach (double value in values)
        {
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        // A completely flat series has no range to scale against; centre it instead of
        // dividing by zero.
        double range = max - min;
        bool flat = range < double.Epsilon;

        double width = Bounds.Width;
        double inset = StrokeThickness;
        double height = Bounds.Height - inset * 2;
        double step = width / (values.Length - 1);

        var geometry = new StreamGeometry();

        using (StreamGeometryContext line = geometry.Open())
        {
            line.BeginFigure(Point(0), isFilled: false);

            for (int i = 1; i < values.Length; i++)
            {
                line.LineTo(Point(i));
            }

            line.EndFigure(false);
        }

        IBrush brush = Stroke
            ?? (values[^1] >= values[0]
                ? PositiveBrush ?? Brushes.MediumSeaGreen
                : NegativeBrush ?? Brushes.IndianRed);

        if (ShowArea)
        {
            var area = new StreamGeometry();

            using (StreamGeometryContext fill = area.Open())
            {
                fill.BeginFigure(new Point(0, Bounds.Height), isFilled: true);

                for (int i = 0; i < values.Length; i++)
                {
                    fill.LineTo(Point(i));
                }

                fill.LineTo(new Point(width, Bounds.Height));
                fill.EndFigure(true);
            }

            context.DrawGeometry(new SolidColorBrush(ToColor(brush), 0.14), null, area);
        }

        context.DrawGeometry(null, new Pen(brush, StrokeThickness, lineJoin: PenLineJoin.Round), geometry);

        Point Point(int index)
        {
            double normalised = flat ? 0.5 : (values[index] - min) / range;
            return new Point(index * step, inset + (1 - normalised) * height);
        }
    }

    /// <summary>Reads a series of numbers out of whatever the binding supplied.</summary>
    private static double[] Read(IEnumerable? source) => source switch
    {
        null => [],
        double[] doubles => doubles,
        int[] ints => [.. ints.Select(static value => (double)value)],
        IEnumerable<double> doubles => [.. doubles],
        IEnumerable<int> ints => [.. ints.Select(static value => (double)value)],
        IEnumerable<decimal> decimals => [.. decimals.Select(static value => (double)value)],
        _ => [],
    };

    private static Color ToColor(IBrush brush) =>
        brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;
}
