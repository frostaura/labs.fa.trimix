using Trimix.Contracts.Dtos;
using Trimix.Core.Charts;
using Xunit;

namespace Trimix.Core.Tests.Charts;

/// <summary>
/// The accessible profile table is rendered by a BindableLayout, which materializes every item even while
/// collapsed. A cloud-synced 1 Hz profile is ~5,600 samples for a 100-minute dive, so one row per sample
/// created tens of thousands of views on the UI thread and wedged the detail page for a minute or more
/// (observed on a real Garmin dive). These tests pin the downsampling that fixed it.
/// </summary>
public class ProfileTableDownsampleTests
{
    private static IReadOnlyList<ProfileSampleDto> Profile(int count, int deepestAt) =>
        [.. Enumerable.Range(0, count).Select(i => new ProfileSampleDto(
            TSeconds: i,
            DepthM: i == deepestAt ? 40.0 : 5.0 + (i % 10) * 0.1,
            TempC: 26.0,
            PpO2: 0.4))];

    [Fact]
    public void A_realistic_1hz_profile_is_capped()
    {
        var table = ChartGeometry.BuildProfileTable(Profile(5581, deepestAt: 5216));

        Assert.InRange(table.Rows.Count, 2, 150);
    }

    [Fact]
    public void A_short_profile_is_left_untouched()
    {
        var table = ChartGeometry.BuildProfileTable(Profile(20, deepestAt: 7));

        Assert.Equal(20, table.Rows.Count);
    }

    [Fact]
    public void Downsampling_keeps_the_first_last_and_deepest_samples()
    {
        // Losing the deepest point would understate the dive in the text alternative that a screen-reader
        // user relies on, so the extremes are retained rather than evenly sampled away.
        var samples = Profile(5581, deepestAt: 5216);
        var table = ChartGeometry.BuildProfileTable(samples);

        var elapsed = table.Rows.Select(r => int.Parse(r[0], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(0, elapsed[0]);
        Assert.Equal(5580, elapsed[^1]);
        Assert.Contains(5216, elapsed);
        // Still ordered, so the table reads chronologically.
        Assert.Equal(elapsed.OrderBy(e => e), elapsed);
    }
}
