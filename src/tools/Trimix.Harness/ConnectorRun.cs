using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Infra;

namespace Trimix.Harness;

/// <summary>
/// Drives a real reverse-engineered source connector from <c>Trimix.Core</c> against its live vendor API —
/// the actual proof that the shipped library works, as distinct from <see cref="GarminDiveProbe"/> which only
/// proves the endpoints exist. Read-only: authenticate, list, fetch. Connector-agnostic, so the same runner
/// verifies Garmin, ScubaPro/LogTRAK, and any future source.
/// </summary>
public static class ConnectorRun
{
    /// <summary>Builds the Garmin connector + its harness stores and runs the shared read flow.</summary>
    public static Task<int> ExecuteAsync(string username, string password, CancellationToken ct = default)
    {
        var transport = new HttpClientTransport();
        var (tokenStore, consentStore) = HarnessStoresFor("garmin");
        var connector = new GarminCloudSourceConnector(transport, tokenStore, consentStore);
        return RunAsync(connector, transport, tokenStore, consentStore, username, password, ct);
    }

    /// <summary>Builds the ScubaPro/LogTRAK connector (Firebase backend) + its harness stores and runs the flow.</summary>
    public static Task<int> ExecuteScubaProAsync(string username, string password, CancellationToken ct = default)
    {
        var transport = new HttpClientTransport();
        var (tokenStore, consentStore) = HarnessStoresFor("scubapro");
        var connector = new ScubaProCloudSourceConnector(transport, tokenStore, consentStore);
        return RunAsync(connector, transport, tokenStore, consentStore, username, password, ct);
    }

    /// <summary>TRIMIX_TOKEN_CACHE keeps the exchanged token between runs so repeated dev iterations do not
    /// re-authenticate and trip the vendor's sign-in rate limit.</summary>
    private static (ISecureTokenStore Tokens, InMemoryConsentStore Consent) HarnessStoresFor(string tag)
    {
        var cachePath = Environment.GetEnvironmentVariable("TRIMIX_TOKEN_CACHE");
        ISecureTokenStore tokens = string.IsNullOrWhiteSpace(cachePath)
            ? new InMemoryTokenStore()
            : new FileTokenStore(cachePath);
        return (tokens, new InMemoryConsentStore());
    }

    /// <summary>Renders a nullable as an em dash when absent, so provider gaps are visible at a glance.</summary>
    private static string Show(object? value) => value switch
    {
        null => "—",
        double d => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "yes" : "no",
        _ => value.ToString() ?? "—",
    };

