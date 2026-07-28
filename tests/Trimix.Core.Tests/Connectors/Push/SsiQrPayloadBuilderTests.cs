using Trimix.Core.Connectors.Push;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Push;

/// <summary>
/// The zero-network, diver-mediated MySSI QR payload: a semicolon-delimited <c>key:value</c> blob carrying
/// date/time, duration (min) and max depth (m) only. SSI-internal ids (site, user_master) are deliberately
/// omitted because a third party cannot obtain them, and no profile series is carried — summary-only.
/// </summary>
public class SsiQrPayloadBuilderTests
{
    [Fact]
    public void Build_emits_the_summary_only_semicolon_payload()
    {
        var payload = new SsiQrPayloadBuilder().Build(DiveFixture.Trimix());
        var parts = payload.Split(';');

        Assert.Equal("dive", parts[0]);
        Assert.Equal("noid", parts[1]);
        Assert.Contains("dive_type:0", parts);
        Assert.Contains("datetime:202606010830", parts); // 2026-06-01 08:30 UTC
        Assert.Contains("divetime:53", parts);            // 3180s -> 53 min
        Assert.Contains("depth_m:29", parts);             // 28.5 m -> 29 (rounded away from zero)
    }

    [Fact]
    public void Build_omits_unobtainable_ssi_ids_and_all_profile_detail()
    {
        var payload = new SsiQrPayloadBuilder().Build(DiveFixture.Trimix());

        Assert.DoesNotContain("site", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user_master", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("temp", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profile", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_null_dive_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SsiQrPayloadBuilder().Build(null!));
    }
}
