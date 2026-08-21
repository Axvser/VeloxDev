using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.StandardEx;

#pragma warning disable

public static class WorkflowTreeEx
{
    private static readonly ConditionalWeakTable<IWorkflowTreeViewModel, TreeCache> _cache = new();

    public static IReadOnlyCollection<IVeloxCommand> GetStandardCommands(this IWorkflowTreeViewModel component)
        =>
        [
            component.CreateNodeCommand,
            component.SetPointerCommand,
            component.ResetVirtualLinkCommand,
            component.SendConnectionCommand,
            component.ReceiveConnectionCommand,
            component.SubmitCommand,
            component.RedoCommand,
            component.UndoCommand
        ];

    public static void StandardCreateNode(this IWorkflowTreeViewModel component, IWorkflowNodeViewModel node)
    {
        var oldParent = node.Parent;
        var newParent = component;
        // Detach the node from its previous tree first. A detached node (first CreateNode)
        // has nothing to detach — skip, otherwise StandardDelete's attachment guard fires.
        if (node.Parent is not null)
        {
            node.GetHelper().Delete();
        }
        component.StandardSubmit(new WorkflowActionPair(
            () => CreateNodeRedo(component, node, newParent),
            () => CreateNodeUndo(component, node, oldParent)));
    }

    public static void StandardSetPointer(this IWorkflowTreeViewModel component, Anchor anchor)
    {
        component.VirtualLink.Receiver.Anchor = anchor;
        component.VirtualLink.OnPropertyChanged(nameof(component.VirtualLink.Receiver));
        component.OnPropertyChanged(nameof(component.VirtualLink));
    }

    public static async Task StandardCloseAsync(this IWorkflowTreeViewModel component)
    {
        component.GetHelper().Closing();

        foreach (var node in component.Nodes)
        {
            node.GetHelper().Closing();
            foreach (var slot in node.Slots)
            {
                slot.GetHelper().Closing();
            }
        }
        foreach (var link in component.Links)
        {
            link.GetHelper().Closing();
        }

        foreach (var node in component.Nodes)
        {
            await node.GetHelper().CloseAsync().ConfigureAwait(false);
            foreach (var slot in node.Slots)
            {
                await slot.GetHelper().CloseAsync().ConfigureAwait(false);
            }
        }
        foreach (var link in component.Links)
        {
            await link.GetHelper().CloseAsync().ConfigureAwait(false);
        }

        foreach (var node in component.Nodes)
        {
            node.GetHelper().Closed();
            foreach (var slot in node.Slots)
            {
                slot.GetHelper().Closed();
            }
        }
        foreach (var link in component.Links)
        {
            link.GetHelper().Closed();
        }

        component.GetHelper().Closed();
    }

    #region Connection Manager Extensions

    public static void StandardSendConnection(this IWorkflowTreeViewModel component, IWorkflowSlotViewModel slot)
    {
        var cache = GetCache(component);

        if (slot.Parent?.Parent is null)
        {
            WorkflowGuard.Fail("The slot is not attached to a tree; it cannot send a connection.");
            return;
        }

        // 1. Check the sender's capability
        bool canBeSender = slot.StandardCanBeSender();
        if (!canBeSender)
        {
            component.StandardResetVirtualLink();
            cache.CurrentSender = null;
            return;
        }

        // 2. Clean up connections smartly based on the sender's channel type
        component.StandardSmartCleanupSenderConnections(slot);

        // 3. Set up the virtual link
        component.VirtualLink.Sender.Anchor = slot.Anchor;
        component.VirtualLink.Receiver.Anchor = slot.Anchor;
        component.VirtualLink.IsVisible = true;

        // 4. Update state
        cache.CurrentSender = slot;
        slot.State = SlotState.PreviewSender;
        slot.GetHelper().UpdateState();
    }

