using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace Demo.ViewModels;

/// <summary>Which kind of workflow element a <see cref="WorkflowElementEntry"/> holds.</summary>
public enum WorkflowElementKind
{
    Node,
    Slot,
    Link,
}

/// <summary>One row of the demo element lists.</summary>
public sealed class WorkflowElementEntry(IWorkflowViewModel element, string label, WorkflowElementKind kind)
{
    /// <summary>The node, slot or link the row acts on.</summary>
    public IWorkflowViewModel Element { get; } = element;

    /// <summary>The text shown for the row.</summary>
    public string Label { get; } = label;

    /// <summary>Which list the row belongs to.</summary>
    public WorkflowElementKind Kind { get; } = kind;
}

/// <summary>
/// Loads the tree's nodes, slots and links into three bindable lists and exposes Locate / Delete for a row.
/// </summary>
/// <remarks>
/// Shared by every full demo so the six sidebars behave the same. Slots are flattened from their nodes; the
/// collections are rebuilt on any node/link/slot change. The catalog captures the UI synchronization context
/// it was constructed on, so a change made from a background thread is marshalled before the bound collections
/// are touched.
/// </remarks>
public sealed class WorkflowElementCatalog : INotifyPropertyChanged
{
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly HashSet<IWorkflowNodeViewModel> _trackedNodes = [];
    private IWorkflowTreeViewModel? _tree;

    /// <summary>The nodes in the tree.</summary>
    public ObservableCollection<WorkflowElementEntry> Nodes { get; } = [];

    /// <summary>Every node's slots, flattened.</summary>
    public ObservableCollection<WorkflowElementEntry> Slots { get; } = [];

    /// <summary>The links in the tree.</summary>
    public ObservableCollection<WorkflowElementEntry> Links { get; } = [];

    /// <summary>A one-line count, for a status label.</summary>
    public string Summary => $"{Nodes.Count} 节点 · {Slots.Count} 插槽 · {Links.Count} 连线";

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the three lists have been rebuilt.</summary>
    public event EventHandler? Changed;

    /// <summary>Creates a catalog, optionally already bound to <paramref name="tree"/>.</summary>
    public WorkflowElementCatalog(IWorkflowTreeViewModel? tree = null) => SetTree(tree);

    /// <summary>Rebinds the catalog to <paramref name="tree"/>; the previous tree's events are detached.</summary>
    public void SetTree(IWorkflowTreeViewModel? tree)
    {
        if (ReferenceEquals(_tree, tree)) return;

        Detach();
        _tree = tree;
        if (_tree is not null)
        {
            _tree.Nodes.CollectionChanged += OnNodesChanged;
            _tree.Links.CollectionChanged += OnLinksChanged;
        }

        Rebuild();
    }

    /// <summary>Centers the row's element in the viewport of its surface.</summary>
    /// <returns><see langword="true"/> when a surface navigator handled it.</returns>
    public bool Locate(WorkflowElementEntry entry) => entry.Kind switch
    {
        WorkflowElementKind.Node => ((IWorkflowNodeViewModel)entry.Element).Locate(),
        WorkflowElementKind.Slot => ((IWorkflowSlotViewModel)entry.Element).Locate(),
        WorkflowElementKind.Link => ((IWorkflowLinkViewModel)entry.Element).Locate(),
        _ => false,
    };

    /// <summary>Deletes the row's element. Nodes and slots take their links with them.</summary>
    public void Delete(WorkflowElementEntry entry)
    {
        switch (entry.Element)
        {
            case IWorkflowNodeViewModel node:
                node.GetHelper().Delete();
                break;
            case IWorkflowSlotViewModel slot:
                slot.GetHelper().Delete();
                break;
            case IWorkflowLinkViewModel link:
                link.GetHelper().Delete();
                break;
        }
    }

    private void Detach()
    {
        if (_tree is null) return;

        _tree.Nodes.CollectionChanged -= OnNodesChanged;
        _tree.Links.CollectionChanged -= OnLinksChanged;
        foreach (var node in _trackedNodes) node.Slots.CollectionChanged -= OnSlotsChanged;
        _trackedNodes.Clear();
        _tree = null;
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        // Agent edits may arrive off the UI thread; the bound collections must not be touched there.
        if (_ui is not null && !ReferenceEquals(_ui, SynchronizationContext.Current))
        {
            _ui.Post(_ => Rebuild(), null);
            return;
        }

        Nodes.Clear();
        Slots.Clear();
        Links.Clear();

        if (_tree is { } tree)
        {
            SyncSlotHandlers(tree);

            for (var i = 0; i < tree.Nodes.Count; i++)
            {
                var node = tree.Nodes[i];
                Nodes.Add(new WorkflowElementEntry(node, $"#{i} {Describe(node)}", WorkflowElementKind.Node));

                for (var j = 0; j < node.Slots.Count; j++)
                {
                    Slots.Add(new WorkflowElementEntry(
                        node.Slots[j], $"{Describe(node)} · slot {j}", WorkflowElementKind.Slot));
                }
            }

            for (var i = 0; i < tree.Links.Count; i++)
            {
                var link = tree.Links[i];
                Links.Add(new WorkflowElementEntry(
                    link,
                    $"{Describe(link.Sender?.Parent)} → {Describe(link.Receiver?.Parent)}",
                    WorkflowElementKind.Link));
            }
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Slots are not exposed as one collection on the tree; subscribe to each node's own.
    private void SyncSlotHandlers(IWorkflowTreeViewModel tree)
    {
        var current = new HashSet<IWorkflowNodeViewModel>(tree.Nodes);
        foreach (var node in _trackedNodes.ToArray())
        {
            if (current.Contains(node)) continue;
            node.Slots.CollectionChanged -= OnSlotsChanged;
            _trackedNodes.Remove(node);
        }

        foreach (var node in current)
        {
            if (_trackedNodes.Add(node)) node.Slots.CollectionChanged += OnSlotsChanged;
        }
    }

    private static string Describe(IWorkflowNodeViewModel? node)
    {
        if (node is null) return "?";

        // 列表是给人看的：优先显示节点自己的标题，没有标题的类型再退回类型名（去掉 ViewModel）。
        var title = node switch
        {
            TimerNodeViewModel timer => timer.Title,
            EnumSelectorNodeViewModel selector => selector.Title,
            PythonScriptNodeViewModel python => python.Title,
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(title)) return title!;
        return node.GetType().Name.Replace("ViewModel", string.Empty);
    }
}
