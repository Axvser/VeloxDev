using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Presentation helpers shared by the Blazor workflow tree template: the default tree palette,
/// XAML&#8594;CSS color conversion, and the generic input/output slot split used by the default
/// node markup. They live here rather than in the template's code-behind so that code-behind holds
/// only the template's own knobs.
/// </summary>
public static class WorkflowPresentation
{
    /// <summary>Gets the minor grid line color.</summary>
    public static string MinorGridColor { get; } = ToCss("#2A2D2E");

    /// <summary>Gets the ruler band background color.</summary>
    public static string RulerBackground { get; } = ToCss("#C8252526");

    /// <summary>Gets the ruler tick color.</summary>
    public static string RulerTickColor { get; } = ToCss("#555555");

    /// <summary>Gets the ruler divider color.</summary>
    public static string RulerDividerColor { get; } = ToCss("#3A3D40");

    /// <summary>Gets the node output-label foreground color.</summary>
    public static string NodeForegroundCss { get; } = ToCss("#DD1E1E1E");

    /// <summary>
    /// Converts XAML-style <c>#AARRGGBB</c> color literals (as used by the template symbols)
    /// into CSS color values, so symbol-driven colors work in Razor views. Also passes
    /// through named colors and CSS <c>rgb()/rgba()</c> strings unchanged.
    /// </summary>
    public static string ToCss(string value)
    {
        var text = value.Trim();
        if (text.Length == 9 && text[0] == '#')
        {
            var alpha = text.Substring(1, 2);
            var rgb = text.Substring(3);
            if (byte.TryParse(alpha, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a))
            {
                return FormattableString.Invariant(
                    $"rgba({HexByte(rgb, 0)},{HexByte(rgb, 2)},{HexByte(rgb, 4)},{a / 255d:0.###})");
            }
        }

        if (text.Length == 7 && text[0] == '#')
        {
            return text;
        }

        return text;
    }

    /// <summary>
    /// Input slots are the pure link-sources rendered on the node's left edge. The
    /// enumerated selector slots default to <see cref="SlotChannel.MultipleBoth"/>, which
    /// also carries a source flag, so the target flags are what separate the right-edge
    /// output slots from the single left-edge input slot.
    /// </summary>
    public static IEnumerable<IWorkflowSlotViewModel> InputSlotsOf(IWorkflowNodeViewModel node)
        => node.Slots.Where(s => (s.Channel.HasFlag(SlotChannel.OneSource)
                                  || s.Channel.HasFlag(SlotChannel.MultipleSources))
                                 && !s.Channel.HasFlag(SlotChannel.OneTarget)
                                 && !s.Channel.HasFlag(SlotChannel.MultipleTargets));

    /// <summary>Output slots are the link-targets rendered on the node's right edge.</summary>
    public static IEnumerable<IWorkflowSlotViewModel> OutputSlotsOf(IWorkflowNodeViewModel node)
        => node.Slots.Where(s => s.Channel.HasFlag(SlotChannel.OneTarget)
                                  || s.Channel.HasFlag(SlotChannel.MultipleTargets));

    /// <summary>
    /// Builds a slot → name lookup for the node's enumerated selector slots. The names live
    /// on the <see cref="ConditionalSlot{TSlot}"/> wrappers inside each
    /// <see cref="SlotEnumerator{TSlot}"/> property, not on the slot view models themselves,
    /// so they are surfaced via reflection over any property implementing
    /// <see cref="IConditionalSlotProvider{TSlot}"/>.
    /// </summary>
    public static Dictionary<IWorkflowSlotViewModel, string> SlotNamesOf(IWorkflowNodeViewModel node)
    {
        var map = new Dictionary<IWorkflowSlotViewModel, string>();
        foreach (var property in node.GetType().GetProperties())
        {
            var value = property.GetValue(node);
            if (value is null) continue;

            var isProvider = value.GetType().GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConditionalSlotProvider<>));
            if (!isProvider) continue;

            if (value.GetType().GetProperty("Items")
                    ?.GetValue(value) is not IEnumerable items)
                continue;

            foreach (var item in items)
            {
                var itemType = item.GetType();
                if (itemType.GetProperty("Slot")?.GetValue(item) is not IWorkflowSlotViewModel slot)
                    continue;
                var name = itemType.GetProperty("Name")?.GetValue(item) as string;
                map[slot] = string.IsNullOrEmpty(name) ? slot.ToString() ?? string.Empty : name;
            }
        }
        return map;
    }

    // 取十六进制两位颜色分量
    private static int HexByte(string hex, int offset)
        => Convert.ToInt32(hex.Substring(offset, 2), 16);
}
