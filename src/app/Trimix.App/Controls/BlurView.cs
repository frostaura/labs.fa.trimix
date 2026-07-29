namespace Trimix.App.Controls;

/// <summary>
/// A native backdrop-blur view. Placed as the backdrop of a <see cref="BottomSheet"/> so the content
/// behind the sheet reads as frosted glass, not merely dimmed. iOS renders a real
/// <c>UIVisualEffectView</c> (dark material) that blurs whatever is behind the transparent host modal
/// — the logbook and the tab bar. Android has no cheap live backdrop blur, so its handler is a no-op
/// and the scrim carries the frost there. Reduced-motion / capability differences are handled in the
/// platform handlers (see Platforms/iOS + Platforms/Android BlurViewHandler).
/// </summary>
public sealed class BlurView : View
{
}
