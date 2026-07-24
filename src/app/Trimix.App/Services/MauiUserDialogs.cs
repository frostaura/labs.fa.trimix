using Trimix.Core.Abstractions;

namespace Trimix.App.Services;

/// <summary>
/// <see cref="IUserDialogs"/> over the current window's page. Toasts use the native Android toast;
/// other platforms fall back to a short alert (no CommunityToolkit.Maui — architecture §12).
/// </summary>
public sealed class MauiUserDialogs : IUserDialogs
{
    private static Page? CurrentPage =>
        Application.Current?.Windows.FirstOrDefault()?.Page;

    public Task AlertAsync(string title, string message, string cancel = "OK") =>
        MainThread.InvokeOnMainThreadAsync(() =>
            CurrentPage?.DisplayAlert(title, message, cancel) ?? Task.CompletedTask);

    public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel") =>
        MainThread.InvokeOnMainThreadAsync(() =>
            CurrentPage?.DisplayAlert(title, message, accept, cancel) ?? Task.FromResult(false));

    public Task ToastAsync(string message) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
#if ANDROID
            Android.Widget.Toast.MakeText(
                Platform.AppContext, message, Android.Widget.ToastLength.Short)?.Show();
            return Task.CompletedTask;
#else
            return CurrentPage?.DisplayAlert(null, message, "OK") ?? Task.CompletedTask;
#endif
        });
}