    private static async Task<int> RunAsync(
        GatedCloudSourceConnector connector,
        HttpClientTransport transport,
        ISecureTokenStore tokenStore,
        InMemoryConsentStore consentStore,
        string username,
        string password,
        CancellationToken ct)
    {
        using var _ = transport;

        Console.WriteLine($"connector : {connector.Descriptor.ConnectorId} — \"{connector.DisplayName}\"");
        Console.WriteLine($"legal     : {connector.Descriptor.Legal}, enabledByDefault={connector.Descriptor.EnabledByDefault}");
        Console.WriteLine($"fidelity  : {connector.Descriptor.Fidelity}");
        Console.WriteLine();

        // Guardrail check: no consent yet, so this must refuse and make no network call.
        var ungated = await connector.AuthenticateAsync(ct).ConfigureAwait(false);
        Console.WriteLine($"1. authenticate without consent -> {ungated.Status}");
        if (ungated.Status != ConnectorAuthStatus.NeedsInteraction)
        {
            Console.Error.WriteLine("   GUARDRAIL FAILURE: expected NeedsInteraction before consent.");
            return 1;
        }

        Console.WriteLine("   (disclosure returned, zero network I/O — guardrail holds)");
        Console.WriteLine();

        // Grant consent as the in-app disclosure screen would, then supply credentials transiently.
        await consentStore.GrantAsync(connector.Descriptor.ConnectorId, ct).ConfigureAwait(false);
        connector.ProvideCredentials(username, password);

        var auth = await connector.AuthenticateAsync(ct).ConfigureAwait(false);
        Console.WriteLine($"2. authenticate with consent -> {auth.Status}");
        if (auth.Status != ConnectorAuthStatus.Granted)
        {
            Console.Error.WriteLine($"   FAILED: {auth.Message}");
            return 1;
        }

        var stored = await tokenStore.GetAsync(
            ConnectorTokenKeys.CloudSourceToken(connector.Descriptor.ConnectorId), ct).ConfigureAwait(false);
        Console.WriteLine($"   token envelope stored ({stored?.Length ?? 0} chars)");
        if (stored is not null && stored.Contains(password, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("   GUARDRAIL FAILURE: the password reached the token store.");
            return 1;
        }

        Console.WriteLine("   (password absent from stored token — guardrail holds)");
        Console.WriteLine();

        var dives = await connector.ListDivesSinceAsync(SyncCursor.None, ct).ConfigureAwait(false);
        Console.WriteLine($"3. listed {dives.Count} dive(s)");
        foreach (var dive in dives)
        {
            Console.WriteLine($"   {dive.SourceId,-10} {dive.StartUtc:yyyy-MM-dd HH:mm}Z  "
                              + $"{dive.DurationSec / 60,4} min  {dive.Label}");
        }

        if (dives.Count == 0)
        {
            Console.WriteLine("   nothing to fetch.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("4. fetching full profile for the most recent dive");
        var imported = await connector.FetchProfileAsync(dives[0], ct).ConfigureAwait(false);

        Console.WriteLine($"   start      : {imported.StartUtc:yyyy-MM-dd HH:mm:ss}Z");
        Console.WriteLine($"   duration   : {imported.DurationSec / 60} min {imported.DurationSec % 60} s");
        Console.WriteLine($"   max depth  : {imported.MaxDepthM:0.0} m   avg {imported.AvgDepthM:0.0} m");
        Console.WriteLine($"   min temp   : {imported.MinTempC:0.0} °C");
        Console.WriteLine($"   gas        : {imported.GasMix ?? "(none)"}");
        Console.WriteLine($"   cylinders  : {imported.Cylinders?.Count ?? 0}");
        Console.WriteLine($"   samples    : {imported.Samples.Count}");

        // The rich channels — this is what the logbook cards and detail page will render, and what other
        // providers are expected to fill in as far as they can.
        Console.WriteLine();
        Console.WriteLine("   ── rich channels ──");
        Console.WriteLine($"   dive #     : {Show(imported.DiveNumber)}");
        Console.WriteLine($"   type       : {Show(imported.DiveTypeLabel)}   deco: {Show(imported.IsDeco)}");
        Console.WriteLine($"   bottom     : {Show(imported.BottomTimeSec)} s   surface interval: {Show(imported.SurfaceIntervalSec)} s");
        Console.WriteLine($"   timezone   : {Show(imported.TimezoneId)}   water: {Show(imported.WaterType)}");

        var env = imported.Environment;
        Console.WriteLine($"   temps      : min {Show(imported.MinTempC)} / avg {Show(env?.AvgTempC)} / max {Show(env?.MaxTempC)} °C");
        Console.WriteLine($"   density    : {Show(env?.WaterDensityKgM3)} kg/m³");
        Console.WriteLine($"   entry      : {Show(env?.EntryLatitude)}, {Show(env?.EntryLongitude)}");

        var exp = imported.Exposure;
        Console.WriteLine($"   CNS        : {Show(exp?.CnsStartPercent)}% → {Show(exp?.CnsEndPercent)}%");
        Console.WriteLine($"   N₂         : {Show(exp?.N2StartPercent)}% → {Show(exp?.N2EndPercent)}%");
        Console.WriteLine($"   O₂ tox     : {Show(exp?.O2ToxicityPercent)}%   po2 avg {Show(exp?.AvgPo2)} / max {Show(exp?.MaxPo2)}");

        var comp = imported.Computer;
        Console.WriteLine($"   device     : {Show(comp?.DeviceName)}");
        Console.WriteLine($"   deco cfg   : {Show(comp?.TissueModel)}  GF {Show(comp?.GradientFactorLow)}/{Show(comp?.GradientFactorHigh)}");
        Console.WriteLine($"   tags       : {(imported.Tags is { Count: > 0 } tg ? string.Join(", ", tg) : "—")}");

        if (imported.Alerts is { Count: > 0 } alerts)
        {
            Console.WriteLine($"   alerts     : {alerts.Count}");
            foreach (var a in alerts.Take(6))
            {
                Console.WriteLine($"     t+{a.TSeconds,5}s  {a.Type}{(a.Detail is null ? "" : $"  [{a.Detail}]")}");
            }

            if (alerts.Count > 6)
            {
                Console.WriteLine($"     … {alerts.Count - 6} more");
            }
        }
        else
        {
            Console.WriteLine("   alerts     : —");
        }

        Console.WriteLine();

        if (imported.Samples.Count > 0)
        {
            var withNdl = imported.Samples.Count(s => s.Ndl is not null);
            var withCns = imported.Samples.Count(s => s.Cns is not null);
            var withPo2 = imported.Samples.Count(s => s.PpO2 is not null);
            var withTemp = imported.Samples.Count(s => s.TempC is not null);
            Console.WriteLine($"   populated  : depth={imported.Samples.Count} temp={withTemp} "
                              + $"po2={withPo2} ndl={withNdl} cns={withCns}");

            Console.WriteLine("   first 3 samples:");
            foreach (var s in imported.Samples.Take(3))
            {
                Console.WriteLine($"     t+{s.TSeconds,5}s  {s.DepthM,6:0.00} m  {s.TempC,5:0.0} °C  "
                                  + $"po2={s.PpO2,5:0.00}  ascent={s.AscentRateMPerMin,7:0.0} m/min");
            }

            var deepest = imported.Samples.MaxBy(s => s.DepthM);
            if (deepest is not null)
            {
                Console.WriteLine($"   deepest    : {deepest.DepthM:0.00} m at t+{deepest.TSeconds}s");
            }

            // Sanity: the summary's maxDepth and the sample profile must agree.
            var sampleMax = imported.Samples.Max(s => s.DepthM);
            if (Math.Abs(sampleMax - imported.MaxDepthM) > 0.5)
            {
                Console.WriteLine($"   WARNING: summary maxDepth {imported.MaxDepthM:0.00} m disagrees with "
                                  + $"deepest sample {sampleMax:0.00} m");
            }
        }

        return 0;
    }
}
