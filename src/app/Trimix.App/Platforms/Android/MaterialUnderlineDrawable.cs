using Android.Graphics.Drawables;
// Aliased: MAUI's global usings bring Microsoft.Maui.Graphics.Paint/Color/Canvas into scope, and this
// file needs the Android ones.
using APaint = Android.Graphics.Paint;
using AColor = Android.Graphics.Color;
using ACanvas = Android.Graphics.Canvas;
using AColorFilter = Android.Graphics.ColorFilter;
using AFormat = Android.Graphics.Format;

namespace Trimix.App.Platforms.Android;

/// <summary>
/// The Material bottom indicator for a text field, drawn as the EditText's background: a hairline at rest
/// that thickens and turns accent-coloured on focus.
///
/// <para><b>Why a custom drawable rather than a tint.</b> The obvious move — tinting the platform
/// EditText background via <c>BackgroundTintList</c> — is wrong on a Material3 theme, where that background
/// is a FILLED box rather than the old underline. Tinting it paints a solid rectangle inside the GlassCard
/// that already surrounds the field, which is exactly the doubled-container look that was being fixed.
/// Owning the drawable is the only way to get an underline and nothing else, on any host theme.</para>
///
/// <para>State handling is the drawable's own job (<see cref="IsStateful"/> +
/// <see cref="OnStateChange"/>), so focus response needs no focus-changed listener on the field and
/// survives the platform recycling the view.</para>
/// </summary>
internal sealed class MaterialUnderlineDrawable : Drawable
{
    private const float RestDip = 1f;
    private const float FocusedDip = 2f;

    private readonly APaint _paint = new() { AntiAlias = true };
    private readonly float _density;
    private readonly AColor _idle;
    private readonly AColor _accent;
    private readonly AColor _disabled;

    private bool _isFocused;
    private bool _isEnabled = true;

    public MaterialUnderlineDrawable(float density, AColor idle, AColor accent, AColor disabled)
    {
        _density = density <= 0 ? 1f : density;
        _idle = idle;
        _accent = accent;
        _disabled = disabled;
        _paint.SetStyle(APaint.Style.Fill);
    }

    /// <summary>Tells the framework to route state changes here instead of ignoring them.</summary>
    public override bool IsStateful => true;

    public override int Opacity => (int)AFormat.Translucent;

    protected override bool OnStateChange(int[]? state)
    {
        var focused = false;
        var enabled = false;

        foreach (var attribute in state ?? [])
        {
            if (attribute == global::Android.Resource.Attribute.StateFocused)
            {
                focused = true;
            }
            else if (attribute == global::Android.Resource.Attribute.StateEnabled)
            {
                enabled = true;
            }
        }

        if (focused == _isFocused && enabled == _isEnabled)
        {
            return false;
        }

        _isFocused = focused;
        _isEnabled = enabled;
        InvalidateSelf();
        return true;
    }

    public override void Draw(ACanvas canvas)
    {
        var bounds = Bounds;
        var thickness = (_isFocused ? FocusedDip : RestDip) * _density;

        _paint.Color = !_isEnabled ? _disabled : _isFocused ? _accent : _idle;
        canvas.DrawRect(bounds.Left, bounds.Bottom - thickness, bounds.Right, bounds.Bottom, _paint);
    }

    public override void SetAlpha(int alpha) => _paint.Alpha = alpha;

    public override void SetColorFilter(AColorFilter? colorFilter) => _paint.SetColorFilter(colorFilter);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _paint.Dispose();
        }

        base.Dispose(disposing);
    }
}
