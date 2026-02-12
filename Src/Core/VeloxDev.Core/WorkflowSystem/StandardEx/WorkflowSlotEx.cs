using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.StandardEx;

public static class WorkflowSlotEx
{
    public static IReadOnlyCollection<IVeloxCommand> GetStandardCommands
        (this IWorkflowSlotViewModel component)
        =>
        [
            component.SetSizeCommand,
            component.SendConnectionCommand,
            component.ReceiveConnectionCommand,
            component.DeleteCommand
        ];

    public static void StandardSetSize(this IWorkflowSlotViewModel component, Size size)
    {
        component.Size.Width = size.Width;
        component.Size.Height = size.Height;
        component.OnPropertyChanged(nameof(component.Size));
        component.GetHelper().UpdateLayout();
    }
    public static void StandardSetOffset(this IWorkflowSlotViewModel component, Offset offset)
    {
        component.Offset.Left = offset.Left;
        component.Offset.Top = offset.Top;
        component.OnPropertyChanged(nameof(component.Offset));
        component.GetHelper().UpdateLayout();
    }
    public static void StandardSetChannel(this IWorkflowSlotViewModel component, SlotChannel channel)
    {
        if (component.Parent?.Parent is null) return;
        var tree = component.Parent.Parent;
        List<IWorkflowLinkViewModel> links_asSource = [];
        List<IWorkflowLinkViewModel> links_asTarget = [];
        foreach (var target in component.Targets)
        {
            if (tree.LinksMap.TryGetValue(component, out var pair) &&
               pair.TryGetValue(target, out var link))
                links_asSource.Add(link);
        }
        foreach (var source in component.Sources)
        {
            if (tree.LinksMap.TryGetValue(source, out var pair) &&
               pair.TryGetValue(component, out var link))
                links_asTarget.Add(link);
        }
        switch (component.Channel.HasFlag(SlotChannel.None),
                component.Channel.HasFlag(SlotChannel.OneTarget),
                component.Channel.HasFlag(SlotChannel.OneSource),
                component.Channel.HasFlag(SlotChannel.MultipleTargets),
                component.Channel.HasFlag(SlotChannel.MultipleSources))
        {
            case (true, false, false, false, false):
                foreach (var link in links_asSource)
                {
                    link.GetHelper().Delete();
                }
                foreach (var link in links_asTarget)
                {
                    link.GetHelper().Delete();
                }
                break;
            case (_, true, false, false, false):
                foreach (var link in links_asTarget)
                {
                    link.GetHelper().Delete();
                }
                break;
            case (_, false, true, false, false):
                foreach (var link in links_asSource)
                {
                    link.GetHelper().Delete();
                }
                break;
            case (_, true, _, false, true):
                foreach (var link in links_asTarget)
                {
                    link.GetHelper().Delete();
                }
                break;
            case (_, _, true, true, false):
                foreach (var link in links_asSource)
                {
                    link.GetHelper().Delete();
                }
                break;
        }
        component.Channel = channel;
    }
    public static void StandardSetLayer(this IWorkflowSlotViewModel component, int layer)
    {
        component.Anchor.Layer = layer;
        component.OnPropertyChanged(nameof(component.Anchor));
    }

