using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Blazor analogue of the canvas render-transform attached behavior used by the XAML adapters.
/// Because Blazor has no element property system, this exposes the translate offset as data and
/// as a CSS transform string for use in element styles. Consumers that draw nodes/links in world
/// coordinates can subtract <see cref="GetOffset"/> to align with a translated canvas.
/// </summary>
public static class WorkflowCanvasTransformBehavior
{
    /// <summary>
    /// Gets the current content translate offset for the workflow tree (its <see cref="CanvasLayout.ActualOffset"/>).
    /// </summary>
    public static Offset GetOffset(IWorkflowTreeViewModel tree)
        => tree.Layout?.ActualOffset ?? new Offset();

    /// <summary>
    /// Renders a translate offset as a CSS <c>transform</c> value, e.g. <c>translate(0px, 0px)</c>.
    /// </summary>
    /// <remarks>
    /// The result is always invariant-formatted, because it is meant to be written into a <c>style</c> attribute:
    /// under a culture whose decimal separator is a comma the browser would drop the whole declaration.
    /// </remarks>
    public static string ToCss(Offset offset)
        => FormattableString.Invariant(
            $"translate({offset.Horizontal:0.###}px, {offset.Vertical:0.###}px)");

    /// <summary>
    /// Renders the workflow tree's content translate offset as a CSS <c>transform</c> value.
    /// </summary>
    public static string GetTransformStyle(IWorkflowTreeViewModel tree)
        => ToCss(GetOffset(tree));
}