    public static void StandardReceiveConnection(this IWorkflowTreeViewModel component, IWorkflowSlotViewModel slot)
    {
        var cache = GetCache(component);
        if (cache.CurrentSender == null) return;

        // Check the receiver's capability
        bool canBeReceiver = slot.StandardCanBeReceiver();
        if (!canBeReceiver)
        {
            component.StandardResetVirtualLink();
            return;
        }

        // Check the user-custom validation logic
        if (!component.GetHelper().ValidateConnection(cache.CurrentSender, slot))
        {
            component.StandardResetVirtualLink();
            cache.CurrentSender = null;
            return;
        }

        // Check for connections within the same node
        if (cache.CurrentSender.Parent == slot.Parent)
        {
            component.StandardResetVirtualLink();
            cache.CurrentSender = null;
            return;
        }

        // Enforce the hard rule: check and clean up same-direction connection conflicts
        component.StandardCleanupSameDirectionConnections(cache.CurrentSender, slot);

        // Clean up connections smartly based on the receiver's channel type
        component.StandardSmartCleanupReceiverConnections(slot);

        // Create the new connection
        component.StandardCreateNewConnection(cache.CurrentSender, slot);

        // Reset state
        component.StandardResetVirtualLink();
        cache.CurrentSender = null;
    }

    public static void StandardResetVirtualLink(this IWorkflowTreeViewModel component)
    {
        var cache = GetCache(component);

        // Reset to "no value" (NaN) rather than the origin (0,0,0): the virtual-link endpoints have no valid coordinates before a drag.
        component.VirtualLink.Sender.Anchor = new Anchor(double.NaN, double.NaN, 0);
        component.VirtualLink.Receiver.Anchor = new Anchor(double.NaN, double.NaN, 0);
        component.VirtualLink.IsVisible = false;

        if (cache.CurrentSender != null)
        {
            cache.CurrentSender.State &= ~SlotState.PreviewSender;
            cache.CurrentSender.GetHelper().UpdateState();
        }

        cache.CurrentSender = null;
    }
    #endregion

