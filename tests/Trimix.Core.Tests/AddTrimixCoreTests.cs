using Microsoft.Extensions.DependencyInjection;
using Trimix.Core.Abstractions;
using Trimix.Core.Billing;
using Trimix.Core.DiveComputer;
using Trimix.Core.Storage;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>The AddTrimixCore DI graph is complete and picks the Debug defaults (sandbox + simulator).</summary>
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
        Assert.NotNull(provider.GetRequiredService<IApiClient>());
        Assert.NotNull(provider.GetRequiredService<IAuthSession>());
        Assert.NotNull(provider.GetRequiredService<IEntitlementService>());
        Assert.NotNull(provider.GetRequiredService<IImportService>());
        Assert.NotNull(provider.GetRequiredService<ISyncService>());
        Assert.NotNull(provider.GetRequiredService<ILocalStore>());
    }

    [Fact]
    public void Debug_defaults_use_sandbox_billing_and_simulator_transport()
    {
        using var provider = Build();

        Assert.IsType<SandboxBillingService>(provider.GetRequiredService<IBillingService>());
        Assert.IsType<SimulatorTransport>(provider.GetRequiredService<IDiveComputerTransport>());
        Assert.IsType<InMemoryLocalStore>(provider.GetRequiredService<ILocalStore>());
    }

    [Fact]
    public void Registers_all_six_vendor_protocols()
    {
        using var provider = Build();
        var protocols = provider.GetServices<IDiveComputerProtocol>().ToList();

        // Shearwater + Suunto + Garmin(placeholder) + Mares + ScubaPro + Aqualung = 6 distinct vendors.
        Assert.Equal(6, protocols.Select(p => p.Vendor).Distinct().Count());
    }

    [Fact]
    public void Registers_the_ble_source_connector()
    {
        using var provider = Build();
        var connectors = provider.GetServices<IDiveSourceConnector>().ToList();

        var ble = Assert.Single(connectors, c => c.Descriptor.Kind == ConnectorKind.BleDevice);
        Assert.Equal(LegalPosture.OwnedHardware, ble.Descriptor.Legal);
        Assert.True(ble.Descriptor.EnabledByDefault);
    }

    [Fact]
    public void Resolves_every_viewmodel()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<OnboardingViewModel>());
        Assert.NotNull(provider.GetRequiredService<AuthViewModel>());
        Assert.NotNull(provider.GetRequiredService<LogbookViewModel>());
        Assert.NotNull(provider.GetRequiredService<DiveDetailViewModel>());
        Assert.NotNull(provider.GetRequiredService<StatsViewModel>());
        Assert.NotNull(provider.GetRequiredService<ManualEntryViewModel>());
        Assert.NotNull(provider.GetRequiredService<ImportViewModel>());
        Assert.NotNull(provider.GetRequiredService<PaywallViewModel>());
        Assert.NotNull(provider.GetRequiredService<SitesViewModel>());
        Assert.NotNull(provider.GetRequiredService<SiteDetailViewModel>());
        Assert.NotNull(provider.GetRequiredService<ContributeViewModel>());
        Assert.NotNull(provider.GetRequiredService<SettingsViewModel>());
    }

    [Fact]
    public void Configured_database_path_switches_to_the_sqlite_store()
    {
        using var provider = Build(o => o.LocalDatabasePath =
            Path.Combine(AppContext.BaseDirectory, $"trimix-di-{Guid.NewGuid():N}.db3"));

        Assert.IsType<SqliteLocalStore>(provider.GetRequiredService<ILocalStore>());
    }

    [Fact]
    public void Reverse_eng_connector_families_are_dark_by_default_and_not_even_registered()
    {
        // The deploy master switches default OFF: with no legal/BD sign-off, the reverse-eng cloud + push
        // families are not registered at all — the documented gate ON TOP OF the per-connector consent gate.
        using var provider = Build();

        Assert.DoesNotContain(
            provider.GetServices<IDiveSourceConnector>(),
            c => c.Descriptor.Legal == LegalPosture.ToSRiskReverseEng);
        Assert.DoesNotContain(
            provider.GetServices<IDiveDestinationConnector>(),
            c => c.Descriptor.Legal == LegalPosture.ToSRiskReverseEng);
    }

    [Fact]
    public void EnableCloudConnectors_flag_registers_the_three_reverse_eng_cloud_sources()
    {
        using var provider = Build(o => o.EnableCloudConnectors = true);

        var reverseEngSources = provider.GetServices<IDiveSourceConnector>()
            .Where(c => c.Descriptor.Legal == LegalPosture.ToSRiskReverseEng)
            .ToList();
        Assert.Equal(3, reverseEngSources.Count); // Garmin + Suunto + Oceanic
        // The push family stays dark — the flags are independent.
        Assert.DoesNotContain(
            provider.GetServices<IDiveDestinationConnector>(),
            c => c.Descriptor.Legal == LegalPosture.ToSRiskReverseEng);
    }

    [Fact]
    public void EnablePushConnectors_flag_registers_the_two_reverse_eng_push_destinations()
    {
        using var provider = Build(o => o.EnablePushConnectors = true);

        var reverseEngDestinations = provider.GetServices<IDiveDestinationConnector>()
            .Where(c => c.Descriptor.Legal == LegalPosture.ToSRiskReverseEng)
            .ToList();
        Assert.Equal(2, reverseEngDestinations.Count); // PADI + SSI
        Assert.DoesNotContain(
            provider.GetServices<IDiveSourceConnector>(),
            c => c.Descriptor.Legal == LegalPosture.ToSRiskReverseEng);
    }
}
