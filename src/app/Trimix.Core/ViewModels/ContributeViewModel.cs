using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Api;
using Trimix.Core.Entitlements;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Site contribution / correction (PRD Flow C): Pro-gated, enters the server-side moderation
/// queue as <c>pending</c>, and the UI shows the public-after-review disclosure BEFORE submit
/// (PRD privacy posture: "site contributions are public-by-design and the UI states this").
/// Fires <c>site_contribution_submitted</c>; the moderator decision fires
/// <c>site_contribution_resolved</c> server-side.
/// </summary>
public partial class ContributeViewModel(
    IApiClient api,
    IEntitlementService entitlements,
    IAnalytics analytics,
    INavigator navigator,
    IUserDialogs dialogs) : ObservableObject
{
    /// <summary>Shown above the form and repeated on the submit button's confirmation.</summary>
    public const string PublicAfterReviewDisclosure =
        "Contributions are reviewed by a moderator and become PUBLIC to all divers once approved.";

    /// <summary>Web (camelCase) JSON so the serialized keys match the server's ContributionPayload exactly.</summary>
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Null proposes a brand-new site; a value suggests a correction to that site.</summary>
    [ObservableProperty]
    private Guid? _siteId;

    [ObservableProperty]
    private string _note = "";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Back navigation, wired to the page's Back button.</summary>
    [RelayCommand]
    private Task Close() => navigator.GoBackAsync();

    [RelayCommand]
    private async Task SubmitAsync()
    {
        // Defensive re-check; the server enforces this authoritatively too (overview §9).
        if (!EntitlementGate.CanContributeSites(entitlements.CurrentTier))
        {
            analytics.Track(FunnelEvents.PaywallShown, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Trigger] = FunnelEvents.Values.TriggerSiteContribute,
                [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
            });
            await navigator.GoToAsync(Routes.Paywall);
            return;
        }

        if (string.IsNullOrWhiteSpace(Note))
        {
            await dialogs.AlertAsync("Nothing to submit", "Describe the site or the correction first.");
            return;
        }

        IsBusy = true;
        try
        {
            // Serialize to the shared contract shape (communityNote/...); the server's payload parser
            // reads these camelCase keys — a bare {"note":...} would deserialize to all-nulls.
            var payload = JsonSerializer.Serialize(
                new SiteContributionPayload(CommunityNote: Note.Trim()),
                PayloadJsonOptions);
            var response = await api.SubmitContributionAsync(new SiteContributionRequest(SiteId, payload));
            analytics.Track(FunnelEvents.SiteContributionSubmitted, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Status] = response.Status,
            });
            await dialogs.ToastAsync("Submitted for review — it goes public once a moderator approves it.");
            await navigator.GoBackAsync();
        }
        catch (ApiException ex)
        {
            await dialogs.AlertAsync("Could not submit", ex.Error.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
