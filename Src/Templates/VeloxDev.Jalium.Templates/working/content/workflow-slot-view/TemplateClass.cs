using System.Collections;
using Jalium.UI;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>Port layout for the node-editor surface (authoritative model, the Jalium NodeEditorDemo):
/// geometry constants and generic input/output slot enumeration computed from the node's Anchor/Size,
/// so the surface's cards, link endpoints and hit-testing share one source of truth.</summary>
public static class TemplateClass
{
    public const double TitleBarH = 36;
    public const double RowH = 26;
    public const double InputPortX = 10;
    public const double OutputInset = 15;

    /// <summary>Design (scale-1) card size — the node's Size at Layout.Scale 1. The card is drawn at this
    /// size inside a Viewbox scaled to the collapsed box, so content (and ports) shrink by 1/scale.</summary>
    public const double DesignWidth = 260;
    public const double DesignHeight = 180;

    /// <summary>Input slots of a node, with their display names.</summary>
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Inputs(IWorkflowNodeViewModel node)
    {
        var single = SingleSlot(node, "InputSlot");
        return single is { } s ? [(s, string.Empty)] : [];
    }

    /// <summary>Output slots of a node, with their display names.</summary>
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Outputs(IWorkflowNodeViewModel node)
    {
        // A node may expose a SlotEnumerator (e.g. OutputSlots): enumerate its ConditionalSlot items —
        // one output per selected enum/bool member, labeled by member name. The enumerator is not on
        // the IWorkflowNodeViewModel interface, so resolve it reflectively by property name.
        var outputSlots = node.GetType().GetProperty("OutputSlots")?.GetValue(node);
        if (outputSlots is not null
            && outputSlots.GetType().GetProperty("Items")?.GetValue(outputSlots) is IEnumerable items)
        {
            var result = new List<(IWorkflowSlotViewModel Slot, string Name)>();
            foreach (var item in items)
            {
                var itemType = item.GetType();
                var slot = itemType.GetProperty("Slot")?.GetValue(item) as IWorkflowSlotViewModel;
                var name = itemType.GetProperty("Name")?.GetValue(item)?.ToString() ?? string.Empty;
                if (slot is not null) result.Add((slot, name));
            }
            return result;
        }

        // Fallback: a plain single-OutputSlot node (non-selector nodes).
        var single = SingleSlot(node, "OutputSlot");
        return single is { } s ? [(s, string.Empty)] : [];
    }

    /// <summary>Display title of a node (generated Title/Name property).</summary>
    public static string TitleOf(IWorkflowNodeViewModel node)
        => node.GetType().GetProperty("Title")?.GetValue(node)?.ToString()
            ?? node.GetType().GetProperty("Name")?.GetValue(node)?.ToString()
            ?? string.Empty;

    /// <summary>Whether a slot is an input or output of its node and its index among that list.</summary>
    public static (bool IsInput, int Index)? IndexOf(IWorkflowNodeViewModel node, IWorkflowSlotViewModel slot)
    {
        var inputs = Inputs(node);
        for (int i = 0; i < inputs.Count; i++)
            if (ReferenceEquals(inputs[i].Slot, slot)) return (true, i);

        var outputs = Outputs(node);
        for (int i = 0; i < outputs.Count; i++)
            if (ReferenceEquals(outputs[i].Slot, slot)) return (false, i);

        return null;
    }

    private static IWorkflowSlotViewModel? SingleSlot(IWorkflowNodeViewModel node, string propertyName)
        => node.GetType().GetProperty(propertyName)?.GetValue(node) as IWorkflowSlotViewModel;
}
