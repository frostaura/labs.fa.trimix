using System;
using Microsoft.Maui.Graphics;

namespace Trimix.App.Controls;

/// <summary>
/// The Trimix brand mark, alive — the app-icon sonar motif (a blip + three concentric arcs radiating
/// up-right) drawn in mint on a transparent background, with the signal pulsing OUTWARD: a brightness
/// wave travels blip → inner → middle → outer and repeats, so the icon itself reads as a live sonar
/// ping. No separate rings (the icon already IS the sonar — wrapping it in more rings just collides).
/// Centred on its own content bounds; honours reduced motion (static, icon-accurate dimming).
/// </summary>
public sealed class TrimixMark : GraphicsView
{
    private const string AnimationHandle = "trimix.markPing";
    private const uint PeriodMs = 2600;

    private double _phase;

    public TrimixMark()
    {
        Drawable = new MarkDrawable(this);
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;

        Loaded += (_, _) => Start();
        Unloaded += (_, _) => this.AbortAnimation(AnimationHandle);
    }

    private void Start()
    {
        this.AbortAnimation(AnimationHandle);
        if (MotionSettings.IsReducedMotionEnabled)
        {
            _phase = 0;
            Invalidate();
            return;
        }

        new Animation(t =>
        {
            _phase = t;
            Invalidate();
        }).Commit(
            this, AnimationHandle, rate: 16, length: PeriodMs, Easing.Linear,
            repeat: () => Handler is not null && !MotionSettings.IsReducedMotionEnabled);
    }

    private sealed class MarkDrawable(TrimixMark owner) : IDrawable
    {
        // App-icon foreground geometry (appiconfg.svg, 456 canvas): blip + three arcs sweeping
        // 15° → -105° (through the up-right diagonal) about the blip at (168,288).
        private const float OriginX = 168f;
        private const float OriginY = 288f;
        private const float StartDeg = 15f;
        private const float EndDeg = -105f;

        // Content bounding box within the 456 canvas (to centre the mark).
        private const float ContentCx = 213.25f;
        private const float ContentCy = 242.75f;
        private const float ContentSize = 156.7f;

        // Where each element sits along the outgoing signal (0 = source … 1 = far), and its icon-accurate
        // resting brightness so the static (reduced-motion) render matches the app icon's depth fade.
        private const float BaseAlpha = 0.32f;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var mint = ThemeTokens.Get("Sonar");
            var scale = Math.Min(dirtyRect.Width, dirtyRect.Height) * 0.9f / ContentSize;
            if (scale <= 0)
            {
                return;
            }

            var reduced = MotionSettings.IsReducedMotionEnabled;

            canvas.SaveState();
            canvas.Antialias = true;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.Translate(dirtyRect.Center.X, dirtyRect.Center.Y);
            canvas.Scale(scale, scale);
            canvas.Translate(-ContentCx, -ContentCy);

            // Blip — the ping source; a soft flash as the wave departs.
            canvas.FillColor = mint.WithAlpha(reduced ? 1f : Brightness(pos: 0.0f, restAlpha: 1.0f));
            canvas.FillCircle(OriginX, OriginY, 14f);

            // Three arcs; brightness travels outward. Resting alphas mirror the icon (100 / 70 / 45%).
            DrawArc(canvas, mint, radius: 48f, strokeWidth: 18f, pos: 0.30f, restAlpha: 1.0f, reduced);
            DrawArc(canvas, mint, radius: 88f, strokeWidth: 15f, pos: 0.55f, restAlpha: 0.7f, reduced);
            DrawArc(canvas, mint, radius: 128f, strokeWidth: 12f, pos: 0.80f, restAlpha: 0.45f, reduced);

            canvas.RestoreState();
        }

        /// <summary>Alpha for an element at signal-position <paramref name="pos"/>: rests at its icon
        /// value, brightening to full as the outward wave passes over it (raised-cosine window).</summary>
        private float Brightness(float pos, float restAlpha)
        {
            var d = MathF.Abs((float)owner._phase - pos);
            d = MathF.Min(d, 1f - d); // wrap-around distance
            const float window = 0.34f;
            var bump = d < window ? 0.5f * (1f + MathF.Cos(MathF.PI * d / window)) : 0f;
            var a = restAlpha + ((1f - restAlpha) * bump);
            return Math.Clamp(a, 0f, 1f);
        }

        private void DrawArc(ICanvas canvas, Color mint, float radius, float strokeWidth, float pos, float restAlpha, bool reduced)
        {
            const int steps = 30;
            var path = new PathF();
            for (var i = 0; i <= steps; i++)
            {
                var deg = StartDeg + ((EndDeg - StartDeg) * i / steps);
                var rad = deg * MathF.PI / 180f;
                var x = OriginX + (radius * MathF.Cos(rad));
                var y = OriginY + (radius * MathF.Sin(rad)); // y-down canvas
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            canvas.StrokeColor = mint.WithAlpha(reduced ? restAlpha : MathF.Max(BaseAlpha, Brightness(pos, restAlpha)));
            canvas.StrokeSize = strokeWidth;
            canvas.DrawPath(path);
        }
    }
}
