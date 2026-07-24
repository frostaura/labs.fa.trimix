using System.Globalization;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core;
using Trimix.Core.Abstractions;
using Trimix.Core.Analytics;
using Trimix.Core.Api;
using Trimix.Core.Billing;
using Trimix.Core.Platform;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// THE END-TO-END FUNNEL RECONCILIATION TEST (architecture overview §7, PRD Layer 3): drives the
/// full journey through Trimix.Core's REAL <see cref="ApiClient"/> — its pseudo-id handler stamps the
/// <see cref="TrimixHeaders.UserPseudoId"/> header on every request, so server-emitted funnel events
/// (<c>account_created</c>, <c>entitlement_granted</c>, <c>entitlement_restored</c>) reconcile into
/// the same per-pseudo-user funnel as client-emitted events WITHOUT any hand-rolled header injection —
/// plus Trimix.Core's <see cref="SandboxBillingService"/> (the slice's live purchase path). It asserts
/// the per-pseudo-user event ordering (by client/logical timestamp, never DB insertion id) matches the
/// funnel spine — <c>install → first_run_completed → dive_logged_* → paywall_shown → purchase_completed
/// → entitlement_granted</c> — plus the reinstall path <c>install → entitlement_restored</c>.
/// </summary>
public sealed class FunnelReconciliationTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    // Strictly-increasing logical clock anchored to real time, shared across every event this test
    // emits. Client events get an explicit timestamp from here; server-emitted rows carry the server's
    // own UtcNow. Because both live on one real timeline and every leg is separated by an awaited
    // round-trip, ordering the stored rows by ClientTimestampUtc reproduces true chronology — the
    // reconciliation gate never leans on DB insertion id (server rows can insert before queued client
    // rows they actually preceded).
    private DateTime _lastTimestamp = DateTime.UtcNow;

    public void Dispose() => _factory.Dispose();

    private DateTime NextTimestamp()
    {
        var now = DateTime.UtcNow;
        _lastTimestamp = now > _lastTimestamp ? now : _lastTimestamp.AddTicks(1);
        return _lastTimestamp;
    }

    /// <summary>
    /// A device: the REAL Trimix.Core <see cref="ApiClient"/> (pseudo-id handler stamps every request —
    /// no manual header) bound to the in-memory API host, plus the secure store its bearer token lands in.
    /// </summary>
    private sealed record Device(ApiClient Api, ISecureStore SecureStore, string PseudoId);

    private Device CreateDevice(string pseudoId)
    {
        // Seed the per-install pseudo-id so the real UserPseudoIdProvider resolves exactly this device
        // identity — the same value the ApiClient sends as the pseudo-id header AND the value we stamp
        // on client analytics rows. No test code touches the header directly.
        var preferences = new InMemoryPreferences();
        preferences.Set(PreferenceKeys.UserPseudoId, pseudoId);
        var pseudoIds = new UserPseudoIdProvider(preferences);
        var secureStore = new InMemorySecureStore();
        var api = new ApiClient(_factory.CreateClient(), secureStore, pseudoIds);
        return new Device(api, secureStore, pseudoIds.UserPseudoId);
    }

    /// <summary>Emits client-side funnel events through the real ApiClient with explicit logical timestamps.</summary>
    private async Task EmitAsync(Device device, params (string Name, Dictionary<string, string> Props)[] events)
    {
        var batch = new AnalyticsBatchRequest(events
            .Select(e => new AnalyticsEventDto(
                e.Name, e.Props, device.PseudoId, NextTimestamp(),
                TestClient.TestPlatform, TestClient.TestAppVersion, Guid.NewGuid()))
            .ToList());
        var response = await device.Api.SendAnalyticsAsync(batch);
        Assert.Equal(events.Length, response.Accepted);
    }

    [Fact]
    public async Task Funnel_spine_reconciles_end_to_end_and_reinstall_restores()
    {
        const string email = "funnel-diver@example.com";

        // Durable store-account memory: shared across the two SandboxBillingService instances so the
        // reinstall's fresh instance restores PERSISTED ownership rather than in-process state. Receipt
        // identity is a pure function of (account, sku) — which is what makes the restore reconcile
        // instead of double-granting.
        var storePreferences = new InMemoryPreferences();
        var storeOptions = new SandboxBillingOptions { Secret = TrimixApiFactory.SandboxSecret, AccountId = email };
        var billing = new SandboxBillingService(storeOptions, storePreferences);

        // ---------- Install #1: fresh device, fresh pseudo-user, real ApiClient ----------
        const string pseudo1 = "pseudo-device-1";
        var device1 = CreateDevice(pseudo1);

        // install → first_run_completed (client-emitted, pre-auth: pseudo-id only).
        await EmitAsync(device1,
            (FunnelEvents.Install, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Platform] = TestClient.TestPlatform,
                [FunnelEvents.Props.Source] = "organic",
                [FunnelEvents.Props.AppVersion] = TestClient.TestAppVersion,
            }),
            (FunnelEvents.FirstRunCompleted, new Dictionary<string, string>
            {
                [FunnelEvents.Props.UserPseudoId] = pseudo1,
            }));

        // account_created — SERVER-emitted during register, attributed via the ApiClient's pseudo-id
        // HEADER (no manual injection). Persist the returned bearer so later calls authenticate.
        var auth = await device1.Api.RegisterAsync(new RegisterRequest(email, TestClient.DefaultPassword));
        await device1.SecureStore.SetAsync(SecureStoreKeys.AuthToken, auth.Token);

        // dive_logged_manual — and the dive really syncs through the real client.
        var diveId = Guid.NewGuid();
        await device1.Api.SyncDivesAsync(new DiveSyncRequest(
            [
                new DiveDto(diveId, DateTime.UtcNow.AddHours(-3), 2400, 18.3, 12.1, 26.0, "Air",
                    DiveSources.Manual, null, null, null, null, null, "First dive of the trip", 4,
                    DateTime.UtcNow, false, [new ProfileSampleDto(0, 0, 26.5, null), new ProfileSampleDto(60, 12.0, 26.0, null)]),
            ], [], null));
        await EmitAsync(device1,
            (FunnelEvents.DiveLoggedManual, new Dictionary<string, string>
            {
                [FunnelEvents.Props.DiveId] = diveId.ToString(),
                [FunnelEvents.Props.SiteMatchedBool] = "false",
            }));

        // paywall_shown (the PURCHASE MOMENT) → purchase_started.
        await EmitAsync(device1,
            (FunnelEvents.PaywallShown, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Trigger] = FunnelEvents.Values.TriggerBleImportCap,
                [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
            }),
            (FunnelEvents.PurchaseStarted, new Dictionary<string, string>
            {
                [FunnelEvents.Props.SkuId] = Skus.Pro,
            }));

        // The sandbox store confirms the purchase (Trimix.Core's SandboxBillingService generates the
        // HMAC-signed token via the shared SandboxReceiptCodec AND persists ownership) → purchase_completed.
        var products = await billing.QueryProductsAsync([Skus.Pro]);
        var product = Assert.Single(products);
        var receipt = await billing.PurchaseAsync(Skus.Pro);
        await EmitAsync(device1,
            (FunnelEvents.PurchaseCompleted, new Dictionary<string, string>
            {
                [FunnelEvents.Props.SkuId] = receipt.SkuId,
                [FunnelEvents.Props.GrossPrice] = product.Price.ToString(CultureInfo.InvariantCulture),
                [FunnelEvents.Props.Currency] = product.CurrencyCode,
            }));

        // POST /purchases/validate through the real client → server verifies and emits entitlement_granted
        // (attributed to pseudo1 via the same pseudo-id header handler — nothing hand-rolled).
        var granted = await device1.Api.ValidatePurchaseAsync(
            new ValidatePurchaseRequest(receipt.Platform, receipt.SkuId, receipt.Token));
        Assert.Equal(PurchaseValidationOutcome.Granted, granted.Outcome);
        Assert.Equal(EntitlementTier.Pro, granted.Tier);
        Assert.Equal(EntitlementTier.Pro, (await device1.Api.GetMyEntitlementAsync()).Tier);

        // ---------- Reconciliation #1: per-pseudo-user ordering (by logical timestamp) matches the spine ----------
        var journey = _factory.EventNamesFor(pseudo1);
        Assert.Equal(
            new[]
            {
                FunnelEvents.Install,
                FunnelEvents.FirstRunCompleted,
                FunnelEvents.AccountCreated,
                FunnelEvents.DiveLoggedManual,
                FunnelEvents.PaywallShown,
                FunnelEvents.PurchaseStarted,
                FunnelEvents.PurchaseCompleted,
                FunnelEvents.EntitlementGranted,
            },
            journey);

        // The PRD's funnel spine reconciles as an ordered subsequence of the journey.
        TestClient.AssertOrderedSubsequence(
            [
                FunnelEvents.Install,
                FunnelEvents.FirstRunCompleted,
                FunnelEvents.DiveLoggedManual,
                FunnelEvents.PaywallShown,
                FunnelEvents.PurchaseCompleted,
                FunnelEvents.EntitlementGranted,
            ],
            journey);

        // ---------- Install #2 (reinstall): new device identity + a FRESH billing instance ----------
        const string pseudo2 = "pseudo-device-2-reinstall";
        var device2 = CreateDevice(pseudo2);

        // A brand-new SandboxBillingService instance that carries nothing in process — it loads the
        // PERSISTED store-account ownership on construction, which is exactly what a reinstall's restore
        // reads back. (Same account + secret + deterministic identity ⇒ the identical receipt token.)
        var reinstallBilling = new SandboxBillingService(storeOptions, storePreferences);

        await EmitAsync(device2,
            (FunnelEvents.Install, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Platform] = TestClient.TestPlatform,
                [FunnelEvents.Props.Source] = "organic",
                [FunnelEvents.Props.AppVersion] = TestClient.TestAppVersion,
            }));

        // Sign in → restore purchases (auto-invoked on fresh install after sign-in, per PRD Flow D).
        var login = await device2.Api.LoginAsync(new LoginRequest(email, TestClient.DefaultPassword));
        await device2.SecureStore.SetAsync(SecureStoreKeys.AuthToken, login.Token);

        var restoredReceipts = await reinstallBilling.RestoreAsync();
        var restoredReceipt = Assert.Single(restoredReceipts);
        Assert.Equal(receipt.Token, restoredReceipt.Token); // Deterministic identity, reproduced from persisted ownership.

        var restored = await device2.Api.ValidatePurchaseAsync(
            new ValidatePurchaseRequest(restoredReceipt.Platform, restoredReceipt.SkuId, restoredReceipt.Token));
        Assert.Equal(PurchaseValidationOutcome.Restored, restored.Outcome);
        Assert.Equal(EntitlementTier.Pro, restored.Tier);

        // Entitlement survived the reinstall — server truth, account-bound.
        Assert.Equal(EntitlementTier.Pro, (await device2.Api.GetMyEntitlementAsync()).Tier);

        // ---------- Reconciliation #2: reinstall path install → entitlement_restored ----------
        var reinstallJourney = _factory.EventNamesFor(pseudo2);
        Assert.Equal(
            new[] { FunnelEvents.Install, FunnelEvents.EntitlementRestored },
            reinstallJourney);

        // And the first device's funnel gained no extra grant — one receipt, one grant, ever.
        var grantRows = _factory.WithDb(db =>
            db.AnalyticsEvents.Count(e => e.Name == FunnelEvents.EntitlementGranted));
        Assert.Equal(1, grantRows);
    }

    /// <summary>
    /// THE CLOUD-AGGREGATOR SPINE (PRD Amendments set 2, item 10): the multi-source import/sync path
    /// reconciles the UPDATED spine <c>install → first_run_completed → dive_logged_* → dives_synced →
    /// paywall_shown(cloud_sync_cap) → purchase_completed → entitlement_granted</c>. This is the additive
    /// analogue of the BLE spine above — <c>source_connected</c>/<c>dives_synced</c> stand in for
    /// <c>ble_device_connected</c>/<c>dives_fetched</c>, the cap fires <c>trigger=cloud_sync_cap</c>
    /// instead of <c>ble_import_cap</c>, and every clean import channel keeps <c>dive_logged_import</c>
    /// with <c>source=import</c> (origin rides the free-text <c>vendor</c>). The BLE spine test above is
    /// retained unchanged — both paths are valid, per the retain-and-deprecate rule.
    /// </summary>
    [Fact]
    public async Task Cloud_aggregator_spine_reconciles_with_dives_synced_and_cloud_sync_cap()
    {
        const string email = "cloud-diver@example.com";

        var storePreferences = new InMemoryPreferences();
        var storeOptions = new SandboxBillingOptions { Secret = TrimixApiFactory.SandboxSecret, AccountId = email };
        var billing = new SandboxBillingService(storeOptions, storePreferences);

        const string pseudo = "pseudo-cloud-device-1";
        var device = CreateDevice(pseudo);

        // install → first_run_completed (client-emitted, pre-auth).
        await EmitAsync(device,
            (FunnelEvents.Install, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Platform] = TestClient.TestPlatform,
                [FunnelEvents.Props.Source] = "organic",
                [FunnelEvents.Props.AppVersion] = TestClient.TestAppVersion,
            }),
            (FunnelEvents.FirstRunCompleted, new Dictionary<string, string>
            {
                [FunnelEvents.Props.UserPseudoId] = pseudo,
            }));

        // account_created — SERVER-emitted during register, attributed via the pseudo-id header.
        var auth = await device.Api.RegisterAsync(new RegisterRequest(email, TestClient.DefaultPassword));
        await device.SecureStore.SetAsync(SecureStoreKeys.AuthToken, auth.Token);

        // dive_logged_manual — the first dive; the spine's dive_logged_* milestone (precedes dives_synced).
        var manualDiveId = Guid.NewGuid();
        await device.Api.SyncDivesAsync(new DiveSyncRequest(
            [
                new DiveDto(manualDiveId, DateTime.UtcNow.AddHours(-4), 2100, 16.0, 11.0, 25.0, "Air",
                    DiveSources.Manual, null, null, null, null, null, "Shore check-out dive", 3,
                    DateTime.UtcNow, false, [new ProfileSampleDto(0, 0, 25.5, null), new ProfileSampleDto(60, 10.0, 25.0, null)]),
            ], [], null));
        await EmitAsync(device,
            (FunnelEvents.DiveLoggedManual, new Dictionary<string, string>
            {
                [FunnelEvents.Props.DiveId] = manualDiveId.ToString(),
                [FunnelEvents.Props.SiteMatchedBool] = "false",
            }));

        // source_connected → dives_synced — the cloud-aggregator legs. 'source' is the free-text connector
        // id (Apple Health here), NOT the frozen dive_logged_import 'import' value; the server accepts it.
        await EmitAsync(device,
            (FunnelEvents.SourceConnected, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Source] = "apple_health",
                [FunnelEvents.Props.Platform] = TestClient.TestPlatform,
            }),
            (FunnelEvents.DivesSynced, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Source] = "apple_health",
                [FunnelEvents.Props.Count] = "12",
            }));

        // A synced dive logs as dive_logged_import — source=import (frozen), origin via free-text vendor.
        var importDiveId = Guid.NewGuid();
        await EmitAsync(device,
            (FunnelEvents.DiveLoggedImport, new Dictionary<string, string>
            {
                [FunnelEvents.Props.DiveId] = importDiveId.ToString("N"),
                [FunnelEvents.Props.Vendor] = "apple_health",
                [FunnelEvents.Props.Source] = FunnelEvents.Values.SourceImport,
            }));

        // paywall_shown(cloud_sync_cap) — the multi-source purchase moment → purchase_started.
        // The server ACCEPTS this only because cloud_sync_cap is in AllowedValues[trigger]; EmitAsync's
        // accepted-count assertion is the proof the new trigger validates end-to-end.
        await EmitAsync(device,
            (FunnelEvents.PaywallShown, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Trigger] = FunnelEvents.Values.TriggerCloudSyncCap,
                [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
            }),
            (FunnelEvents.PurchaseStarted, new Dictionary<string, string>
            {
                [FunnelEvents.Props.SkuId] = Skus.Pro,
            }));

        // Sandbox confirms the purchase → purchase_completed.
        var products = await billing.QueryProductsAsync([Skus.Pro]);
        var product = Assert.Single(products);
        var receipt = await billing.PurchaseAsync(Skus.Pro);
        await EmitAsync(device,
            (FunnelEvents.PurchaseCompleted, new Dictionary<string, string>
            {
                [FunnelEvents.Props.SkuId] = receipt.SkuId,
                [FunnelEvents.Props.GrossPrice] = product.Price.ToString(CultureInfo.InvariantCulture),
                [FunnelEvents.Props.Currency] = product.CurrencyCode,
            }));

        // Server validates and emits entitlement_granted (pseudo-id header attribution).
        var granted = await device.Api.ValidatePurchaseAsync(
            new ValidatePurchaseRequest(receipt.Platform, receipt.SkuId, receipt.Token));
        Assert.Equal(PurchaseValidationOutcome.Granted, granted.Outcome);
        Assert.Equal(EntitlementTier.Pro, granted.Tier);
        Assert.Equal(EntitlementTier.Pro, (await device.Api.GetMyEntitlementAsync()).Tier);

        // ---------- Reconciliation: full per-pseudo journey in logical-timestamp order ----------
        var journey = _factory.EventNamesFor(pseudo);
        Assert.Equal(
            new[]
            {
                FunnelEvents.Install,
                FunnelEvents.FirstRunCompleted,
                FunnelEvents.AccountCreated,
                FunnelEvents.DiveLoggedManual,
                FunnelEvents.SourceConnected,
                FunnelEvents.DivesSynced,
                FunnelEvents.DiveLoggedImport,
                FunnelEvents.PaywallShown,
                FunnelEvents.PurchaseStarted,
                FunnelEvents.PurchaseCompleted,
                FunnelEvents.EntitlementGranted,
            },
            journey);

        // The UPDATED cloud spine reconciles as an ordered subsequence of the journey.
        TestClient.AssertOrderedSubsequence(
            [
                FunnelEvents.Install,
                FunnelEvents.FirstRunCompleted,
                FunnelEvents.DiveLoggedManual,
                FunnelEvents.DivesSynced,
                FunnelEvents.PaywallShown,
                FunnelEvents.PurchaseCompleted,
                FunnelEvents.EntitlementGranted,
            ],
            journey);

        // The persisted purchase-moment carries the CLOUD trigger (distinct from the BLE path's ble_import_cap).
        var paywallProps = _factory.WithDb(db => db.AnalyticsEvents
            .Single(e => e.UserPseudoId == pseudo && e.Name == FunnelEvents.PaywallShown)
            .PropsJson);
        Assert.Contains(FunnelEvents.Values.TriggerCloudSyncCap, paywallProps);
    }
}
