namespace Trimix.Core.Abstractions;

/// <summary>User-facing alerts/confirmations/toasts so viewmodels stay MAUI-free.</summary>
public interface IUserDialogs
{
    Task AlertAsync(string title, string message, string cancel = "OK");

    Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel");

    Task ToastAsync(string message);
}
