using Demo.ViewModels;
using Jalium.UI;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Generic port layout for the full demo's Common/Lib node view-models, matching the NodeEditorSurface's
/// card geometry: a single input is vertically centered on the left edge; multiple inputs and all outputs
/// are laid out as rows under the title bar. This class exposes DESIGN (scale-1) local centers; the surface
/// turns them into world centers as <c>node.Anchor + designLocal·s</c> with <c>s = node.Size/DesignSize</c>
/// (the same factor as the card's RenderTransform), so links and hit-testing land on the scaled port dots
/// under workspace zoom.
/// </summary>
internal static class NodePorts
{
    public const double TitleBarH = 36;
    public const double RowH = 26;
    public const double InputPortX = 10;
    public const double OutputInset = 15;

    /// <summary>Input ports of a node, with their display names (empty for unnamed single slots).</summary>
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Inputs(IWorkflowNodeViewModel node)
    {
        if (node is PythonScriptNodeViewModel python)
        {
            return python.InputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
        }

        var single = SingleSlot(node, "InputSlot");
        return single is { } s ? [(s, string.Empty)] : [];
    }

    /// <summary>Output ports of a node, with their display names (empty for unnamed single slots).</summary>
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Outputs(IWorkflowNodeViewModel node)
    {
        switch (node)
        {
            case PythonScriptNodeViewModel python:
                return python.OutputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
            case EnumSelectorNodeViewModel e:
                return e.OutputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
        }

        var single = SingleSlot(node, "OutputSlot");
        return single is { } s ? [(s, string.Empty)] : [];
    }

    /// <summary>Display title of a node (the generated Title property on the Common/Lib VMs).</summary>
    public static string TitleOf(IWorkflowNodeViewModel node)
        => node.GetType().GetProperty("Title")?.GetValue(node)?.ToString() ?? string.Empty;

    /// <summary>Vertical space under the title bar that port rows may occupy at the DESIGN size (a small
    /// bottom gap is reserved so packed rows do not touch the card's lower edge).</summary>
    public static double RowUsableHeight(double designHeight)
        => System.Math.Max(0, designHeight - TitleBarH - 6);

    /// <summary>Vertical pitch between consecutive port rows so an arbitrary number of dynamic rows stays
    /// on the card: the fixed <see cref="RowH"/> when the rows fit under the title bar, otherwise the pitch
    /// is compressed to the usable height so no row is pushed below the card (best-effort packing).</summary>
    public static double RowPitchFor(int count, double designHeight)
    {
        if (count <= 0)
        {
            return RowH;
        }

        double usable = RowUsableHeight(designHeight);
        return count * RowH <= usable ? RowH : System.Math.Max(4, usable / count);
    }

    /// <summary>Input port center at the DESIGN size (used to compute the scaled world center).</summary>
    public static Point InputCenterLocalDesign(IWorkflowNodeViewModel node, int i, double designHeight)
    {
        int count = Inputs(node).Count;
        if (count <= 1)
        {
            return new Point(InputPortX, designHeight / 2);
        }

        double pitch = RowPitchFor(count, designHeight);
        return new Point(InputPortX, TitleBarH + pitch * i + pitch / 2);
    }

    /// <summary>Output port center at the DESIGN size (used to compute the scaled world center).</summary>
    public static Point OutputCenterLocalDesign(IWorkflowNodeViewModel node, int i, double designWidth, double designHeight)
    {
        int count = Outputs(node).Count;
        double pitch = RowPitchFor(count, designHeight);
        double y = TitleBarH + pitch * i + pitch / 2;
        return new Point(designWidth - OutputInset, y);
    }

    /// <summary>Finds whether a slot is an input or output of its node and its index among that list.</summary>
    public static (bool IsInput, int Index)? IndexOf(IWorkflowNodeViewModel node, IWorkflowSlotViewModel slot)
    {
        var inputs = Inputs(node);
        for (int i = 0; i < inputs.Count; i++)
        {
            if (ReferenceEquals(inputs[i].Slot, slot))
            {
                return (true, i);
            }
        }

        var outputs = Outputs(node);
        for (int i = 0; i < outputs.Count; i++)
        {
            if (ReferenceEquals(outputs[i].Slot, slot))
            {
                return (false, i);
            }
        }

        return null;
    }

    private static IWorkflowSlotViewModel? SingleSlot(IWorkflowNodeViewModel node, string propertyName)
        => node.GetType().GetProperty(propertyName)?.GetValue(node) as IWorkflowSlotViewModel;
}
