namespace Trimix.App.Controls;

/// <summary>
/// Signature motif 1 — "ping rings": concentric expanding circles (Sonar at 12–4% opacity),
/// used for BLE scanning, the logbook empty state, and the paywall hero. ≤3 rings, 2.4s period.
/// Honors platform reduced-motion: when reduced, renders three static rings instead of animating.
/// Purely decorative — excluded from the accessibility tree.
/// </summary>
public sealed class PingRingsView : GraphicsView
{
    private readonly PingRingsDrawable _drawable = new();
    private IDispatcherTimer? _timer;

    public PingRingsView()
    {
        Drawable = _drawable;
        InputTransparent = true;
        SemanticProperties.SetDescription(this, null);
        AutomationProperties.SetIsInAccessibleTree(this, false);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (MotionSettings.IsReducedMotionEnabled)
        {
            _drawable.Static = true;
            Invalidate();
            return;
        }

        _drawable.Static = false;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(40);
        _timer.Tick += (_, _) =>
        {
            // 2.4s period per the design language.
            _drawable.Phase = (_drawable.Phase + 40.0 / 2400.0) % 1.0;
            Invalidate();
        };
        _timer.Start();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _timer?.Stop();
        _timer = null;
    }

    private sealed class PingRingsDrawable : IDrawable
    {
        private const int RingCount = 3;

        public double Phase { get; set; }

        public bool Static { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var accent = ThemeTokens.Get("Sonar");
            var cx = dirtyRect.Center.X;
            var cy = dirtyRect.Center.Y;
            var maxRadius = Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f - 2f;
            if (maxRadius <= 0)
            {
                return;
            }

            canvas.StrokeSize = 1.75f;
            for (var i = 0; i < RingCount; i++)
            {
                // Each ring is a phase-shifted copy of the expanding pulse.
                var t = Static
                    ? (i + 1) / (double)RingCount
                    : (Phase + (double)i / RingCount) % 1.0;
                var radius = (float)(t * maxRadius);
                if (radius < 1f)
                {
                    continue;
                }

                // Opacity fades 12% → 4% as the ring expands (design token usage).
                var opacity = (float)(0.12 - 0.08 * t);
                canvas.StrokeColor = accent.WithAlpha(Math.Max(0.04f, opacity));
                canvas.DrawCircle(cx, cy, radius);
            }

            // Nucleus dot anchors the motif.
            canvas.FillColor = accent.WithAlpha(0.9f);
            canvas.FillCircle(cx, cy, 4f);
        }
    }
}
