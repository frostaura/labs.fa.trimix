using Trimix.Core.Charts;

namespace Trimix.App.Controls;

/// <summary>
/// Signature motif 2 — the depth profile chart (architecture overview §11): draws Core's normalized
/// <see cref="DepthChartPath"/> geometry with Microsoft.Maui.Graphics. Inverted Y is already baked into the
/// geometry (0 = surface at top). The accessible data-table alternative lives in the page, not here.
///
/// <para><b>Every line on this canvas means something.</b> Two solid hairlines mark the surface and the deepest
/// point and are labelled by the page; exactly one dotted line is the average depth; the only other dashes are
/// the scrub crosshair. The previous two dashed gridlines at arbitrary 35 %/65 % fractions were unlabelled and
/// unexplainable, and the design spec asks for "no axes chrome, faint baseline".</para>
///
/// <para><b>No event markers.</b> They were removed from the chart entirely: the provider's own alert list —
/// gas switches, safety stops, ascent warnings, min temp, max depth — is shown on the Events section and is both
/// more authoritative and more legible than glyphs inferred from sample thresholds. <c>ProfileOverlays.Markers</c>
/// is still built (it carries the recorded deco-onset signal and is covered by tests) but nothing paints it.</para>
/// </summary>
public sealed class DepthChartDrawable : IDrawable
{
    public DepthChartPath Path { get; set; } = DepthChartPath.Empty;

    /// <summary>Normalized scrub X ∈ [0,1], or null when no finger is on the chart (C6 touch-to-scrub).</summary>
    public double? ScrubX { get; set; }

    /// <summary>Normalized crosshair Y ∈ [0,1] on the depth polyline at <see cref="ScrubX"/>.</summary>
    public double ScrubY { get; set; }

    /// <summary>
    /// Additive recorded-channel overlays (ceiling do-not-ascend zones, event markers) drawn after the depth
    /// line. Never restyles the depth line/area/gradient.
    /// </summary>
    public ProfileOverlays Overlays { get; set; } = ProfileOverlays.Empty;

    /// <summary>The dive's average depth as a dotted reference line; null when no average is known.</summary>
    public DepthReference? AverageDepth { get; set; }

    /// <summary>Line draw-in progress ∈ [0,1] (design drawLine 1.8s ease .15s; 1 when reduced motion).</summary>
    public double Progress { get; set; } = 1.0;

    /// <summary>Area-fill opacity ∈ [0,1] (design fillFade .8s ease 1.4s; 1 when reduced motion).</summary>
    public double FillOpacity { get; set; } = 1.0;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var pad = ChartMetrics.PlotPad;
        var w = dirtyRect.Width - (pad * 2);
        var h = dirtyRect.Height - (pad * 2);
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var accent = ThemeTokens.Get("Sonar");

        // Line color: in light theme LightSonar on white measures below the 3:1 WCAG 2.1 AA non-text minimum
        // the design doc commits to, so the LINE uses the darker SonarDeep token there; the area gradient keeps
        // the brighter accent. Dark theme keeps Sonar for both.
        var isLight = Application.Current?.RequestedTheme == AppTheme.Light;
        var lineColor = isLight ? ThemeTokens.Get("SonarDeep") : accent;

        // Baseline hairlines: the surface (top) and the deepest point (bottom). Solid and faint — these are the
        // two values the page labels, so they must be identifiable without being chart junk.
        canvas.StrokeSize = 1f;
        canvas.StrokeColor = ThemeTokens.Get("GlassStroke").WithAlpha(0.6f);
        canvas.StrokeDashPattern = null;
        canvas.DrawLine(pad, pad, pad + w, pad);
        canvas.DrawLine(pad, pad + h, pad + w, pad + h);

        if (Path.IsEmpty)
        {
            return;
        }

        canvas.SaveState();

        // Draw-in reveal: clip the plot from the left (skipped when Progress is already 1).
        if (Progress < 1.0)
        {
            canvas.ClipRectangle(pad, 0, (float)(w * Math.Clamp(Progress, 0, 1)), dirtyRect.Height);
        }

        // Area fill: Sonar → transparent, downward.
        var area = new PathF();
        area.MoveTo(Point(Path.Area[0], w, h));
        for (var i = 1; i < Path.Area.Count; i++)
        {
            area.LineTo(Point(Path.Area[i], w, h));
        }

        area.Close();
        var fillAlpha = 0.45f * (float)Math.Clamp(FillOpacity, 0, 1);
        canvas.SetFillPaint(
            new LinearGradientPaint(
                new PaintGradientStop[]
                {
                    new(0f, accent.WithAlpha(fillAlpha)),
                    new(1f, accent.WithAlpha(0f)),
                },
                new Point(0, 0),
                new Point(0, 1)),
            new RectF(pad, pad, w, h));
        canvas.FillPath(area);

