using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.FileImport;
using Trimix.Core.DiveComputer;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests.Connectors.FileImport;

/// <summary>
/// FileImportSourceConnector: format sniffing/routing, the cancelled-pick and unrecognized-file paths, and
/// an end-to-end pass through the real <c>ImportService.ImportFromConnectorAsync</c> seam (the plug point:
/// a file connector produces <see cref="ImportedDive"/>s and the existing source-agnostic downstream runs
/// unchanged).
/// </summary>
public class FileImportSourceConnectorTests
{
    private static IReadOnlyList<IDiveFileParser> Parsers() =>
        [new GarminFitParser(), new SuuntoJsonParser(), new UddfParser(), new DanDl7Parser()];

    private static FileImportSourceConnector Connector(PickedFile? file) =>
        new(new FakeFilePicker(file), Parsers());

    [Fact]
    public void Descriptor_is_clean_user_file_and_on_by_default()
    {
        var descriptor = Connector(null).Descriptor;

        Assert.Equal("file_import", descriptor.ConnectorId);
        Assert.Equal(ConnectorKind.FileImport, descriptor.Kind);
        Assert.Equal(LegalPosture.UserFile, descriptor.Legal); // clean: the diver moves their own data
        Assert.True(descriptor.EnabledByDefault);
        Assert.Equal(Platforms.Both, descriptor.Platforms);
    }

    [Theory]
    [InlineData("sample.fit", "garmin_fit")]
    [InlineData("sample.uddf", "uddf")]
    [InlineData("sample.dl7", "dan_dl7")]
    [InlineData("suunto-sample.json", "suunto_json")]
    public async Task Sniffs_each_format_and_tags_the_ref_with_the_format_id(string fixture, string expectedFormat)
    {
        var connector = Connector(FileImportFixtures.Load(fixture));

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        var diveRef = Assert.Single(refs);
        Assert.Equal("file_import", diveRef.ConnectorId);
        Assert.Equal(expectedFormat, diveRef.Label); // format id rides on Label for provenance
    }

    [Fact]
    public async Task Fetch_returns_the_buffered_dive()
    {
        var connector = Connector(FileImportFixtures.Uddf);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);
        var dive = await connector.FetchProfileAsync(refs[0]);

        Assert.Equal(18.0, dive.MaxDepthM, 3);
        Assert.Equal("EAN32", dive.GasMix);
    }

    [Fact]
    public async Task Cancelled_pick_yields_no_dives_without_throwing()
    {
        var connector = Connector(null); // picker returns null → diver cancelled

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        Assert.Empty(refs);
    }

    [Fact]
    public async Task Unrecognized_file_throws()
    {
        var connector = Connector(new PickedFile("mystery.bin", [1, 2, 3, 4, 5]));

        await Assert.ThrowsAsync<DiveFileParseException>(() => connector.ListDivesSinceAsync(SyncCursor.None));
    }

    [Fact]
    public async Task Imports_through_the_ImportService_seam_end_to_end()
    {
        var store = new InMemoryLocalStore();
        var analytics = new RecordingAnalytics();
        var connector = Connector(FileImportFixtures.SuuntoJson);
        var service = new ImportService(
            [connector],
            new FakeApiClient(),
            new FakeLocation(),
            new FakeEntitlementService { CurrentTier = EntitlementTier.Pro },
            store,
            analytics,
            new FakeAppInfo(),
            new FakeClock(),
            NullLogger<ImportService>.Instance);

        var result = await service.ImportFromConnectorAsync(connector, SyncCursor.None);

        // The file connector's dive flowed through the unchanged downstream and persisted. Provenance is the
        // per-file FORMAT id (from DiveRef.Label), not the generic "file_import" connector id — so a Suunto
        // export badges as Suunto and the deterministic id keeps format identity.
        var dive = Assert.Single(result.ImportedDives);
        Assert.Equal("18/45", dive.GasMix);
        Assert.Equal(DiveSources.Import, dive.Source);
        Assert.Equal("suunto_json", dive.SourceVendor);
        Assert.Single(await store.GetDivesAsync());

        // Funnel spine: source_connected → dives_synced → dive_logged_import (source=import, frozen).
        Assert.Contains(analytics.Events, e => e.Name == FunnelEvents.SourceConnected);
        Assert.Contains(analytics.Events, e => e.Name == FunnelEvents.DivesSynced);
        var logged = Assert.Single(analytics.Events, e => e.Name == FunnelEvents.DiveLoggedImport);
        Assert.Equal(FunnelEvents.Values.SourceImport, logged.Props[FunnelEvents.Props.Source]);
        Assert.Equal("suunto_json", logged.Props[FunnelEvents.Props.Vendor]);
    }
}
