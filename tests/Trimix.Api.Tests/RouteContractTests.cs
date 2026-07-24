using Trimix.Contracts;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// Placeholder anchor tests. WebApplicationFactory integration tests (SQLite in-memory),
/// including the end-to-end funnel reconciliation, land with the implementations.
/// </summary>
public class RouteContractTests
{
    [Fact]
    public void Route_constants_match_the_architecture_api_table()
    {
        Assert.Equal("/api/v1/healthz", ApiRoutes.HealthzFull);
        Assert.Equal("/api/v1/auth/register", ApiRoutes.Auth.RegisterFull);
        Assert.Equal("/api/v1/auth/login", ApiRoutes.Auth.LoginFull);
        Assert.Equal("/api/v1/entitlements/me", ApiRoutes.Entitlements.MeFull);
        Assert.Equal("/api/v1/purchases/validate", ApiRoutes.Purchases.ValidateFull);
        Assert.Equal("/api/v1/dives/sync", ApiRoutes.Dives.SyncFull);
        Assert.Equal("/api/v1/sites", ApiRoutes.Sites.SearchFull);
        Assert.Equal("/api/v1/sites/{id}", ApiRoutes.Sites.ByIdTemplateFull);
        Assert.Equal("/api/v1/sites/contributions", ApiRoutes.Sites.ContributionsFull);
        Assert.Equal("/api/v1/moderation/contributions", ApiRoutes.Moderation.ContributionsFull);
        Assert.Equal("/api/v1/moderation/contributions/{id}/resolve", ApiRoutes.Moderation.ResolveTemplateFull);
        Assert.Equal("/api/v1/analytics/events", ApiRoutes.Analytics.EventsFull);
    }
}
