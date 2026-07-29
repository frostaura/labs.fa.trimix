using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Demo;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Formatting;
using Trimix.Core.Privacy;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Settings: entitlement state, Restore purchases, units (m/ft · °C/°F via preference), privacy
/// export/delete, onboarding replay + factory reset, about.
///
/// Privacy posture (serverless, no accounts — the phone IS the account): ALL data is on-device.
/// Export serializes the device's own dive store; delete/reset wipe the local store and every
/// connector consent+token. There is no server-side copy, no sign-in, and nothing to sign out of.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IEntitlementService _entitlements;
    private readonly IPreferences _preferences;
    private readonly ILocalStore _store;
    private readonly IAppInfo _appInfo;
    private readonly INavigator _navigator;
    private readonly IUserDialogs _dialogs;
    private readonly IConnectorPurgeService _connectorPurge;
    private readonly DemoModeState? _demoModeState;
    private readonly IConnectorConsentStore? _consent;

    public SettingsViewModel(
        IEntitlementService entitlements,
        IPreferences preferences,
        ILocalStore store,
        IAppInfo appInfo,
        INavigator navigator,
        IUserDialogs dialogs,
        IConnectorPurgeService connectorPurge,
        DemoModeState? demoMode = null,
        IConnectorConsentStore? consent = null)
    {
        _entitlements = entitlements;
        _preferences = preferences;
        _store = store;
        _appInfo = appInfo;
        _navigator = navigator;
        _dialogs = dialogs;
        _connectorPurge = connectorPurge;
        _demoModeState = demoMode;
        _consent = consent;

        DepthUnit = preferences.Get(PreferenceKeys.DepthUnit) ?? DepthUnits.Meters;
        TempUnit = preferences.Get(PreferenceKeys.TempUnit) ?? TempUnits.Celsius;
        DemoMode = demoMode?.Enabled ?? false;
        RefreshEntitlement();
        entitlements.TierChanged += (_, _) => RefreshEntitlement();
    }

    [ObservableProperty]
    private string _entitlementLabel = "";

    [ObservableProperty]
    private string _depthUnit = DepthUnits.Meters;

    [ObservableProperty]
    private string _tempUnit = TempUnits.Celsius;

    public string AboutText => $"Trimix (working name) {_appInfo.AppVersion} — the dive log that fills itself.";

    /// <summary>DEBUG-only DEVELOPER card visibility — compile-time false in Release, so the card
    /// can never ship even if the XAML remains.</summary>
    public bool IsDeveloperVisible =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>The DEVELOPER card's "Demo mode" switch (persisted via <see cref="DemoModeState"/>).</summary>
    [ObservableProperty]
    private bool _demoMode;

    partial void OnDemoModeChanged(bool value)
    {
        if (_demoModeState is not null)
        {
            _demoModeState.Enabled = value;
        }
    }

    partial void OnDepthUnitChanged(string value) => _preferences.Set(PreferenceKeys.DepthUnit, value);

    partial void OnTempUnitChanged(string value) => _preferences.Set(PreferenceKeys.TempUnit, value);

    private void RefreshEntitlement()
    {
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

    /// <summary>Opens the Connected-accounts registry (connector activation + SSI QR export) — Slice 2.</summary>
    [RelayCommand]
    private Task ConnectedAccountsAsync() => _navigator.GoToAsync(ConnectedAccountsViewModel.Route);

    /// <summary>
    /// Re-runs the onboarding intro on demand (to revisit it, or demo Trimix to a friend). Purely a
    /// re-show: dives, provider links and settings are untouched, and first-run stays marked complete
    /// so the completion path no-ops the <c>first_run_completed</c> event.
    /// </summary>
    [RelayCommand]
    private Task ReplayOnboardingAsync() => _navigator.GoToAsync(Routes.Onboarding);

    /// <summary>GDPR/CCPA data export: serializes the local-first dive store (the App shares the file).</summary>
    [RelayCommand]
    private async Task<string> ExportDataAsync()
    {
        var dives = await _store.GetDivesAsync();
        var json = JsonSerializer.Serialize(
            new { exportedAtUtc = DateTime.UtcNow, dives },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await _dialogs.ToastAsync($"Export ready — {dives.Count} dives.");
        return json;
    }

    /// <summary>
    /// GDPR/CCPA delete, serverless: everything lives on-device, so erasure is a local wipe — dives,
    /// entitlement cache, and every connector consent+token. Settings (units, profile) are kept; use
    /// "Reset app" for a full factory reset.
    /// </summary>
    [RelayCommand]
    private async Task DeleteMyDataAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Delete your data?",
            "This erases all your dives and provider links on this device. Dives already synced to PADI, "
            + "SSI or other providers won't be removed from those services. This cannot be undone.",
            accept: "Delete",
            cancel: "Keep my data");
        if (!confirmed)
        {
            return;
        }

        // Wipe local dives, sever every device-global vendor consent+token, and drop the paid tier to Free
        // (a store restore can re-grant later). Serverless: there is no server copy to erase.
        foreach (var dive in await _store.GetDivesAsync())
        {
            await _store.DeleteDiveAsync(dive.Id);
        }

        await _connectorPurge.PurgeAllAsync();
        await _entitlements.ClearLocalGrantAsync();
        await _dialogs.ToastAsync("Your data has been deleted.");
    }

    /// <summary>
    /// Factory reset: the superset of "Delete my data" that also clears every on-device setting and
    /// re-runs onboarding — a fresh-install state. Confirmed first, with the same downstream-provider
    /// disclaimer (already-synced dives remain on PADI/SSI etc.).
    /// </summary>
    [RelayCommand]
    private async Task ResetAppAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Reset Trimix?",
            "This erases all dives, provider links and settings on this device and starts you fresh. "
            + "Dives already synced to PADI, SSI or other providers won't be removed from those services. "
            + "This cannot be undone.",
            accept: "Reset",
            cancel: "Cancel");
        if (!confirmed)
        {
            return;
        }

        foreach (var dive in await _store.GetDivesAsync())
        {
            await _store.DeleteDiveAsync(dive.Id);
        }

        await _connectorPurge.PurgeAllAsync();
        await _entitlements.ClearLocalGrantAsync();

        // Clear every on-device preference so the app returns to a fresh-install state. FirstRunTracked
        // is cleared last-in-spirit: onboarding will replay AND (this time) re-fire first_run_completed.
        foreach (var key in (string[])[
            PreferenceKeys.FirstRunTracked, PreferenceKeys.DepthUnit, PreferenceKeys.TempUnit,
            PreferenceKeys.UserName, PreferenceKeys.CertLabel, PreferenceKeys.LastSyncUtc,
            PreferenceKeys.AutoRestoreDone, PreferenceKeys.DevDemoMode])
        {
            _preferences.Remove(key);
        }

        if (_demoModeState is not null)
        {
            _demoModeState.Enabled = false;
        }

        await _navigator.GoToAsync(Routes.Onboarding);
    }

    /// <summary>
    /// DEVELOPER card (DEBUG-only): wipes everything demo mode seeded — the imported demo dives
    /// (recognisable by their <c>demo_</c> SourceVendor) and the demo connectors' consent + sync
    /// cursors — so the connect → sync → cap → paywall walk-through can be replayed from scratch.
    /// Real dives, real links and the entitlement tier are untouched.
    /// </summary>
    [RelayCommand]
    private async Task ResetDemoDataAsync()
    {
        var removed = 0;
        foreach (var dive in await _store.GetDivesAsync())
        {
            if (dive.SourceVendor is { } vendor && DemoModeState.IsDemoId(vendor))
            {
                await _store.DeleteDiveAsync(dive.Id);
                removed++;
            }
        }

        if (_consent is not null)
        {
            foreach (var id in (string[])[
                DemoDiveCatalog.GarminId, DemoDiveCatalog.SuuntoId, DemoDiveCatalog.MaresId,
                DemoDiveCatalog.PadiId, DemoDiveCatalog.SsiId])
            {
                await _consent.RevokeAsync(id); // also clears the demo sync cursors (token-key sweep)
            }
        }

        await _dialogs.ToastAsync($"Demo data reset — {removed} demo dives removed.");
    }
}
