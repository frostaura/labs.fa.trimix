using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Platform;
using Trimix.Core.Tests.Connectors.Cloud;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests.ViewModels;

/// <summary>
/// Haptic outcome cues. These are pinned because the design removed toasts, which makes the buzz the
/// only non-visual report of an outcome the diver may not be looking at — a failed sync writes "Sync
/// failed" into a sheet that closes itself, and a rejected sign-in is one alert on a page the diver is
/// already staring at. The cue vocabulary is also asserted here rather than "some haptic fired": firing
/// Success on a capped run, or Warning on a real failure, is a wrong answer, not a lesser one.
/// </summary>
public class HapticOutcomeTests
{
    private readonly FakeSyncCoordinator _coordinator = new();
    private readonly InMemoryConnectorConsentStore _consent = new();
    private readonly FakeNavigator _navigator = new();
    private readonly RecordingHaptics _haptics = new();

    private SyncSessionViewModel NewSyncVm() => new(
        _coordinator, [], _consent, _navigator, new InMemoryPreferences(), new FakeClock(), _haptics)
    {
        AutoSyncDelayMs = 0,
        DoneLingerMs = 0,
    };

    [Fact]
    public async Task A_completed_sync_reports_success()
    {
        _coordinator.HasLinkedSourcesValue = true;
        _coordinator.NextReport = new SyncReport(
            [new SourceSyncOutcome("garmin_connect", ConnectorAuthStatus.Granted, Imported: 4, Fetched: 4, CapReached: false)],
            [],
            [],
            CapReached: false);

        await NewSyncVm().SyncNowCommand.ExecuteAsync(null);

        Assert.Equal(HapticCue.Success, Assert.Single(_haptics.Cues));
    }

    [Fact]
    public async Task A_capped_sync_reports_warning_not_success()
    {
        _coordinator.HasLinkedSourcesValue = true;
        _coordinator.NextReport = new SyncReport(
            [new SourceSyncOutcome("garmin_connect", ConnectorAuthStatus.Granted, Imported: 5, Fetched: 8, CapReached: true)],
            [],
            [],
            CapReached: true);

        await NewSyncVm().SyncNowCommand.ExecuteAsync(null);

        // The run technically succeeded, but it ended by refusing to import 3 dives and immediately
        // navigates to the paywall. Success would tell the diver the opposite of what happened.
        Assert.Equal(HapticCue.Warning, Assert.Single(_haptics.Cues));
        Assert.DoesNotContain(HapticCue.Success, _haptics.Cues);
    }

    [Fact]
    public async Task A_failed_sync_reports_error()
    {
        var vm = new SyncSessionViewModel(
            new ThrowingCoordinator(), [], _consent, _navigator, new InMemoryPreferences(), new FakeClock(),
            _haptics)
        {
            DoneLingerMs = 0,
        };

        await vm.SyncNowCommand.ExecuteAsync(null);

        Assert.Equal(HapticCue.Error, Assert.Single(_haptics.Cues));
    }

    [Fact]
    public async Task A_granted_provider_sign_in_reports_success_and_a_rejected_one_reports_error()
    {
        var http = new RecordedHttpTransport();
        var tokens = new InMemorySecureTokenStore();
        var consent = new PreferencesConnectorConsentStore(new InMemoryPreferences(), tokens);
        _coordinator.HasLinkedSourcesFunc = () => consent.HasConsent("garmin_connect");

        var session = new SyncSessionViewModel(
            _coordinator, [], consent, _navigator, new InMemoryPreferences(), new FakeClock())
        {
            AutoSyncDelayMs = 0,
            DoneLingerMs = 0,
        };

        // Garmin answers the login with a rejection, so sign-in cannot reach the ticket exchange.
        http.EnqueueText("{\"error\":\"invalid_credentials\"}", status: 401);
        var vm = new ProviderOAuthViewModel(
            [new GarminCloudSourceConnector(http, tokens, consent)], [], consent, session, _navigator,
            new FakeDialogs { ConfirmResult = true }, _haptics)
        {
            ConnectorId = "garmin_connect",
            Username = "diver@example.com",
            Password = "wrong-password",
        };

        await vm.SignInAndAuthorizeCommand.ExecuteAsync(null);

        Assert.Equal(HapticCue.Error, Assert.Single(_haptics.Cues));
    }

    private sealed class ThrowingCoordinator : ISyncCoordinator
    {
        public bool HasLinkedSources => true;

        public Task<SyncReport> SyncNowAsync(
            Func<Task>? onCapReached = null,
            IProgress<SyncProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider endpoint fell over");
    }
}
