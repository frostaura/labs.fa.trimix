using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Platform;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Connectors.Push;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// Settings privacy actions, serverless: everything lives on-device, so "Delete my data" is a local
/// wipe — dives, session, and every connector consent+token (awaited, never fire-and-forget).
/// </summary>
public sealed class SettingsViewModelTests
{
    private static (SettingsViewModel Vm, FakeAuthSession Session, InMemoryLocalStore Store, FakeDialogs Dialogs, RecordingConnectorPurgeService Purge)
        NewSut(bool authenticated)
    {
        var session = new FakeAuthSession();
        if (authenticated)
        {
            session.IsAuthenticated = true;
            session.CurrentUser = new UserDto(Guid.NewGuid(), "diver@example.com", UserRoles.User, true, DateTime.UtcNow);
        }

        var store = new InMemoryLocalStore();
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var purge = new RecordingConnectorPurgeService();
        var vm = new SettingsViewModel(
            session,
            new FakeEntitlementService(),
            new InMemoryPreferences(),
            store,
            new FakeAppInfo(),
            new FakeNavigator(),
            dialogs,
            purge);
        return (vm, session, store, dialogs, purge);
    }

    [Fact]
    public async Task DeleteMyData_wipes_local_data_signs_out_and_purges_connectors()
    {
        var (vm, session, store, dialogs, purge) = NewSut(authenticated: true);
        await store.UpsertDiveAsync(DiveFixture.Trimix());

        await vm.DeleteMyDataCommand.ExecuteAsync(null);

        Assert.False(session.IsAuthenticated);            // local wipe then sign-out
        Assert.Equal(1, purge.PurgeAllCalls);             // connector consent+tokens purged too (awaited)
        Assert.Contains("Your data has been deleted.", dialogs.Toasts);
        Assert.All(await store.GetDivesAsync(), d => Assert.True(d.Deleted)); // local dives wiped
    }

    [Fact]
    public async Task DeleteMyData_guest_wipes_local_and_purges()
    {
        var (vm, session, store, dialogs, purge) = NewSut(authenticated: false);
        await store.UpsertDiveAsync(DiveFixture.Trimix());

        await vm.DeleteMyDataCommand.ExecuteAsync(null);

        Assert.Equal(1, purge.PurgeAllCalls);             // a guest may have connected vendors — still purge
        Assert.All(await store.GetDivesAsync(), d => Assert.True(d.Deleted));
        Assert.Contains("Your data has been deleted.", dialogs.Toasts);
    }

    [Fact]
    public async Task DeleteMyData_declined_does_nothing()
    {
        var (vm, session, store, dialogs, purge) = NewSut(authenticated: true);
        await store.UpsertDiveAsync(DiveFixture.Trimix());
        dialogs.ConfirmResult = false;

        await vm.DeleteMyDataCommand.ExecuteAsync(null);

        Assert.True(session.IsAuthenticated);
        Assert.Equal(0, purge.PurgeAllCalls);             // declined => nothing erased or purged
        Assert.All(await store.GetDivesAsync(), d => Assert.False(d.Deleted));
        Assert.Empty(dialogs.Toasts);
    }

    [Fact]
    public async Task SignOut_awaits_the_connector_purge()
    {
        var (vm, session, _, dialogs, purge) = NewSut(authenticated: true);

        await vm.SignOutCommand.ExecuteAsync(null);

        Assert.False(session.IsAuthenticated);
        Assert.Equal(1, purge.PurgeAllCalls);             // the flow AWAITS the purge (Finding 2a), not fire-and-forget
        Assert.Contains("Signed out.", dialogs.Toasts);
    }
}
