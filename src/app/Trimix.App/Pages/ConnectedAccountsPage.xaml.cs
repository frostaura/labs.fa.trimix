using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// Providers (prototype S5). Binds the registry-driven <see cref="ConnectedAccountsViewModel"/>.
/// The optional <c>nudge</c> query parameter (an unlinked "Sync now" tap routed here) flashes the
/// Connect buttons red via the VM's nudge state.
/// </summary>
[QueryProperty(nameof(Nudge), Routes.Params.Nudge)]
public partial class ConnectedAccountsPage : ContentPage
{
    private readonly ConnectedAccountsViewModel _viewModel;
    private bool _nudgeRequested;

    public ConnectedAccountsPage(ConnectedAccountsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>"1" when arrival should flash the Connect buttons (set by Shell query).</summary>
    public string? Nudge
    {
        set => _nudgeRequested = value is "1";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
        if (_nudgeRequested)
        {
            _nudgeRequested = false;
            await _viewModel.BeginNudgeAsync();
        }
    }
}
