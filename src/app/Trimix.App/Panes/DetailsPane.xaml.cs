namespace Trimix.App.Panes;

/// <summary>Provider-reported detail cards, tags and the entry-point card. Pure markup apart from the scroll reset below.</summary>
public partial class DetailsPane : ContentView, IDivePane
{
    public DetailsPane() => InitializeComponent();

    /// <inheritdoc />
    public void ScrollToTop() => Scroller.ScrollToAsync(0, 0, false);
}
