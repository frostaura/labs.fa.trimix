using Trimix.Contracts.Dtos;

namespace Trimix.Core.Abstractions;

/// <summary>
/// A legally-clean source of dives (architecture overview §2). Every source connector's job is to
/// <em>produce <see cref="ImportedDive"/>s</em>; from the <see cref="ImportedDive"/> boundary the
/// existing <c>ImportService</c> pipeline (site auto-match → free-tier truncation → <c>ToDiveDto</c>
/// → <c>store.UpsertDiveAsync</c> → <c>dive_logged_import</c> → paywall) runs unchanged. BLE is
/// demoted to one <see cref="ConnectorKind.BleDevice"/> connector; HealthKit / file / cloud sources
/// map their structured data straight into <see cref="ImportedDive"/>, skipping the byte-frame
/// machinery.
/// </summary>
public interface IDiveSourceConnector
{
    ConnectorDescriptor Descriptor { get; }
    Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default);
    Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default); // emits the EXISTING ImportedDive
}

/// <summary>
/// A destination the diver can push their canonical dives to (file export, cert-agency push, …).
/// None ship in the foundation; the port exists so destinations self-register alongside sources.
/// </summary>
public interface IDiveDestinationConnector
{
    ConnectorDescriptor Descriptor { get; }
    Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default);
    Task<PushResult> PushDiveAsync(DiveDto dive, CancellationToken ct = default); // consumes the canonical DTO
}

/// <summary>
/// Static, self-declared identity + policy of a connector (architecture overview §2). Legal gating is
/// structural: any connector whose <see cref="Legal"/> is <see cref="LegalPosture.ToSRiskReverseEng"/>
/// MUST carry <see cref="EnabledByDefault"/> == false (ships dark), enabled only behind explicit
/// per-user opt-in + disclosure + recorded legal sign-off. The foundation adds no such connector; the
/// descriptor only guarantees later reverse-engineered connectors ship gated by construction.
/// </summary>
public sealed record ConnectorDescriptor(
    string ConnectorId,          // "ble_device", "apple_health", "garmin_fit", "export_uddf", ...
    ConnectorKind Kind,          // BleDevice | HealthStore | FileImport | CloudApi | FileExport
    LegalPosture Legal,          // CleanOfficial | OwnedHardware | UserFile | PartnerRequired | ToSRiskReverseEng
    ProfileFidelity Fidelity,    // FullProfile | DepthTempOnly | SummaryOnly
    AuthModel Auth,              // None | OnDeviceConsent | OAuth2 | UserCredential
    Platforms Platforms,         // Android | iOS | Both
    bool EnabledByDefault);      // false for every ToSRiskReverseEng connector (ships dark)

public enum ConnectorKind { BleDevice, HealthStore, FileImport, CloudApi, FileExport }
public enum LegalPosture { CleanOfficial, OwnedHardware, UserFile, PartnerRequired, ToSRiskReverseEng }
public enum ProfileFidelity { FullProfile, DepthTempOnly, SummaryOnly }
public enum AuthModel { None, OnDeviceConsent, OAuth2, UserCredential }

[Flags]
public enum Platforms { None = 0, Android = 1, iOS = 2, Both = Android | iOS }

public enum ConnectorAuthStatus { Granted, Denied, NeedsInteraction, Unavailable }

public sealed record ConnectorAuthResult(ConnectorAuthStatus Status, string? Message = null);

/// <summary>Incremental-sync cursor: dives since <see cref="Since"/> / opaque <see cref="Token"/>.</summary>
public sealed record SyncCursor(DateTime? Since, string? Token = null)
{
    public static readonly SyncCursor None = new(null, null);
}

/// <summary>A lightweight handle to a dive at a source, resolved to an <see cref="ImportedDive"/> by <c>FetchProfileAsync</c>.</summary>
public sealed record DiveRef(string ConnectorId, string SourceId, DateTime StartUtc, int DurationSec, string? Label = null);

public enum PushStatus { Pushed, Skipped, Failed, Unsupported }

public sealed record PushResult(PushStatus Status, string? DestinationId = null, string? Message = null);
