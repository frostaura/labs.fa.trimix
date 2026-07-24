using System.ComponentModel;
using Trimix.App.Controls;
using Trimix.Contracts.Dtos;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// Settings → Connected accounts. Binds the registry-driven <see cref="ConnectedAccountsViewModel"/> and hosts
/// the MySSI QR render surface. The QR is fixed dark-on-light for scannability, fed from the viewmodel's
/// <c>SsiQrMatrix</c> via a <see cref="QrCodeDrawable"/> and redrawn when it changes. An optional
/// <c>dive</c> query parameter lets a dive-scoped caller navigate here to export a specific dive as a QR.
/// </summary>
[QueryProperty(nameof(IncomingDive), "dive")]
public partial class ConnectedAccountsPage : ContentPage
{
    private readonly ConnectedAccountsViewModel _viewModel;
    private readonly QrCodeDrawable _qrDrawable = new();

    public ConnectedAccountsPage(ConnectedAccountsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        QrView.Drawable = _qrDrawable;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Optional dive to export as a MySSI QR when this page is opened from a dive-scoped surface.</summary>
    public DiveDto? IncomingDive
    {
        set
        {
            if (value is not null)
            {
                _viewModel.ShowSsiQrCommand.Execute(value);
            }
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectedAccountsViewModel.SsiQrMatrix))
        {
            _qrDrawable.Matrix = _viewModel.SsiQrMatrix;
            QrView.Invalidate();
        }
    }
}
