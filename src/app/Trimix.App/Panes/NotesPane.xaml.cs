namespace Trimix.App.Panes;

/// <summary>The diver's own notes: view / edit / empty. Pure markup apart from the scroll reset below.</summary>
public partial class NotesPane : ContentView, IDivePane
{
    public NotesPane() => InitializeComponent();

    /// <inheritdoc />
    public void ScrollToTop() => Scroller.ScrollToAsync(0, 0, false);
}
