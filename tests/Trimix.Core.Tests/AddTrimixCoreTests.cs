using Microsoft.Extensions.DependencyInjection;
using Trimix.Core.Abstractions;
using Trimix.Core.Billing;
using Trimix.Core.Storage;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>The AddTrimixCore DI graph is complete and picks the Debug defaults (sandbox billing).</summary>
public class AddTrimixCoreTests
{
    private static ServiceProvider Build(Action<TrimixCoreOptions>? configure = null) =>
        new ServiceCollection().AddTrimixCore(configure).BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

    [Fact]
    public void Resolves_the_full_service_graph()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<AppStartupService>());
        Assert.NotNull(provider.GetRequiredService<IAnalytics>());
        Assert.NotNull(provider.GetRequiredService<IEntitlementService>());
        Assert.NotNull(provider.GetRequiredService<IImportService>());
        Assert.NotNull(provider.GetRequiredService<ILocalStore>());
    }

    [Fact]
    public void Debug_defaults_use_sandbox_billing()
    {
        using var provider = Build();

        Assert.IsType<SandboxBillingService>(provider.GetRequiredService<IBillingService>());
        Assert.IsType<InMemoryLocalStore>(provider.GetRequiredService<ILocalStore>());
    }

    [Fact]
    public void All_provider_connectors_register_by_default()
    {
        // Serverless rebuild: the connectors ARE the product surface, so the deploy master switches default
        // ON and all providers resolve (6 sources IN — incl. the dark "Coming soon" Shearwater row — + 2
        // destinations OUT) for the Providers list. Per-user consent behind each disclosure remains the only
        // thing that permits network I/O.
        using var provider = Build();

        var sourceIds = provider.GetServices<IDiveSourceConnector>()
            .Select(c => c.Descriptor.ConnectorId)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var destinationIds = provider.GetServices<IDiveDestinationConnector>()
            .Select(c => c.Descriptor.ConnectorId)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.Equal(["garmin_connect", "mares", "oceanic_cloud", "scubapro", "shearwater", "suunto_cloud"], sourceIds);
        Assert.Equal(["padi_push_personal", "ssi_push_personal"], destinationIds);
    }

    [Fact]
    public void Every_registered_reverse_eng_connector_keeps_the_dark_descriptor_invariant()
    {
        // The promotion to default registration must NEVER relax the structural invariant: every
        // ToSRiskReverseEng descriptor carries EnabledByDefault=false — availability comes from
        // registration + per-user consent, not from the descriptor flag.
        using var provider = Build();

        Assert.All(
            provider.GetServices<IDiveSourceConnector>().Where(c => c.Descriptor.Legal == LegalPosture.ToSRiskReverseEng),
            c => Assert.False(c.Descriptor.EnabledByDefault));
        Assert.All(
            provider.GetServices<IDiveDestinationConnector>().Where(c => c.Descriptor.Legal == LegalPosture.ToSRiskReverseEng),
            c => Assert.False(c.Descriptor.EnabledByDefault));
    }

    [Fact]
    public void Resolves_the_sync_coordinator()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<ISyncCoordinator>());
    }

    [Fact]
    public void Resolves_every_viewmodel()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<OnboardingViewModel>());
        Assert.NotNull(provider.GetRequiredService<LogbookViewModel>());
        Assert.NotNull(provider.GetRequiredService<DiveDetailViewModel>());
        Assert.NotNull(provider.GetRequiredService<StatsViewModel>());
        Assert.NotNull(provider.GetRequiredService<PaywallViewModel>());
    }

    [Fact]
    public void Configured_database_path_switches_to_the_sqlite_store()
    {
        using var provider = Build(o => o.LocalDatabasePath =
            Path.Combine(AppContext.BaseDirectory, $"trimix-di-{Guid.NewGuid():N}.db3"));

        Assert.IsType<SqliteLocalStore>(provider.GetRequiredService<ILocalStore>());
    }

    [Fact]
    public void Disabling_EnableCloudConnectors_unregisters_the_source_family_entirely()
    {
        // The master switch remains the deploy kill-switch: OFF ⇒ the family is not registered at all, so
        // it never surfaces in the providers UI, can never be consented, and sync can never pull it.
        using var provider = Build(o => o.EnableCloudConnectors = false);

        Assert.Empty(provider.GetServices<IDiveSourceConnector>());
        // The push family is independent and stays registered.
        Assert.Equal(2, provider.GetServices<IDiveDestinationConnector>().Count());
    }

    [Fact]
    public void Disabling_EnablePushConnectors_unregisters_the_push_family_entirely()
    {
        using var provider = Build(o => o.EnablePushConnectors = false);

        Assert.Empty(provider.GetServices<IDiveDestinationConnector>());
        // The cloud family is independent and stays registered (all six sources, incl. dark Shearwater).
        Assert.Equal(6, provider.GetServices<IDiveSourceConnector>().Count());
    }
}
