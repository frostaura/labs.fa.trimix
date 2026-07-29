using Trimix.Core.Charts;

namespace Trimix.App.Controls;

/// <summary>
/// Signature motif 2 — the depth profile chart (architecture overview §11): draws Core's
/// normalized <see cref="DepthChartPath"/> geometry with Microsoft.Maui.Graphics. Inverted Y is
/// already baked into the geometry (0 = surface at top). Sonar 2px line, vertical Sonar→transparent
/// gradient fill, GlassStroke grid, no chart junk. The accessible data-table alternative lives in
/// the page, not here.
/// </summary>
public sealed class DepthChartDrawable : IDrawable
{
    private const float Pad = 8f;

    /// <summary>The two dashed depth gridlines (prototype C6): 35% and 65% of the plot height —
    /// the page's right-hand depth labels align to the same fractions.</summary>
    private static readonly float[] GridFractions = [0.35f, 0.65f];

    public DepthChartPath Path { get; set; } = DepthChartPath.Empty;

    /// <summary>Normalized scrub X ∈ [0,1], or null when no finger is on the chart (C6 touch-to-scrub).</summary>
    public double? ScrubX { get; set; }

    /// <summary>Normalized crosshair Y ∈ [0,1] on the depth polyline at <see cref="ScrubX"/>.</summary>
    public double ScrubY { get; set; }

    /// <summary>
    /// Additive recorded-channel overlays (temperature/tank secondary lines, ceiling do-not-ascend
    /// band, event markers) drawn after the depth line. Never restyles the depth line/area/gradient.
    /// </summary>
    public ProfileOverlays Overlays { get; set; } = ProfileOverlays.Empty;

    /// <summary>Line draw-in progress ∈ [0,1] (design drawLine 1.8s ease .15s; 1 when reduced motion).</summary>
    public double Progress { get; set; } = 1.0;

    /// <summary>Area-fill opacity ∈ [0,1] (design fillFade .8s ease 1.4s; 1 when reduced motion).</summary>
    public double FillOpacity { get; set; } = 1.0;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var w = dirtyRect.Width - Pad * 2;
        var h = dirtyRect.Height - Pad * 2;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var grid = ThemeTokens.Get("GlassStroke");
        var accent = ThemeTokens.Get("Sonar");

        // Line color: in light theme LightSonar (#2AA8C4) on white measures only 2.80:1 — below the
        // 3:1 WCAG 2.1 AA non-text minimum the design doc commits to. Use the darker SonarDeep token
        // (LightSonarDeep #1E7A91, ~4.6:1 on white) for the LINE in light theme; the area gradient
        // keeps the brighter accent. Dark theme keeps Sonar for both.
        var isLight = Application.Current?.RequestedTheme == AppTheme.Light;
        var lineColor = isLight ? ThemeTokens.Get("SonarDeep") : accent;

        // Grid: exactly two dashed horizontal depth lines (prototype C6; quiet, instrument-like).
        canvas.StrokeSize = 1f;
        canvas.StrokeColor = grid;
        canvas.StrokeDashPattern = [3f, 4f];
        foreach (var fraction in GridFractions)
        {
            var y = Pad + h * fraction;
            canvas.DrawLine(Pad, y, Pad + w, y);
        }

        canvas.StrokeDashPattern = null;

        if (Path.IsEmpty)
        {
            return;
        }

        canvas.SaveState();

        // Draw-in reveal: clip the plot from the left (skipped when Progress is already 1).
        if (Progress < 1.0)
        {
            canvas.ClipRectangle(Pad, 0, (float)(w * Math.Clamp(Progress, 0, 1)), dirtyRect.Height);
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
            new RectF(Pad, Pad, w, h));
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

        // Additive recorded-channel overlays, drawn on top of the untouched depth line/area.
        DrawOverlays(canvas, w, h);

