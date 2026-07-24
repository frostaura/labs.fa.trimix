using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

public partial class PaywallPage : ContentPage
{
    private readonly PaywallViewModel _viewModel;

    public PaywallPage(PaywallViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void OnDismissClicked(object? sender, EventArgs e)
    {
        // Belt-and-suspenders with the XAML IsEnabled guard: never dismiss while a purchase is in
        // flight, or the VM's trailing GoBack would pop the page beneath the (already-popped) paywall.
        if (_viewModel.IsBusy)
        {
            return;
        }

        await Shell.Current.GoToAsync("..");
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
