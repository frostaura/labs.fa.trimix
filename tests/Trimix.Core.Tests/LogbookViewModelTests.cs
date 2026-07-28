using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Storage;
using Trimix.Core.Stats;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;
using static Trimix.Core.Tests.Stats.StatsTestFactory;

namespace Trimix.Core.Tests;

/// <summary>
/// Logbook integration of the stats surface: the 3 lifetime aggregates now come from
/// <see cref="DiveStats.Summarize"/> and the visible list is a <see cref="DiveMergePolicy"/> union —
/// display-only, so the free-tier cap keeps counting raw active dives.
/// </summary>
public class LogbookViewModelTests
{
    private readonly InMemoryLocalStore _store = new();
    private readonly FakeEntitlementService _entitlements = new();
    private readonly RecordingAnalytics _analytics = new();
    private readonly FakeNavigator _navigator = new();

    private LogbookViewModel CreateViewModel() =>
        new(_store, _entitlements, _analytics, _navigator, new SyncSessionViewModel(
            new FakeSyncCoordinator(),
            [],
            new Trimix.Core.Tests.Connectors.Cloud.InMemoryConnectorConsentStore(),
            _navigator,
            new FakeDialogs(),
            new Trimix.Core.Platform.InMemoryPreferences(),
            new FakeClock()),
            new Trimix.Core.Formatting.UnitFormatter(new Trimix.Core.Platform.InMemoryPreferences()),
            new Trimix.Core.Sites.SiteAtlas(),
            new Trimix.Core.Tests.Connectors.Cloud.InMemoryConnectorConsentStore(),
            []);

    private async Task SeedAsync(params DiveDto[] dives)
    {
        foreach (var dive in dives)
        {
            await _store.UpsertDiveAsync(dive);
        }
    }

    [Fact]
    public async Task Lifetime_aggregates_summarize_all_active_dives()
    {
        await SeedAsync(
            Dive(start: BaseUtc, durationSec: 3000, maxDepthM: 30),
            Dive(start: BaseUtc.AddDays(1), durationSec: 1800, maxDepthM: 40),
            Dive(start: BaseUtc.AddDays(2), durationSec: 2400, maxDepthM: 20));
        _entitlements.CurrentTier = EntitlementTier.Free;

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.TotalDives);
        Assert.Equal(40, vm.MaxDepthM);
        Assert.Equal(7200, vm.TotalBottomTimeSec);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task Cross_source_duplicates_collapse_to_one_badged_entry()
    {
        // Same physical dive from two sources → distinct rows, same start/duration/depth.
        var shearwater = Dive(
            start: BaseUtc, durationSec: 3000, maxDepthM: 30, sourceVendor: "shearwater",
            samples: [Sample(0, 0, temp: 21), Sample(60, 30, temp: 20, tank: 190)]);
        var appleWatch = Dive(
            start: BaseUtc.AddSeconds(20), durationSec: 3010, maxDepthM: 30.4,
            sourceVendor: "apple_health", samples: [Sample(0, 0, temp: 21)]);
        await SeedAsync(shearwater, appleWatch);
        _entitlements.CurrentTier = EntitlementTier.Free;

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        // Strip totals count PHYSICAL dives: the two source rows are one dive, so the strip reads 1 — it must
        // not double-count just because a second source synced the same dive (that inflates with each source).
        Assert.Equal(1, vm.TotalDives);
        Assert.Equal(3000, vm.TotalBottomTimeSec); // one dive's bottom time, not 6010
        var row = Assert.Single(vm.VisibleDives);
        Assert.Equal(2, row.Merged.Badges.Count);
        Assert.Contains(row.Merged.Badges, b => b.Label == "Shearwater");
        Assert.Contains(row.Merged.Badges, b => b.Label == "Apple Watch");
        // Richest-fidelity-per-field: the full-profile (tank-bearing) samples win.
        Assert.Contains(row.Merged.Primary.ProfileSamples, s => s.TankPressureBar is not null);
        // Route string origin: the union's source vendor, uppercased.
        Assert.StartsWith("SHEARWATER", row.Route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Free_tier_cap_counts_raw_active_dives_not_merged()
    {
        var raw = FreeTier.FreeLogbookVisibleDives + 2; // 27 distinct dives
        var dives = Enumerable.Range(0, raw)
            .Select(i => Dive(start: BaseUtc.AddDays(i), durationSec: 1800 + i, maxDepthM: 10 + i))
            .ToArray();
        await SeedAsync(dives);
        _entitlements.CurrentTier = EntitlementTier.Free;

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(raw, vm.TotalDives);
        Assert.Equal(FreeTier.FreeLogbookVisibleDives, vm.VisibleDives.Count);
        Assert.Equal(2, vm.HiddenDiveCount);
        Assert.True(vm.IsCapMeterVisible);
    }
}
