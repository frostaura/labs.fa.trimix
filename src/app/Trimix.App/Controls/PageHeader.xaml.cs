using System.Windows.Input;
using Trimix.Core.Abstractions;

namespace Trimix.App.Controls;

/// <summary>
/// Code-behind for the shared page header. Bindables: <see cref="Title"/>, <see cref="ShowBack"/> +
/// <see cref="BackCommand"/> (defaults to <see cref="INavigator.GoBackAsync"/> when unset), and the
/// <see cref="RightAction"/> view slot. Reference adoption: Pages/SettingsPage.xaml.
/// </summary>
public partial class PageHeader : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(PageHeader), "");

    public static readonly BindableProperty ShowBackProperty = BindableProperty.Create(
        nameof(ShowBack), typeof(bool), typeof(PageHeader), false);

    public static readonly BindableProperty BackCommandProperty = BindableProperty.Create(
        nameof(BackCommand), typeof(ICommand), typeof(PageHeader));

    public static readonly BindableProperty RightActionProperty = BindableProperty.Create(
        nameof(RightAction), typeof(View), typeof(PageHeader),
        propertyChanged: (b, _, newValue) => ((PageHeader)b).RightSlot.Content = (View?)newValue);

    public PageHeader() => InitializeComponent();

    /// <summary>The page title (Title24 typography).</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Shows the ← back affordance (every pushed page sets this true).</summary>
    public bool ShowBack
    {
        get => (bool)GetValue(ShowBackProperty);
        set => SetValue(ShowBackProperty, value);
    }

    /// <summary>Optional back override; when null the header pops via <see cref="INavigator"/>.</summary>
    public ICommand? BackCommand
    {
        get => (ICommand?)GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    /// <summary>The top-right action view (sync radar button, gear, plus, …).</summary>
    public View? RightAction
    {
        get => (View?)GetValue(RightActionProperty);
        set => SetValue(RightActionProperty, value);
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (BackCommand is { } command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            return;
        }

        // Default: the app-wide navigator (same abstraction viewmodels use).
        var navigator = IPlatformApplication.Current?.Services.GetService<INavigator>();
        if (navigator is not null)
        {
            await navigator.GoBackAsync();
        }
        else if (Shell.Current is { } shell)
        {
            await shell.GoToAsync("..");
        }
    }
}