    #region Redo & Undo Extensions
    public static void StandardRedo(this IWorkflowTreeViewModel component)
    {
        var cache = GetCache(component);
        if (cache.RedoStack.TryPop(out var pair))
        {
            try
            {
                pair.Redo.Invoke();
                cache.UndoStack.Push(pair);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }

    public static void StandardSubmit(this IWorkflowTreeViewModel component, IWorkflowActionPair actionPair)
    {
        var cache = GetCache(component);
        try
        {
            actionPair.Redo.Invoke();
            cache.UndoStack.Push(actionPair);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    public static void StandardUndo(this IWorkflowTreeViewModel component)
    {
        var cache = GetCache(component);
        if (cache.UndoStack.TryPop(out var pair))
        {
            try
            {
                pair.Undo.Invoke();
                cache.RedoStack.Push(pair);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }

    public static void StandardClearHistory(this IWorkflowTreeViewModel component)
    {
        var cache = GetCache(component);
        cache.RedoStack.Clear();
        cache.UndoStack.Clear();
    }
    #endregion

    #region Private Helper Methods
    private static void CreateNodeRedo(IWorkflowTreeViewModel component, IWorkflowNodeViewModel node, IWorkflowTreeViewModel newParent)
    {
        node.Parent = newParent;
        component.Nodes.Add(node);
    }

    private static void CreateNodeUndo(IWorkflowTreeViewModel component, IWorkflowNodeViewModel node, IWorkflowTreeViewModel? oldParent)
    {
        component.Nodes.Remove(node);
        node.Parent = oldParent;
    }

    private static void StandardSmartCleanupSenderConnections(this IWorkflowTreeViewModel component, IWorkflowSlotViewModel sender)
    {
        if (component == null) return;

        // Collect all connections that need to be removed
        var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

        // 1. Remove connections where the sender is the source (connections sent to targets)
        if (component.LinksMap.TryGetValue(sender, out var targetLinks))
        {
            foreach (var kvp in targetLinks)
            {
                connectionsToRemove.Add((sender, kvp.Key, kvp.Value));
            }
        }

        // 2. For OneBoth channels, also remove connections where the sender is the target (connections received from other slots)
        if (sender.Channel.HasFlag(SlotChannel.OneBoth))
        {
            // Find all connections targeting this slot
            foreach (var sourceDict in component.LinksMap)
            {
                if (sourceDict.Value.TryGetValue(sender, out var link))
                {
                    connectionsToRemove.Add((sourceDict.Key, sender, link));
                }
            }
        }

        // Decide whether to clean up based on the sender's channel type
        bool shouldCleanup = ShouldCleanupConnections(sender.Channel, isSender: true, existingConnections: connectionsToRemove.Count);

        if (shouldCleanup && connectionsToRemove.Count > 0)
        {
            component.StandardRemoveConnections(connectionsToRemove);
        }
    }

    private static void StandardSmartCleanupReceiverConnections(this IWorkflowTreeViewModel component, IWorkflowSlotViewModel receiver)
    {
        if (component == null) return;

        // Collect all connections that need to be removed
        var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

        // 1. Remove connections where the receiver is the target (connections received from other slots)
        foreach (var senderDict in component.LinksMap)
        {
            if (senderDict.Value.TryGetValue(receiver, out var link))
            {
                connectionsToRemove.Add((senderDict.Key, receiver, link));
            }
        }

        // 2. For OneBoth channels, also remove connections where the receiver is the source (connections sent to other slots)
        if (receiver.Channel.HasFlag(SlotChannel.OneBoth))
        {
            // Find all connections originating from this slot
            if (component.LinksMap.TryGetValue(receiver, out var targetLinks))
            {
                foreach (var kvp in targetLinks)
                {
                    connectionsToRemove.Add((receiver, kvp.Key, kvp.Value));
                }
            }
        }

        // Decide whether to clean up based on the receiver's channel type
        bool shouldCleanup = ShouldCleanupConnections(receiver.Channel, isSender: false, existingConnections: connectionsToRemove.Count);

        if (shouldCleanup && connectionsToRemove.Count > 0)
        {
            component.StandardRemoveConnections(connectionsToRemove);
        }
    }

    private static void StandardCleanupSameDirectionConnections(this IWorkflowTreeViewModel component,
        IWorkflowSlotViewModel newSender, IWorkflowSlotViewModel newReceiver)
    {
        if (component == null || newSender.Parent == null || newReceiver.Parent == null) return;

        var senderNode = newSender.Parent;
        var receiverNode = newReceiver.Parent;

        // Collect same-direction connections that need to be removed
        var sameDirectionConnections = new List<(IWorkflowSlotViewModel Sender, IWorkflowSlotViewModel Receiver, IWorkflowLinkViewModel Link)>();

        // Find all connections from senderNode to receiverNode
        foreach (var potentialSender in senderNode.Slots)
        {
            if (component.LinksMap.TryGetValue(potentialSender, out var targetLinks))
            {
                foreach (var potentialReceiver in receiverNode.Slots)
                {
                    if (targetLinks.TryGetValue(potentialReceiver, out var existingLink))
                    {
                        // Exclude the new connection currently being created
                        if (!(potentialSender == newSender && potentialReceiver == newReceiver))
                        {
                            sameDirectionConnections.Add((potentialSender, potentialReceiver, existingLink));
                        }
                    }
                }
            }
        }

        // Perform the cleanup (keeping the newest connection)
        if (sameDirectionConnections.Count > 0)
        {
            component.StandardRemoveConnections(sameDirectionConnections);
        }
    }

    private static void StandardCreateNewConnection(this IWorkflowTreeViewModel component,
        IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        if (component is null || sender is null || receiver is null) return;

        // Check whether a connection already exists (should not exist after cleanup)
        bool connectionExists = component.LinksMap.TryGetValue(sender, out var existingLinks) &&
                               existingLinks.ContainsKey(receiver);

        if (connectionExists) return;

        // Create the new connection
        var newLink = component.GetHelper().CreateLink(sender, receiver);
        newLink.IsVisible = true;

        component.StandardSubmit(new WorkflowActionPair(
            () =>
            {
                if (!component.LinksMap.ContainsKey(sender))
                    component.LinksMap[sender] = [];

                component.LinksMap[sender][receiver] = newLink;
                component.Links.Add(newLink);

                if (!sender.Targets.Contains(receiver))
                    sender.Targets.Add(receiver);
                if (!receiver.Sources.Contains(sender))
                    receiver.Sources.Add(sender);

                sender.GetHelper().UpdateState();
                receiver.GetHelper().UpdateState();

                newLink.IsVisible = true;
            },
            () =>
            {
                component.Links.Remove(newLink);
                if (component.LinksMap.ContainsKey(sender))
                {
                    component.LinksMap[sender].Remove(receiver);
                    if (component.LinksMap[sender].Count == 0)
                        component.LinksMap.Remove(sender);
                }

                sender.Targets.Remove(receiver);
                receiver.Sources.Remove(sender);

                sender.GetHelper().UpdateState();
                receiver.GetHelper().UpdateState();

                newLink.IsVisible = false;
            }
        ));
    }

    private static void StandardRemoveConnections(this IWorkflowTreeViewModel component,
        List<(IWorkflowSlotViewModel Sender, IWorkflowSlotViewModel Receiver, IWorkflowLinkViewModel Link)> connectionsToRemove)
    {
        if (component is null || connectionsToRemove.Count == 0)
            return;

        var redoActions = new List<Action>();
        var undoActions = new List<Action>();

        // Collect all affected slots for unified state updates
        var affectedSlots = new HashSet<IWorkflowSlotViewModel>();

        foreach (var (sender, receiver, link) in connectionsToRemove)
        {
            affectedSlots.Add(sender);
            affectedSlots.Add(receiver);

            redoActions.Add(() =>
            {
                // Remove the link from the Links collection
                component.Links.Remove(link);

                // Remove the link mapping from LinksMap
                if (component.LinksMap.TryGetValue(sender, out var receiverLinks))
                {
                    receiverLinks.Remove(receiver);
                    if (receiverLinks.Count == 0)
                    {
                        component.LinksMap.Remove(sender);
                    }
                }

                // Update the sender's target collection
                sender.Targets.Remove(receiver);

                // Update the receiver's source collection
                receiver.Sources.Remove(sender);

                // Hide the link
                link.IsVisible = false;
            });

            undoActions.Add(() =>
            {
                // Ensure LinksMap has a dictionary for the sender
                if (!component.LinksMap.ContainsKey(sender))
                {
                    component.LinksMap[sender] = [];
                }

                // Restore the link mapping
                component.LinksMap[sender][receiver] = link;

                // Restore the link in the Links collection
                component.Links.Add(link);

                // Restore the sender's target collection
                if (!sender.Targets.Contains(receiver))
                {
                    sender.Targets.Add(receiver);
                }

                // Restore the receiver's source collection
                if (!receiver.Sources.Contains(sender))
                {
                    receiver.Sources.Add(sender);
                }

                // Show the link
                link.IsVisible = true;
            });
        }

        // Add state-update actions to the redo/undo
        redoActions.Add(() =>
        {
            foreach (var slot in affectedSlots)
            {
                slot?.GetHelper().UpdateState();
            }
        });

        undoActions.Add(() =>
        {
            foreach (var slot in affectedSlots)
            {
                slot?.GetHelper().UpdateState();
            }
        });

        // Build the complete action pair
        var actionPair = new WorkflowActionPair(
            () => { foreach (var action in redoActions) action(); },
            () => { foreach (var action in undoActions) action(); }
        );

        // Submit to the undo/redo stack
        component.StandardSubmit(actionPair);
    }

    private static bool ShouldCleanupConnections(SlotChannel channel, bool isSender, int existingConnections)
    {
        if (channel == SlotChannel.None)
            return false;

        // Sender logic
        if (isSender)
        {
            if (channel.HasFlag(SlotChannel.OneTarget) && existingConnections > 0)
                return true;

            if (channel.HasFlag(SlotChannel.OneBoth) && existingConnections > 0)
                return true;

            return false;
        }
        // Receiver logic
        else
        {
            if (channel.HasFlag(SlotChannel.OneSource) && existingConnections > 0)
                return true;

            if (channel.HasFlag(SlotChannel.OneBoth) && existingConnections > 0)
                return true;

            return false;
        }
    }
    #endregion

    #region Degree & Topology Queries

    /// <summary>
    /// Returns the in-degree (number of incoming connections) for the node at the given index.
    /// </summary>
    public static int GetNodeInDegree(this IWorkflowTreeViewModel tree, int nodeIndex)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));
        if (nodeIndex < 0 || nodeIndex >= tree.Nodes.Count)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));

        var node = tree.Nodes[nodeIndex];
        int degree = 0;
        foreach (var slot in node.Slots)
            degree += slot.Sources.Count;
        return degree;
    }

    /// <summary>
    /// Returns the out-degree (number of outgoing connections) for the node at the given index.
    /// </summary>
    public static int GetNodeOutDegree(this IWorkflowTreeViewModel tree, int nodeIndex)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));
        if (nodeIndex < 0 || nodeIndex >= tree.Nodes.Count)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));

        var node = tree.Nodes[nodeIndex];
        int degree = 0;
        foreach (var slot in node.Slots)
            degree += slot.Targets.Count;
        return degree;
    }

    /// <summary>
    /// Finds all entry nodes (in-degree = 0) in the tree. These are natural starting
    /// points for forward traversal.
    /// </summary>
    public static IReadOnlyList<int> FindEntryNodeIndices(this IWorkflowTreeViewModel tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var entries = new List<int>();
        for (int i = 0; i < tree.Nodes.Count; i++)
            if (GetNodeInDegree(tree, i) == 0)
                entries.Add(i);
        return entries;
    }

    /// <summary>
    /// Finds all exit nodes (out-degree = 0) in the tree. These are natural ending
    /// points for forward traversal, or starting points for reverse traversal.
    /// </summary>
    public static IReadOnlyList<int> FindExitNodeIndices(this IWorkflowTreeViewModel tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var exits = new List<int>();
        for (int i = 0; i < tree.Nodes.Count; i++)
            if (GetNodeOutDegree(tree, i) == 0)
                exits.Add(i);
        return exits;
    }

    /// <summary>
    /// Finds all nodes whose in-degree equals the specified value.
    /// </summary>
    public static IReadOnlyList<int> FindNodesByInDegree(this IWorkflowTreeViewModel tree, int degree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var result = new List<int>();
        for (int i = 0; i < tree.Nodes.Count; i++)
            if (GetNodeInDegree(tree, i) == degree)
                result.Add(i);
        return result;
    }

    /// <summary>
    /// Finds all nodes whose out-degree equals the specified value.
    /// </summary>
    public static IReadOnlyList<int> FindNodesByOutDegree(this IWorkflowTreeViewModel tree, int degree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var result = new List<int>();
        for (int i = 0; i < tree.Nodes.Count; i++)
            if (GetNodeOutDegree(tree, i) == degree)
                result.Add(i);
        return result;
    }

    #endregion

    #region Cache Management
    private class TreeCache
    {
        public IWorkflowSlotViewModel? CurrentSender { get; set; }
        public ConcurrentStack<IWorkflowActionPair> RedoStack { get; } = new();
        public ConcurrentStack<IWorkflowActionPair> UndoStack { get; } = new();
    }

    private static TreeCache GetCache(IWorkflowTreeViewModel component)
    {
        return _cache.GetValue(component, _ => new TreeCache());
    }
    #endregion
}