        // Touch-to-scrub crosshair (C6): dashed vertical line + a ring on the depth polyline at
        // the touched point. Drawn last so it rides above every channel.
        if (ScrubX is { } scrubX)
        {
            var sx = Pad + (float)Math.Clamp(scrubX, 0, 1) * w;
            var sy = Pad + (float)Math.Clamp(ScrubY, 0, 1) * h;

            canvas.StrokeSize = 1f;
            canvas.StrokeColor = ThemeTokens.Get("TextSecondary").WithAlpha(0.6f);
            canvas.StrokeDashPattern = [2f, 3f];
            canvas.DrawLine(sx, 0, sx, dirtyRect.Height);
            canvas.StrokeDashPattern = null;

            canvas.FillColor = ThemeTokens.Get("Abyss");
            canvas.FillCircle(sx, sy, 4f);
            canvas.StrokeSize = 2f;
            canvas.StrokeColor = lineColor;
            canvas.DrawCircle(sx, sy, 4f);
        }
    }

    private void DrawOverlays(ICanvas canvas, float w, float h)
    {
        if (!Overlays.HasAny)
        {
            return;
        }

        // Ceiling "do-not-ascend" zone: translucent Warn fill between surface (Upper) and ceiling (Lower).
        if (Overlays.CeilingBand is { } band && band.Upper.Count > 0 && band.Lower.Count == band.Upper.Count)
        {
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
            canvas.FillColor = ThemeTokens.Get("Warn").WithAlpha(0.18f);
            canvas.FillPath(fill);
        }

        // Secondary channels on the right axis: 1.5px lines (temperature = Ice, tank = TextSecondary).
        DrawSeries(canvas, w, h, Overlays.Temperature, ThemeTokens.Get("Ice"));
        DrawSeries(canvas, w, h, Overlays.TankPressure, ThemeTokens.Get("TextSecondary"));

        // Recorded event markers: each KIND gets a distinct SHAPE (not just a hue), so a colorblind /
        // grayscale user can tell a benign gas switch (dot) from a setpoint change (diamond) from a
        // deco-onset (down triangle) from an ascent warning (up chevron). WCAG 1.4.1 — colour is only a
        // secondary cue (hazard = Warn, benign = Sonar); shape carries the distinction.
        foreach (var marker in Overlays.Markers)
        {
            var hazard = marker.Kind is ChartMarkerKind.DecoOnset or ChartMarkerKind.AscentWarning;
            var color = ThemeTokens.Get(hazard ? "Warn" : "Sonar");
            var p = Point(new ChartPoint(marker.X, marker.Y), w, h);
            DrawMarkerGlyph(canvas, marker.Kind, p, color);
        }
    }

    /// <summary>Draws a kind-distinct glyph so markers are never encoded by colour alone (WCAG 1.4.1).</summary>
    private static void DrawMarkerGlyph(ICanvas canvas, ChartMarkerKind kind, PointF p, Color color)
    {
        const float r = 5f;
        canvas.StrokeColor = color;
        canvas.FillColor = color;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeLineCap = LineCap.Round;

        switch (kind)
        {
            case ChartMarkerKind.GasSwitch: // benign: filled dot
                canvas.FillCircle(p.X, p.Y, r - 1f);
                break;
            case ChartMarkerKind.Setpoint: // benign: diamond
                var diamond = new PathF();
                diamond.MoveTo(p.X, p.Y - r);
                diamond.LineTo(p.X + r, p.Y);
                diamond.LineTo(p.X, p.Y + r);
                diamond.LineTo(p.X - r, p.Y);
                diamond.Close();
                canvas.FillPath(diamond);
                break;
            case ChartMarkerKind.DecoOnset: // hazard: down triangle
                var triangle = new PathF();
                triangle.MoveTo(p.X - r, p.Y - r);
                triangle.LineTo(p.X + r, p.Y - r);
                triangle.LineTo(p.X, p.Y + r);
                triangle.Close();
                canvas.FillPath(triangle);
                break;
            case ChartMarkerKind.AscentWarning: // hazard: up chevron (rising)
                canvas.StrokeSize = 2f;
                canvas.DrawLine(p.X - r, p.Y + r, p.X, p.Y - r);
                canvas.DrawLine(p.X, p.Y - r, p.X + r, p.Y + r);
                break;
        }
    }

    private static void DrawSeries(ICanvas canvas, float w, float h, ChartSeries? series, Color color)
    {
        if (series is null || series.Points.Count < 2)
        {
            return;
        }

        var path = new PathF();
        path.MoveTo(Point(series.Points[0], w, h));
        for (var i = 1; i < series.Points.Count; i++)
        {
            path.LineTo(Point(series.Points[i], w, h));
        }

        canvas.StrokeColor = color;
        canvas.StrokeSize = 1.5f;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawPath(path);
    }

    private static PointF Point(ChartPoint p, float w, float h) =>
        new(Pad + (float)p.X * w, Pad + (float)p.Y * h);
}
