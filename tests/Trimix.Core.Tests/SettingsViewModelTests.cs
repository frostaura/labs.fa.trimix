using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Api;
using Trimix.Core.Platform;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Connectors.Export;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// Settings privacy actions. Pins the GDPR erasure contract: "Delete my data" for a signed-in user calls
/// the live server erasure endpoint (/privacy/delete) BEFORE sign-out — so the account, analytics, and
/// receipt purchase-tokens are actually erased server-side, not just the local dives — and refuses to sign
/// out (leaving data intact) when the server call fails.
/// </summary>
public sealed class SettingsViewModelTests
{
    private static (SettingsViewModel Vm, FakeAuthSession Session, FakeApiClient Api, InMemoryLocalStore Store, FakeDialogs Dialogs, RecordingConnectorPurgeService Purge)
        NewSut(bool authenticated)
    {
        var session = new FakeAuthSession();
        if (authenticated)
        {
            session.IsAuthenticated = true;
            session.CurrentUser = new UserDto(Guid.NewGuid(), "diver@example.com", UserRoles.User, true, DateTime.UtcNow);
        }

        var api = new FakeApiClient();
        var store = new InMemoryLocalStore();
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var purge = new RecordingConnectorPurgeService();
        var vm = new SettingsViewModel(
            session,
            new FakeEntitlementService(),
            new InMemoryPreferences(),
            store,
            api,
            new FakeAppInfo(),
            new FakeNavigator(),
            dialogs,
            purge);
        return (vm, session, api, store, dialogs, purge);
    }

    [Fact]
    public async Task DeleteMyData_signed_in_erases_server_side_then_signs_out()
    {
        var (vm, session, api, store, dialogs, purge) = NewSut(authenticated: true);
        await store.UpsertDiveAsync(DiveFixture.Trimix());

        await vm.DeleteMyDataCommand.ExecuteAsync(null);

        Assert.Equal(1, api.DeleteMyDataCalls);           // the live /privacy/delete endpoint was hit…
        Assert.False(session.IsAuthenticated);            // …and only then did we sign out
        Assert.Equal(1, purge.PurgeAllCalls);             // …and the connector consent+tokens were purged too
        Assert.Contains("Your data has been deleted.", dialogs.Toasts);
        Assert.All(await store.GetDivesAsync(), d => Assert.True(d.Deleted)); // local dives wiped
    }

    [Fact]
    public async Task DeleteMyData_does_not_sign_out_when_server_erasure_fails()
    {
        var (vm, session, api, store, dialogs, purge) = NewSut(authenticated: true);
        await store.UpsertDiveAsync(DiveFixture.Trimix());
        api.DeleteMyDataThrows = new HttpRequestException("offline");

        await vm.DeleteMyDataCommand.ExecuteAsync(null);

        Assert.Equal(1, api.DeleteMyDataCalls);
        Assert.True(session.IsAuthenticated);             // erasure failed => stay signed in, nothing lost locally
        Assert.Equal(0, purge.PurgeAllCalls);             // …and no connector purge (we returned before sign-out)
        Assert.All(await store.GetDivesAsync(), d => Assert.False(d.Deleted));
        Assert.Contains(dialogs.Alerts, a => a.Title == "Delete failed");
        Assert.DoesNotContain("Your data has been deleted.", dialogs.Toasts);
    }

    [Fact]
    public async Task DeleteMyData_guest_wipes_local_without_a_server_call()
    {
        var (vm, session, api, store, dialogs, purge) = NewSut(authenticated: false);
        await store.UpsertDiveAsync(DiveFixture.Trimix());

        await vm.DeleteMyDataCommand.ExecuteAsync(null);

        Assert.Equal(0, api.DeleteMyDataCalls);           // a guest has no server-side account to erase
        Assert.Equal(1, purge.PurgeAllCalls);             // but a guest may have connected vendors — still purge
        Assert.All(await store.GetDivesAsync(), d => Assert.True(d.Deleted));
        Assert.Contains("Your data has been deleted.", dialogs.Toasts);
    }

    [Fact]
    public async Task DeleteMyData_declined_does_nothing()
    {
        var (vm, session, api, store, dialogs, purge) = NewSut(authenticated: true);
        dialogs.ConfirmResult = false;

        await vm.DeleteMyDataCommand.ExecuteAsync(null);

        Assert.Equal(0, api.DeleteMyDataCalls);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(0, purge.PurgeAllCalls);             // declined => nothing erased or purged
        Assert.Empty(dialogs.Toasts);
    }

    [Fact]
    public async Task SignOut_awaits_the_connector_purge()
    {
        var (vm, session, _, _, dialogs, purge) = NewSut(authenticated: true);

        await vm.SignOutCommand.ExecuteAsync(null);

        Assert.False(session.IsAuthenticated);
        Assert.Equal(1, purge.PurgeAllCalls);             // the flow AWAITS the purge (Finding 2a), not fire-and-forget
        Assert.Contains("Signed out.", dialogs.Toasts);
    }
}
