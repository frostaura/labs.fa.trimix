using System.ComponentModel;
using System.Globalization;
using Trimix.App.Controls;
using Trimix.Contracts;
using Trimix.Core;
using Trimix.Core.Abstractions;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// The purchase moment (prototype S8) + Unlocked (S9). The optional <c>pendingDives</c> query
/// parameter (set when a sync run hits the free cap) feeds the "N more dives are waiting on your
/// providers" sub-copy. Motion: the SKU cards enter with the design's <c>swingUp</c> (perspective
/// rotateX 58° → 0, ~.5s); flipping to the Unlocked state runs a screenIn + check-disc pop —
/// state is always shown by animation, never a toast.
/// </summary>
[QueryProperty(nameof(PendingDives), Routes.Params.PendingDives)]
public partial class PaywallPage : TrimixPage
{
    private readonly PaywallViewModel _viewModel;
    private bool _swingUpRan;

    public PaywallPage(PaywallViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Count of dives left waiting on providers by the free cap (Shell query string).</summary>
    public string? PendingDives
    {
        set
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pending))
            {
                _viewModel.PendingDives = pending;
            }
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);

        if (!_viewModel.PurchaseSucceeded && !_swingUpRan)
        {
            _swingUpRan = true;
            RunSwingUp();
        }
    }

    /// <summary>swingUp (design): opacity 0→1 while rotateX swings 58° → −8° → 0 over ~.5s.</summary>
    private void RunSwingUp()
    {
        if (MotionSettings.IsReducedMotionEnabled)
        {
            SkuCards.Opacity = 1;
            SkuCards.RotationX = 0;
            return;
        }

        SkuCards.Opacity = 0;
        SkuCards.RotationX = 58;

        var swing = new Animation();
        swing.Add(0.00, 0.60, new Animation(v => SkuCards.RotationX = v, 58, -8, Easing.SinOut));
        swing.Add(0.60, 1.00, new Animation(v => SkuCards.RotationX = v, -8, 0, Easing.SinInOut));
        swing.Add(0.00, 0.60, new Animation(v => SkuCards.Opacity = v, 0, 1, Easing.SinOut));
        swing.Commit(this, "trimix.paywallSwingUp", rate: 16, length: 500, Easing.Linear);
    }

    /// <summary>Unlocked (S9) entrance: screenIn on the whole state + a spring pop on the check disc.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PaywallViewModel.PurchaseSucceeded) || !_viewModel.PurchaseSucceeded)
        {
            return;
        }

        if (MotionSettings.IsReducedMotionEnabled)
        {
            UnlockedRoot.Opacity = 1;
            CheckDisc.Scale = 1;
            return;
        }

        _ = Motion.ScreenInAsync(UnlockedRoot);
        CheckDisc.Scale = 0.4;
        _ = CheckDisc.ScaleTo(1, 500, Easing.SpringOut);
    }
}

/// <summary>
/// Picks the SKU card template: the Lifetime card carries the PaywallGold accent (design doc:
/// "PaywallGold — Lifetime SKU highlight only"). Keyed on the frozen <see cref="Skus"/> constants.
/// </summary>
public sealed class PaywallSkuTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ProTemplate { get; set; }

    public DataTemplate? LifetimeTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        item is StoreProduct product && product.SkuId == Skus.Lifetime
            ? LifetimeTemplate!
            : ProTemplate!;
}