    public static void StandardUpdateLayout(this IWorkflowSlotViewModel component)
    {
        if (component.Parent is null) return;
        var baseLeft = component.VisualPoint.Unit is VisualUnit.Relative ? component.Parent.Size.Width * component.VisualPoint.Left : component.VisualPoint.Left;
        var baseTop = component.VisualPoint.Unit is VisualUnit.Relative ? component.Parent.Size.Height * component.VisualPoint.Top : component.VisualPoint.Top;
        var leftOffset = component.VisualPoint.Alignment switch
        {
            Alignments.TopLeft or Alignments.CenterLeft or Alignments.BottomLeft => 0d,
            Alignments.TopCenter or Alignments.Center or Alignments.BottomCenter => component.Size.Width * 0.5d,
            Alignments.TopRight or Alignments.CenterRight or Alignments.BottomRight => component.Size.Width,
            _ => 0d
        };
        var topOffset = component.VisualPoint.Alignment switch
        {
            Alignments.TopLeft or Alignments.TopCenter or Alignments.TopRight => 0d,
            Alignments.CenterLeft or Alignments.Center or Alignments.CenterRight => component.Size.Height * 0.5d,
            Alignments.BottomLeft or Alignments.BottomCenter or Alignments.BottomRight => component.Size.Height,
            _ => 0d
        };
        component.Offset.Left = baseLeft - leftOffset;
        component.Offset.Top = baseTop - topOffset;
        component.Anchor.Left = component.Parent.Anchor.Left + component.Offset.Left + component.Size.Width / 2;
        component.Anchor.Top = component.Parent.Anchor.Top + component.Offset.Top + component.Size.Height / 2;
        component.OnPropertyChanged(nameof(component.Anchor));
        component.OnPropertyChanged(nameof(component.Offset));
    }
    public static void StandardUpdateState(this IWorkflowSlotViewModel component)
    {
        bool hasOutgoingConnections = component.Targets.Count > 0;
        bool hasIncomingConnections = component.Sources.Count > 0;

        component.State = (hasOutgoingConnections, hasIncomingConnections) switch
        {
            (true, false) => SlotState.Sender,
            (false, true) => SlotState.Receiver,
            (true, true) => SlotState.Sender | SlotState.Receiver,
            (false, false) => SlotState.StandBy,
        };
    }

    public static void StandardApplyConnection(this IWorkflowSlotViewModel component)
    {
        var tree = component.Parent?.Parent;
        tree?.GetHelper()?.SendConnection(component);
    }
    public static void StandardReceiveConnection(this IWorkflowSlotViewModel component)
    {
        var tree = component.Parent?.Parent;
        tree?.GetHelper().ReceiveConnection(component);
    }

    public static void StandardDelete(this IWorkflowSlotViewModel component)
    {
        if (component.Parent is null) return;

        HashSet<IWorkflowLinkViewModel> links = [];

        foreach (var target in component.Targets)
        {
            var tree = target.Parent?.Parent;

            if ((tree?.LinksMap.TryGetValue(component, out var dic) ?? false) &&
                dic.TryGetValue(target, out var link))
            {
                links.Add(link);
            }
        }

        foreach (var source in component.Sources)
        {
            var tree = source.Parent?.Parent;

            if ((tree?.LinksMap.TryGetValue(source, out var dic) ?? false) &&
                dic.TryGetValue(component, out var link))
            {
                links.Add(link);
            }
        }

        foreach (var link in links)
        {
            link.GetHelper().Delete();
        }

        if (component.Parent.Parent is null)
        {
            component.Parent.Slots.Remove(component);
        }
        else
        {
            var oldParent = component.Parent;
            component.Parent.Parent.GetHelper().Submit(new WorkflowActionPair(
                () =>
                {
                    component.Parent.Slots.Remove(component);
                    component.Parent = null;
                },
                () =>
                {
                    oldParent.Slots.Add(component);
                    component.Parent = oldParent;
                }));
        }
    }

    public static bool StandardCanBeSender(this IWorkflowSlotViewModel component)
        => component.Channel.HasFlag(SlotChannel.OneTarget) ||
           component.Channel.HasFlag(SlotChannel.MultipleTargets) ||
           component.Channel.HasFlag(SlotChannel.OneBoth) ||
           component.Channel.HasFlag(SlotChannel.MultipleBoth);

    public static bool StandardCanBeReceiver(this IWorkflowSlotViewModel component)
        => component.Channel.HasFlag(SlotChannel.OneSource) ||
           component.Channel.HasFlag(SlotChannel.MultipleSources) ||
           component.Channel.HasFlag(SlotChannel.OneBoth) ||
           component.Channel.HasFlag(SlotChannel.MultipleBoth);
}
