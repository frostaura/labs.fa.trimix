namespace Trimix.App.Controls;

/// <summary>
/// The one place the dive-profile chart's plot geometry is defined. Before this existed the plot inset lived in
/// three places — the drawable, the page's touch→chart mapping, and a hand-computed axis-label margin — and the
/// axis labels sat ~2 px off the gridlines they annotated because two of the three disagreed.
///
/// <para>Referenced from XAML via <c>x:Static</c> and from both drawables in C#, so the plot rect, the touch
/// mapping and the label column can no longer drift apart.</para>
/// </summary>
public static class ChartMetrics
{
    /// <summary>
    /// Plot inset on all four sides of the depth canvas. The page's touch→chart mapping and the axis label
    /// column's margin both derive from this — never re-type the number.
    /// </summary>
    public const float PlotPad = 8f;

    /// <summary>Vertical inset of the temperature lane's plot (tighter than the main chart, which has markers).</summary>
    public const float LanePad = 6f;

    /// <summary>
    /// Half the rendered line box of an axis label (JetBrainsMonoMedium at font size 9 measures ~12 px), used
    /// to convert "align the label's edge" into "align the label's centre".
    /// </summary>
    private const double AxisLabelHalfLine = 6;

    /// <summary>Width of the right-hand depth-axis label column — fits "28.4 m" and "93.2 ft" without clipping.</summary>
    public const double AxisLabelColumnWidth = 44;

    /// <summary>
    /// Top/bottom margin that puts an axis label's vertical CENTRE on the plot's surface / floor hairline.
    /// The hairlines sit <see cref="PlotPad"/> inside the canvas, and a label is positioned by its edge, so the
    /// column has to be pulled in by the pad minus half a line box.
    /// </summary>
    public static readonly Thickness AxisLabelInset =
        new(0, PlotPad - AxisLabelHalfLine, 0, PlotPad - AxisLabelHalfLine);
}
