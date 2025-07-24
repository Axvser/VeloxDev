﻿using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed partial class SlotContext : IWorkflowSlot
    {
        [VeloxProperty]
        private ObservableCollection<IWorkflowNode> targets = [];
        [VeloxProperty]
        private ObservableCollection<IWorkflowNode> sources = [];
        [VeloxProperty]
        private IWorkflowNode? parent = null;
        [VeloxProperty]
        private SlotCapacity capacity = SlotCapacity.Universal;
        [VeloxProperty]
        private SlotState state = SlotState.StandBy;
        [VeloxProperty]
        private Anchor anchor = new();
        [VeloxProperty]
        private Anchor offset = new();
        [VeloxProperty]
        private Size size = new();
        [VeloxProperty]
        private bool isEnabled = true;
        [VeloxProperty]
        private string uID = string.Empty;
        [VeloxProperty]
        private string name = string.Empty;

        [VeloxCommand]
        private static Task Delete(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowSlot slot &&
                slot.Parent?.Parent is IWorkflowTree tree)
            {
                List<IWorkflowNode> removed_targets = [];
                List<IWorkflowNode> removed_sources = [];
                List<IWorkflowLink> removed_links = [];
                foreach (var target in slot.Targets)
                {
                    var link = tree.FindLink(slot.Parent, target);
                    if (link != null)
                    {
                        tree.Links.Remove(link);
                        removed_links.Add(link);
                        removed_targets.Add(target);
                    }
                }
                foreach (var target in removed_targets)
                {
                    slot.Targets.Remove(target);
                }
                foreach (var source in slot.Sources)
                {
                    var link = tree.FindLink(source, slot.Parent);
                    if (link != null)
                    {
                        tree.Links.Remove(link);
                        removed_links.Add(link);
                        removed_sources.Add(source);
                    }
                }
                foreach (var source in removed_sources)
                {
                    slot.Sources.Remove(source);
                }
                tree.PushUndo(() =>
                {
                    foreach (var rm in removed_sources)
                    {
                        slot.Sources.Add(rm);
                    }
                    foreach (var rm in removed_targets)
                    {
                        slot.Targets.Add(rm);
                    }
                    foreach (var rm in removed_links)
                    {
                        tree.Links.Add(rm);
                    }
                });
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private static Task Connecting(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowSlot slot &&
                slot.Parent?.Parent is IWorkflowTree tree)
            {
                tree.SetSenderCommand.Execute(parameter);
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private static Task Connected(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowSlot slot &&
                slot.Parent?.Parent is IWorkflowTree tree)
            {
                tree.SetProcessorCommand.Execute(parameter);
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private static Task Undo(object? parameter, CancellationToken ct)
        {
            if(parameter is IWorkflowSlot slot)
            {
                slot.Parent?.Parent?.UndoCommand?.Execute(null);
            }
            return Task.CompletedTask;
        }
    }
}
