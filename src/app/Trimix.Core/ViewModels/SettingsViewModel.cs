using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Api;
using Trimix.Core.Formatting;
using Trimix.Core.Privacy;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Settings: account state, entitlement state, Restore purchases, units (m/ft · °C/°F via
/// preference), privacy export/delete, about.
///
/// Privacy posture (PRD: "GDPR/CCPA export + delete supported"): dives are LOCAL-FIRST, so export
/// serializes the device's own dive store (which, for a signed-in user, already includes the synced
/// server copies). Delete, for a signed-in user, calls the server erasure endpoint
/// (<c>POST /privacy/delete</c>) BEFORE signing out — while the bearer token still resolves — so the
/// account, analytics, receipts, entitlement, contributions, and synced dives are all hard-deleted
/// server-side; it then wipes the local store on sign-out. If the server call fails (offline / error) the
/// flow surfaces an error and does NOT sign out, so erasure is never silently skipped.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAuthSession _session;
    private readonly IEntitlementService _entitlements;
    private readonly IPreferences _preferences;
    private readonly ILocalStore _store;
    private readonly IApiClient _api;
    private readonly IAppInfo _appInfo;
    private readonly INavigator _navigator;
    private readonly IUserDialogs _dialogs;
    private readonly IConnectorPurgeService _connectorPurge;

    public SettingsViewModel(
        IAuthSession session,
        IEntitlementService entitlements,
        IPreferences preferences,
        ILocalStore store,
        IApiClient api,
        IAppInfo appInfo,
        INavigator navigator,
        IUserDialogs dialogs,
        IConnectorPurgeService connectorPurge)
    {
        _session = session;
        _entitlements = entitlements;
        _preferences = preferences;
        _store = store;
        _api = api;
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
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            await _dialogs.AlertAsync("Restore failed", "We couldn't reach the store or server. Check your connection and try again.");
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
        // leakage). Synced dives re-download from the server on next sign-in.
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
    /// GDPR/CCPA delete. For a signed-in user, calls the server erasure endpoint (<c>POST /privacy/delete</c>)
    /// while the bearer token still resolves — hard-deleting the account, analytics, receipts, entitlement,
    /// contributions, and synced dives — and only then wipes the local store on sign-out. If the server call
    /// fails, it surfaces an error and does NOT sign out, so erasure is never silently skipped. A guest has no
    /// server-side data, so it just wipes the local store.
    /// </summary>
    [RelayCommand]
    private async Task DeleteMyDataAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Delete your data?",
            "This erases your account and all your data — on this device and on our servers — and signs you out. This cannot be undone.",
            accept: "Delete",
            cancel: "Keep my data");
        if (!confirmed)
        {
            return;
        }

        // Signed-in: erase everything server-side FIRST, while the bearer token is still valid. On failure,
        // stop here (no local wipe, no sign-out) so the diver can retry rather than lose local data with the
        // server copy still present.
        if (_session.IsAuthenticated)
        {
            try
            {
                await _api.DeleteMyDataAsync();
            }
            catch (Exception ex) when (ex is ApiException or HttpRequestException)
            {
                await _dialogs.AlertAsync(
                    "Delete failed",
                    "We couldn't reach the server to erase your account. Check your connection and try again.");
                return;
            }
        }

        // Wipe local dives, then sign out (which clears the local store + entitlement cache).
        foreach (var dive in await _store.GetDivesAsync())
        {
            await _store.DeleteDiveAsync(dive.Id);
        }

        await _session.SignOutAsync();
        // AWAIT the connector purge so GDPR erasure also severs every device-global vendor consent+token
        // (the erasure path treats that linkage as personal data). Runs only after a successful server erase.
        await _connectorPurge.PurgeAllAsync();
        await _dialogs.ToastAsync("Your data has been deleted.");
    }
}
