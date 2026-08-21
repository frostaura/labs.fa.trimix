using System.Windows.Input;
using Trimix.App.Services;

namespace Trimix.App.Controls;

/// <summary>
/// Code-behind for the reusable bottom sheet. Consumer API:
/// <list type="bullet">
/// <item><see cref="IsOpen"/> (TwoWay by default) — drives the slide-up/down + scrim fade; every
/// dismissal path (scrim tap, ✕, swipe-down) sets it false, so a bound viewmodel flag stays true.</item>
/// <item><see cref="Title"/> — the mono eyebrow ("ADD", "SYNCING …").</item>
/// <item><see cref="SheetContent"/> — the content slot.</item>
/// <item><see cref="CloseCommand"/> — a ready-made dismiss command for buttons inside the content.</item>
/// <item><see cref="Closed"/> — raised once per dismissal, after the exit animation; Phase B hooks
/// sync-cancel here.</item>
/// </list>
/// Timings are the design's halved for a snappier shelf (slide .19s, scrim .15s); reduced motion
/// snaps instantly. Both sheets (add + sync) share this control, so this is the app-wide speed.
/// </summary>
public partial class BottomSheet : ContentView
{
    private const uint SlideMs = 190;
    private const uint ScrimMs = 150;

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(BottomSheet), false, BindingMode.TwoWay,
        propertyChanged: (b, _, newValue) => ((BottomSheet)b).OnIsOpenChanged((bool)newValue));

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(BottomSheet), "",
        propertyChanged: (b, _, newValue) => ((BottomSheet)b).TitleLabel.Text = (string?)newValue ?? "");

    public static readonly BindableProperty SheetContentProperty = BindableProperty.Create(
        nameof(SheetContent), typeof(View), typeof(BottomSheet),
        propertyChanged: (b, _, newValue) => ((BottomSheet)b).SheetSlot.Content = (View?)newValue);

    private int _transition;
    private double _panStartY;

    public BottomSheet()
    {
        InitializeComponent();
        CloseCommand = new Command(() => IsOpen = false);
    }

    /// <summary>Raised after each dismissal completes (scrim tap, ✕, swipe-down or IsOpen=false).</summary>
    public event EventHandler? Closed;

    /// <summary>Dismisses the sheet; bind cancel/close buttons inside <see cref="SheetContent"/> to it.</summary>
    public ICommand CloseCommand { get; }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public View? SheetContent
    {
        get => (View?)GetValue(SheetContentProperty);
        set => SetValue(SheetContentProperty, value);
    }

    private double OffscreenY => Math.Max(Sheet.Height, 620) + 40;

    private async void OnIsOpenChanged(bool open)
    {
        var transition = ++_transition;

        if (open)
        {
            IsVisible = true;
            InputTransparent = false;

            if (MotionSettings.IsReducedMotionEnabled)
            {
                Sheet.TranslationY = 0;
                Scrim.Opacity = 1;
                Backdrop.Opacity = 1;
                return;
            }

            Sheet.TranslationY = OffscreenY;
            await Task.WhenAll(
                Sheet.TranslateTo(0, 0, SlideMs, Easing.CubicOut),
                Scrim.FadeTo(1, ScrimMs),
                Backdrop.FadeTo(1, ScrimMs));
            return;
        }

        if (!MotionSettings.IsReducedMotionEnabled)
        {
            await Task.WhenAll(
                Sheet.TranslateTo(0, OffscreenY, SlideMs, Easing.CubicIn),
                Scrim.FadeTo(0, ScrimMs),
                Backdrop.FadeTo(0, ScrimMs));
        }
        else
        {
            Scrim.Opacity = 0;
            Backdrop.Opacity = 0;
        }

        // A reopen mid-exit wins; don't hide the sheet it is animating back in.
        if (_transition == transition && !IsOpen)
        {
            IsVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDismissRequested(object? sender, TappedEventArgs e)
    {
        // First statement — this starts the sheet's exit animation, so a hook subscribed later would tick
        // after the sheet is already moving. The generic tap hook fires a moment later and is coalesced.
        Feedback.Tap();
        IsOpen = false;
    }

    /// <summary>Drag-to-dismiss: follow the finger downward; release past the threshold closes,
    /// otherwise the sheet springs back.</summary>
    /// <summary>Drag distance past which releasing dismisses. Shared by the drag tick and the release test
    /// so the haptic can never promise a dismiss the release then does not perform.</summary>
    private double DismissThreshold => Math.Max(80, Sheet.Height * 0.25);

    private bool _dismissArmed;

    private async void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartY = Sheet.TranslationY;
                _dismissArmed = false;
                break;

            case GestureStatus.Running:
                Sheet.TranslationY = Math.Max(0, _panStartY + e.TotalY);

                // Tick as the drag CROSSES the dismiss threshold rather than on release, so the sheet feels
                // like it lets go under the finger and the diver knows it will close before committing.
                // Re-arms on the way back up, so dragging in and out of the threshold stays informative.
                var crossed = Sheet.TranslationY > DismissThreshold;
                if (crossed != _dismissArmed)
                {
                    _dismissArmed = crossed;
                    if (crossed)
                    {
                        Feedback.Tap();
                    }
                }

                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _dismissArmed = false;
                if (Sheet.TranslationY > DismissThreshold)
                {
                    IsOpen = false; // exit animation continues from the dragged position
                }
                else if (Sheet.TranslationY > 0)
                {
                    await Sheet.TranslateTo(0, 0, 90, Easing.CubicOut); // spring-back, halved to match
                }

                break;
        }
    }
}
