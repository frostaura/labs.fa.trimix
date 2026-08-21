using Trimix.Core.Charts;

namespace Trimix.App.Controls;

/// <summary>
/// The temperature mini-lane drawn directly under the depth chart, sharing its time axis.
///
/// <para>Temperature used to be a secondary line ON the depth canvas, autoscaled to its own observed range.
/// Because dive computers report whole °C, that turned a 2 °C dive into a full-height binary staircase and made
/// it the loudest thing on the chart. Giving it its own short lane — with a padded axis and its own labels —
/// keeps the information without letting it compete with the depth trace.</para>
///
/// <para>A separate drawable rather than a parameterised <see cref="DepthChartDrawable"/> on purpose: this lane
/// has no draw-in animation, no markers, no ceiling and a value-up axis, so sharing one class would mean a
/// layer-enable flag for every difference. Only the plot arithmetic is shared, via <see cref="ChartMetrics"/>.</para>
/// </summary>
public sealed class TemperatureLaneDrawable : IDrawable
{
    public TemperatureLane? Lane { get; set; }

    /// <summary>Normalized scrub X ∈ [0,1] — the SAME value the depth chart uses, so the two align.</summary>
    public double? ScrubX { get; set; }

    /// <summary>Normalized scrub Y ∈ [0,1] on the temperature curve at <see cref="ScrubX"/>.</summary>
    public double ScrubY { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // X uses the main chart's pad so the lane's time axis lines up with the depth chart's exactly; Y uses a
        // tighter inset because this lane has no marker gutter to leave room for.
        var padX = ChartMetrics.PlotPad;
        var padY = ChartMetrics.LanePad;
        var w = dirtyRect.Width - (padX * 2);
        var h = dirtyRect.Height - (padY * 2);
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var floor = padY + h;

        // Faint floor hairline, matching the depth chart's baselines so the two canvases read as one system.
        canvas.StrokeSize = 1f;
        canvas.StrokeColor = ThemeTokens.Get("GlassStroke").WithAlpha(0.6f);
        canvas.StrokeDashPattern = null;
        canvas.DrawLine(padX, floor, padX + w, floor);

        if (Lane is not { } lane || lane.Points.Count < 2)
        {
            return;
        }

        var ice = ThemeTokens.Get("Ice");

        PointF Point(ChartPoint p) =>
            new(padX + ((float)Math.Clamp(p.X, 0, 1) * w), padY + ((float)Math.Clamp(p.Y, 0, 1) * h));

        // Fill from the curve down to the floor — a quieter sibling of the depth chart's mint gradient.
        var fill = new PathF();
        fill.MoveTo(padX, floor);
        foreach (var point in lane.Points)
        {
            var p = Point(point);
            fill.LineTo(p.X, p.Y);
        }

        fill.LineTo(padX + w, floor);
        fill.Close();

        canvas.SetFillPaint(
            new LinearGradientPaint(
                new PaintGradientStop[]
                {
                    new(0f, ice.WithAlpha(0.14f)),
                    new(1f, ice.WithAlpha(0f)),
                },
                new Point(0, 0),
                new Point(0, 1)),
            new RectF(padX, padY, w, h));
        canvas.FillPath(fill);

        var line = new PathF();
        var first = Point(lane.Points[0]);
        line.MoveTo(first.X, first.Y);
        for (var i = 1; i < lane.Points.Count; i++)
        {
            var p = Point(lane.Points[i]);
            line.LineTo(p.X, p.Y);
        }

        canvas.StrokeColor = ice;
        canvas.StrokeSize = 1.5f;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawPath(line);

        // The shared crosshair. Same paint as the depth chart's so one finger reads as one time position across
        // both canvases, plus a horizontal at the temperature and a ring on the curve.
        if (ScrubX is { } scrubX)
        {
            var sx = padX + ((float)Math.Clamp(scrubX, 0, 1) * w);
            var sy = padY + ((float)Math.Clamp(ScrubY, 0, 1) * h);

            canvas.StrokeSize = 1f;
            canvas.StrokeColor = ThemeTokens.Get("TextSecondary").WithAlpha(0.6f);
            canvas.StrokeDashPattern = [2f, 3f];
            canvas.DrawLine(sx, 0, sx, dirtyRect.Height);
            canvas.DrawLine(padX, sy, padX + w, sy);
            canvas.StrokeDashPattern = null;

            canvas.FillColor = ThemeTokens.Get("Abyss");
            canvas.FillCircle(sx, sy, 3f);
            canvas.StrokeSize = 1.5f;
            canvas.StrokeColor = ice;
            canvas.DrawCircle(sx, sy, 3f);
        }
    }
}
