using Trimix.Core.ViewModels;

namespace Trimix.App.Panes;

/// <summary>
/// Maps a <see cref="DivePaneKind"/> to its pane view.
///
/// <para>A selector with one template per pane, rather than a single template gated by index, so the carousel
/// only ever instantiates the pane it is showing. The onboarding flow uses the gated-single-template pattern,
/// but that would build all four panes — including the chart's two canvases — for every carousel item.</para>
/// </summary>
public sealed class DivePaneTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Profile { get; set; }

    public DataTemplate? Details { get; set; }

    public DataTemplate? Events { get; set; }

    public DataTemplate? Notes { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        (item switch
        {
            DivePaneKind.Details => Details,
            DivePaneKind.Events => Events,
            DivePaneKind.Notes => Notes,
            _ => Profile,
        })
        ?? Profile
        ?? throw new InvalidOperationException("DivePaneTemplateSelector needs at least a Profile template.");
}
