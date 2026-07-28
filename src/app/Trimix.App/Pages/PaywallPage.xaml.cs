using System.Globalization;
using Trimix.Contracts;
using Trimix.Core;
using Trimix.Core.Abstractions;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// The purchase moment (prototype S8) + Unlocked (S9). The optional <c>pendingDives</c> query
/// parameter (set when a sync run hits the free cap) feeds the "N more dives are waiting on your
/// providers" sub-copy.
/// </summary>
[QueryProperty(nameof(PendingDives), Routes.Params.PendingDives)]
public partial class PaywallPage : ContentPage
{
    private readonly PaywallViewModel _viewModel;

    public PaywallPage(PaywallViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
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
