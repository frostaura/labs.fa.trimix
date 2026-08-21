namespace Trimix.App.Panes;

/// <summary>
/// A dive-detail section that can be pinned back to the top of its scroll.
///
/// <para>The panes are static siblings, so each one's ScrollView keeps its offset between dives and between
/// section switches. Without an explicit reset, revealing real content after the loading skeleton could land
/// mid-page — the "bump" a diver sees as the page settling.</para>
/// </summary>
public interface IDivePane
{
    void ScrollToTop();
}