        // Depth line: 2px Sonar.
        var line = new PathF();
        line.MoveTo(Point(Path.Line[0], w, h));
        for (var i = 1; i < Path.Line.Count; i++)
        {
            line.LineTo(Point(Path.Line[i], w, h));
        }

        canvas.StrokeSize = 2f;
        canvas.StrokeColor = lineColor;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawPath(line);

        canvas.RestoreState();

        DrawAverageDepth(canvas, pad, w, h, accent);

        // Additive recorded-channel overlays, drawn on top of the untouched depth line/area.
        DrawCeilingZones(canvas, w, h);

        DrawCrosshair(canvas, dirtyRect, pad, w, h, lineColor);
    }

    /// <summary>
    /// The average-depth reference: a true dotted line, deliberately a different dash pattern from the scrub
    /// crosshair so the two are never confused. Its alpha follows <see cref="FillOpacity"/> so it fades in with
    /// the gradient rather than sitting full-width across a half-drawn line.
    /// </summary>
    private void DrawAverageDepth(ICanvas canvas, float pad, float w, float h, Color accent)
    {
        if (AverageDepth is not { } average)
        {
            return;
        }

        var y = pad + ((float)Math.Clamp(average.Y, 0, 1) * h);
        canvas.StrokeSize = 1f;
        canvas.StrokeColor = accent.WithAlpha(0.55f * (float)Math.Clamp(FillOpacity, 0, 1));
        canvas.StrokeDashPattern = [1f, 3f];
        canvas.DrawLine(pad, y, pad + w, y);
        canvas.StrokeDashPattern = null;
    }

    private void DrawCeilingZones(ICanvas canvas, float w, float h)
    {
        if (Overlays.CeilingBands.Count == 0)
        {
            return;
        }

        // Ceiling "do-not-ascend" zones — ONE PER CONTIGUOUS RUN. A single polygon over every ceiling sample
        // would bridge the gaps between separate deco windows. Rendered as a faint tint plus a crisp edge at the
        // ceiling depth: the edge carries the meaning at a fraction of the visual weight of the old slab.
        var warn = ThemeTokens.Get("Warn");
        foreach (var band in Overlays.CeilingBands)
        {
            if (band.Upper.Count == 0 || band.Lower.Count != band.Upper.Count)
            {
                continue;
            }

            var fill = new PathF();
            fill.MoveTo(Point(band.Upper[0], w, h));
            for (var i = 1; i < band.Upper.Count; i++)
            {
                fill.LineTo(Point(band.Upper[i], w, h));
            }

            for (var i = band.Lower.Count - 1; i >= 0; i--)
            {
                fill.LineTo(Point(band.Lower[i], w, h));
            }

            fill.Close();
            canvas.FillColor = warn.WithAlpha(0.08f);
            canvas.FillPath(fill);

            var edge = new PathF();
            edge.MoveTo(Point(band.Lower[0], w, h));
            for (var i = 1; i < band.Lower.Count; i++)
            {
                edge.LineTo(Point(band.Lower[i], w, h));
            }

            canvas.StrokeColor = warn.WithAlpha(0.9f);
            canvas.StrokeSize = 1.5f;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawPath(edge);
        }

    }

    /// <summary>
    /// Touch-to-scrub crosshair: a vertical line for time AND a horizontal line for depth, meeting at a ring on
    /// the depth polyline, so one touch reads both axes at once. The vertical line is matched by the temperature
    /// lane's own crosshair at the same X, tying the two canvases to one time position.
    /// </summary>
    private void DrawCrosshair(ICanvas canvas, RectF dirtyRect, float pad, float w, float h, Color lineColor)
    {
        if (ScrubX is not { } scrubX)
        {
            return;
        }

        var sx = pad + ((float)Math.Clamp(scrubX, 0, 1) * w);
        var sy = pad + ((float)Math.Clamp(ScrubY, 0, 1) * h);

        canvas.StrokeSize = 1f;
        canvas.StrokeColor = ThemeTokens.Get("TextSecondary").WithAlpha(0.6f);
        canvas.StrokeDashPattern = [2f, 3f];
        canvas.DrawLine(sx, 0, sx, dirtyRect.Height);
        canvas.DrawLine(pad, sy, pad + w, sy);
        canvas.StrokeDashPattern = null;

        canvas.FillColor = ThemeTokens.Get("Abyss");
        canvas.FillCircle(sx, sy, 4f);
        canvas.StrokeSize = 2f;
        canvas.StrokeColor = lineColor;
        canvas.DrawCircle(sx, sy, 4f);
    }

    private static PointF Point(ChartPoint p, float w, float h) =>
        new(ChartMetrics.PlotPad + ((float)p.X * w), ChartMetrics.PlotPad + ((float)p.Y * h));
}
