namespace Trimix.App.Panes;

/// <summary>Recorded computer events and the recorded-channel extremes. Pure markup apart from the scroll reset below.</summary>
public partial class EventsPane : ContentView, IDivePane
{
    public EventsPane() => InitializeComponent();

    /// <inheritdoc />
    public void ScrollToTop() => Scroller.ScrollToAsync(0, 0, false);
}
