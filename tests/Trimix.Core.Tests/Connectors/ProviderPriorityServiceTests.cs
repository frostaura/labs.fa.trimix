using Trimix.Core;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors;
using Trimix.Core.Platform;
using Trimix.Core.Stats;
using Trimix.Core.Tests.Fakes;
using Xunit;
using static Trimix.Core.Tests.Stats.StatsTestFactory;

namespace Trimix.Core.Tests.Connectors;

/// <summary>
/// The user-defined provider priority (which computer wins an overlapping-dive merge): default order mirrors
/// the registered sources, a persisted CSV survives, unknown/new ids are handled, and the composite delegate
/// makes priority dominate fidelity while fidelity breaks intra-provider ties.
/// </summary>
public class ProviderPriorityServiceTests
{
    private static readonly string[] DefaultOrder = ["garmin_connect", "suunto_cloud", "scubapro"];

    private static (ProviderPriorityService Service, InMemoryPreferences Prefs) NewService(params string[] order)
    {
        var prefs = new InMemoryPreferences();
        IEnumerable<IDiveSourceConnector> sources = (order.Length > 0 ? order : DefaultOrder)
            .Select(id => new FakeSourceConnector(id))
            .ToArray();
        return (new ProviderPriorityService(prefs, sources), prefs);
    }

    [Fact]
    public void Default_order_is_the_registered_source_order()
    {
        var (service, _) = NewService();
        Assert.Equal(DefaultOrder, service.Order());
    }

    [Fact]
    public void Persisted_order_is_honoured_and_survives()
    {
        var (service, prefs) = NewService();
        service.Set(["scubapro", "garmin_connect", "suunto_cloud"]);

        Assert.Equal(["scubapro", "garmin_connect", "suunto_cloud"], service.Order());
        // A fresh service over the same prefs reads the same order back.
        var reread = new ProviderPriorityService(prefs, DefaultOrder.Select(id => new FakeSourceConnector(id)));
        Assert.Equal(["scubapro", "garmin_connect", "suunto_cloud"], reread.Order());
    }

    [Fact]
    public void A_registered_provider_missing_from_the_stored_order_is_appended()
    {
        var (service, _) = NewService();
        service.Set(["scubapro"]); // only one of three named
        var order = service.Order();

        Assert.Equal("scubapro", order[0]);
        Assert.Contains("garmin_connect", order);
        Assert.Contains("suunto_cloud", order);
        Assert.Equal(3, order.Count);
    }

    [Fact]
    public void An_unregistered_stored_id_is_dropped()
    {
        var (service, _) = NewService();
        service.Set(["ghost_provider", "garmin_connect", "scubapro", "suunto_cloud"]);
        Assert.DoesNotContain("ghost_provider", service.Order());
    }

    [Fact]
    public void RankOf_is_higher_for_earlier_providers_and_zero_for_unknown()
    {
        var (service, _) = NewService();
        Assert.True(service.RankOf("garmin_connect") > service.RankOf("suunto_cloud"));
        Assert.True(service.RankOf("suunto_cloud") > service.RankOf("scubapro"));
        Assert.Equal(0, service.RankOf("not_a_provider"));
        Assert.Equal(0, service.RankOf(null));
    }

    [Fact]
    public void Composite_delegate_lets_priority_dominate_fidelity()
    {
        var (service, _) = NewService();
        // scubapro is LAST in priority but a rich full profile; garmin is FIRST but a bare summary.
        var richLowPriority = Dive(sourceVendor: "scubapro", gasMix: "air",
            samples: new[] { Sample(0, 0, temp: 20, ndl: 99, tank: 200), Sample(60, 30, temp: 18, ndl: 5, tank: 150) });
        var sparseHighPriority = Dive(sourceVendor: "garmin_connect");

        var fid = service.FidelityDelegate();
        Assert.True(fid(sparseHighPriority) > fid(richLowPriority)); // priority wins outright
    }

    [Fact]
    public void Composite_delegate_breaks_intra_provider_ties_by_fidelity()
    {
        var (service, _) = NewService();
        var richer = Dive(sourceVendor: "garmin_connect", gasMix: "air",
            samples: new[] { Sample(0, 0, temp: 20, ndl: 99), Sample(60, 30, temp: 18, ndl: 5) });
        var sparser = Dive(sourceVendor: "garmin_connect");

        var fid = service.FidelityDelegate();
        Assert.True(fid(richer) > fid(sparser)); // same provider → fidelity decides
    }

    [Fact]
    public void Move_reorders_and_persists()
    {
        var (service, _) = NewService();
        service.Move("scubapro", 0); // last → first
        Assert.Equal("scubapro", service.Order()[0]);
    }

    [Fact]
    public void Priority_drives_the_merge_primary()
    {
        var (service, _) = NewService();
        service.Set(["scubapro", "garmin_connect", "suunto_cloud"]); // scubapro wins

        var garminId = Guid.NewGuid();
        var scubaId = Guid.NewGuid();
        // Garmin is the richer profile, but scubapro is higher priority, so scubapro must be primary.
        var garmin = Dive(id: garminId, start: new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc),
            durationSec: 3000, maxDepthM: 30, sourceVendor: "garmin_connect", gasMix: "air",
            samples: new[] { Sample(0, 0, temp: 20, ndl: 99, tank: 200), Sample(60, 30, temp: 18, ndl: 5, tank: 150) });
        var scuba = Dive(id: scubaId, start: new DateTime(2026, 3, 10, 9, 0, 30, DateTimeKind.Utc),
            durationSec: 3010, maxDepthM: 30.2, sourceVendor: "scubapro",
            samples: new[] { Sample(0, 0, temp: 21) });

        var merged = Assert.Single(DiveMergePolicy.Merge(new[] { garmin, scuba }, service.FidelityDelegate()));
        Assert.Equal(scubaId, merged.Primary.Id);
        Assert.Equal("ScubaPro", merged.Badges[0].Label); // priority provider's badge leads
    }
}
