using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

public partial class AuthPage : ContentPage
{
    public AuthPage(AuthViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
