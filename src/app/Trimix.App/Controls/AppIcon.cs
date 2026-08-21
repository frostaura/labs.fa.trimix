using Microsoft.Maui.Graphics;

namespace Trimix.App.Controls;

/// <summary>
/// Renders one named vector icon from <c>Resources/Styles/Icons.xaml</c> — the single icon system
/// (design: inline SVG, stroke ~1.6, round caps, mint). Replaces every emoji glyph and bespoke
/// shape stack app-wide.
///
/// <para>Usage: <c>&lt;controls:AppIcon Icon="sync" Size="20" /&gt;</c>. <see cref="Icon"/> is the
/// lowercase icon name ("sync" resolves resource key "IconSync"). Scaling is SVG-semantics: the
/// 24-unit viewbox (geometry AND stroke) scales uniformly to <see cref="Size"/>, exactly like the
/// design's <c>&lt;svg viewBox="0 0 24 24" width=…&gt;</c>. <see cref="Color"/> defaults to the
/// themed mint (Sonar); <see cref="StrokeThickness"/> is in viewbox units (design default 1.6).</para>
///
/// <para>Implementation note: this draws through <see cref="GraphicsView"/> + a canvas scale rather
/// than a scaled <c>Shapes.Path</c> view — view-level <c>Scale</c>/anchor transforms on MAUI Shapes
/// proved unreliable on Android (glyphs rendered unscaled and top-left anchored), and canvas
/// transforms are deterministic on both platforms.</para>
/// </summary>
public sealed class AppIcon : GraphicsView
{
    /// <summary>The design viewbox every icon in Icons.xaml is authored in.</summary>
    public const double ViewBox = 24;

    private static readonly Dictionary<string, PathF?> PathCache = [];

    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(string), typeof(AppIcon), null, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size), typeof(double), typeof(AppIcon), 20.0, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color), typeof(Color), typeof(AppIcon), null, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty StrokeThicknessProperty = BindableProperty.Create(
        nameof(StrokeThickness), typeof(double), typeof(AppIcon), 1.6, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty CenterInkProperty = BindableProperty.Create(
        nameof(CenterInk), typeof(bool), typeof(AppIcon), false, propertyChanged: OnVisualChanged);

    public AppIcon()
    {
        Drawable = new IconDrawable(this);
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        Apply();
    }

    /// <summary>Lowercase icon name — "logbook", "you", "sync", "plus", "gear", "pencil",
    /// "lock", "warning", "back", "chevron", "check", "trend", "fingerprint".</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Rendered square size in device-independent units (the SVG width/height).</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Stroke color; null uses the themed mint (Sonar token).</summary>
    public Color? Color
    {
        get => (Color?)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Stroke width in 24-unit viewbox units (scales with <see cref="Size"/>).</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Draws the glyph centred on its own ink rather than on the 24-unit viewbox origin. Set this when the
    /// icon is <b>rotated</b>: MAUI rotates a view about its bounds centre, so any icon whose ink is not
    /// centred in the viewbox orbits that pivot instead of spinning in place.
    ///
    /// <para>The <c>sync</c> radar is exactly this case — its arc is centred on (12,12) but the arrowhead
    /// reaches y=3, putting the ink centre at (12, 11.5), half a viewbox unit high. At Size=17 that is
    /// ~0.35 dip of eccentricity: a visible wobble once per revolution.</para>
    ///
    /// <para>Opt-in on purpose: the statically-placed icons are positioned against the viewbox today, so
    /// re-centring them all would shift existing layouts for no benefit.</para>
    /// </summary>
    public bool CenterInk
    {
        get => (bool)GetValue(CenterInkProperty);
        set => SetValue(CenterInkProperty, value);
    }

    private static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((AppIcon)bindable).Apply();

    private void Apply()
    {
        WidthRequest = Size;
        HeightRequest = Size;
        Invalidate();
    }

    private static PathF? ResolvePath(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        var key = "Icon" + char.ToUpperInvariant(icon[0]) + icon[1..];
        if (PathCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        PathF? path = null;
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is string data)
        {
            try
            {
                path = PathBuilder.Build(data);
            }
            catch
            {
                path = null; // malformed data renders empty rather than crashing a page
            }
        }

        PathCache[key] = path; // unknown names cache null and render empty
        return path;
    }

    private sealed class IconDrawable(AppIcon owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var path = ResolvePath(owner.Icon);
            if (path is null || owner.Size <= 0)
            {
                return;
            }

            canvas.SaveState();
            canvas.StrokeColor = owner.Color ?? ThemeTokens.Get("Sonar");
            canvas.StrokeSize = (float)owner.StrokeThickness;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.Antialias = true;

            // SVG semantics: uniform scale of geometry AND stroke from the 24-unit viewbox.
            var scale = (float)(owner.Size / ViewBox);
            canvas.Scale(scale, scale);

            if (owner.CenterInk)
            {
                // Shift the glyph so its ink centre lands on the viewbox centre, making the view's own
                // bounds centre a wobble-free rotation pivot. Path bounds are the right measure here:
                // strokes are round-capped and therefore extend symmetrically, so the path's centre is
                // the ink's centre.
                var ink = path.GetBoundsByFlattening();
                canvas.Translate(
                    (float)(ViewBox / 2) - ink.Center.X,
                    (float)(ViewBox / 2) - ink.Center.Y);
            }

            canvas.DrawPath(path);
            canvas.RestoreState();
        }
    }
}
