using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

public partial class WorkflowSlotView : ComponentBase
{
    [Parameter]
    public SlotViewModel? Slot { get; set; }

    [Parameter]
    public bool IsInput { get; set; }

    [Parameter]
    public bool IsOutput { get; set; }

    private bool IsConnected => Slot?.State.HasFlag(SlotState.Sender) == true
                             || Slot?.State.HasFlag(SlotState.Receiver) == true;

    private string GetStateClass()
    {
        if (Slot is null) return "";
        var classes = new List<string>();
        if (IsConnected)
            classes.Add("connected");
        if (Slot.Channel is SlotChannel.MultipleTargets or SlotChannel.MultipleSources or SlotChannel.MultipleBoth)
            classes.Add("multi");
        return string.Join(" ", classes);
    }

    private string GetSlotId()
        => Slot?.GetHashCode().ToString("X8") ?? "";

    private string GetFillColor()
    {
        if (Slot is null) return "#555";
        if (Slot.State.HasFlag(SlotState.Sender) || Slot.State.HasFlag(SlotState.Receiver))
            return "#22c55e";
        return IsInput ? "#3b82f6" : "#f97316";
    }

    private string GetStrokeColor()
    {
        if (Slot is null) return "#444";
        if (Slot.State.HasFlag(SlotState.Sender) || Slot.State.HasFlag(SlotState.Receiver))
            return "#16a34a";
        return IsInput ? "#2563eb" : "#ea580c";
    }

    private string GetInnerColor()
    {
        if (Slot is null) return "#333";
        if (Slot.State.HasFlag(SlotState.Sender) || Slot.State.HasFlag(SlotState.Receiver))
            return "#bbf7d0";
        return "#e2e8f0";
    }
}
