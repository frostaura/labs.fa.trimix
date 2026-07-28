using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Formatting;
using Trimix.Core.Privacy;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Settings: account state, entitlement state, Restore purchases, units (m/ft · °C/°F via
/// preference), privacy export/delete, about.
///
/// Privacy posture (serverless): ALL data is on-device. Export serializes the device's own dive
/// store; delete wipes the local store, the session, and every connector consent+token. There is no
/// server-side copy to erase.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAuthSession _session;
    private readonly IEntitlementService _entitlements;
    private readonly IPreferences _preferences;
    private readonly ILocalStore _store;
    private readonly IAppInfo _appInfo;
    private readonly INavigator _navigator;
    private readonly IUserDialogs _dialogs;
    private readonly IConnectorPurgeService _connectorPurge;

    public SettingsViewModel(
        IAuthSession session,
        IEntitlementService entitlements,
        IPreferences preferences,
        ILocalStore store,
        IAppInfo appInfo,
        INavigator navigator,
        IUserDialogs dialogs,
        IConnectorPurgeService connectorPurge)
    {
        _session = session;
        _entitlements = entitlements;
        _preferences = preferences;
        _store = store;
        _appInfo = appInfo;
        _navigator = navigator;
        _dialogs = dialogs;
        _connectorPurge = connectorPurge;

        DepthUnit = preferences.Get(PreferenceKeys.DepthUnit) ?? DepthUnits.Meters;
        TempUnit = preferences.Get(PreferenceKeys.TempUnit) ?? TempUnits.Celsius;
        RefreshAccountState();
        entitlements.TierChanged += (_, _) => RefreshAccountState();
        session.AuthStateChanged += (_, _) => RefreshAccountState();
    }

    [ObservableProperty]
    private string _accountLabel = "";

    [ObservableProperty]
    private bool _isGuest = true;

    [ObservableProperty]
    private string _entitlementLabel = "";

    [ObservableProperty]
    private string _depthUnit = DepthUnits.Meters;

    [ObservableProperty]
    private string _tempUnit = TempUnits.Celsius;

    public string AboutText => $"Trimix (working name) {_appInfo.AppVersion} — the dive log that fills itself.";

    partial void OnDepthUnitChanged(string value) => _preferences.Set(PreferenceKeys.DepthUnit, value);

    partial void OnTempUnitChanged(string value) => _preferences.Set(PreferenceKeys.TempUnit, value);

    private void RefreshAccountState()
    {
        IsGuest = !_session.IsAuthenticated;
        AccountLabel = _session.CurrentUser?.Email ?? "Guest (dives stay on this device)";
        EntitlementLabel = _entitlements.CurrentTier switch
        {
            EntitlementTier.Lifetime => "Lifetime",
            EntitlementTier.Pro => "Pro",
            _ => "Free",
        };
    }

    [RelayCommand]
    private void ToggleDepthUnit() =>
        DepthUnit = DepthUnit == DepthUnits.Meters ? DepthUnits.Feet : DepthUnits.Meters;

    [RelayCommand]
    private void ToggleTempUnit() =>
        TempUnit = TempUnit == TempUnits.Celsius ? TempUnits.Fahrenheit : TempUnits.Celsius;

    [RelayCommand]
    private async Task RestorePurchasesAsync()
    {
        int reconciled;
        try
        {
            reconciled = await _entitlements.RestoreAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            await _dialogs.AlertAsync("Restore failed", "We couldn't reach the store. Check your connection and try again.");
            return;
        }

        await _dialogs.ToastAsync(reconciled > 0
            ? "Purchases restored."
            : "No previous purchases found for this account.");
    }

    [RelayCommand]
    private Task SignInAsync() => _navigator.GoToAsync(Routes.Auth);

    /// <summary>Opens the Connected-accounts registry (connector activation + SSI QR export) — Slice 2.</summary>
    [RelayCommand]
    private Task ConnectedAccountsAsync() => _navigator.GoToAsync(ConnectedAccountsViewModel.Route);

    [RelayCommand]
    private async Task SignOutAsync()
    {
        // Sign-out clears the local dive store + entitlement cache (account-bound; prevents cross-account
        // leakage on a shared device).
        await _session.SignOutAsync();
        // AWAIT the connector purge within the flow so it completes (not left to a fire-and-forget event
        // continuation an app teardown could interrupt), severing every device-global vendor link before the
        // next identity can inherit it. Idempotent with the automatic AuthStateChanged backstop.
        await _connectorPurge.PurgeAllAsync();
        await _dialogs.ToastAsync("Signed out.");
    }

    /// <summary>GDPR/CCPA data export: serializes the local-first dive store (the App shares the file).</summary>
    [RelayCommand]
    private async Task<string> ExportDataAsync()
    {
        var dives = await _store.GetDivesAsync();
        var json = JsonSerializer.Serialize(
            new { exportedAtUtc = DateTime.UtcNow, account = _session.CurrentUser?.Email, dives },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await _dialogs.ToastAsync($"Export ready — {dives.Count} dives.");
        return json;
    }

    /// <summary>
    /// GDPR/CCPA delete, serverless: everything lives on-device, so erasure is a local wipe — dives,
    /// session, entitlement cache, and every connector consent+token.
    /// </summary>
    [RelayCommand]
    private async Task DeleteMyDataAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Delete your data?",
            "This erases all your data on this device and signs you out. This cannot be undone.",
            accept: "Delete",
            cancel: "Keep my data");
        if (!confirmed)
        {
            return;
        }

        // Wipe local dives, then sign out (which clears the local store + entitlement cache).
        foreach (var dive in await _store.GetDivesAsync())
        {
            await _store.DeleteDiveAsync(dive.Id);
        }

        await _session.SignOutAsync();
        // AWAIT the connector purge so GDPR erasure also severs every device-global vendor consent+token
        // (the erasure path treats that linkage as personal data).
        await _connectorPurge.PurgeAllAsync();
        await _dialogs.ToastAsync("Your data has been deleted.");
    }
}
