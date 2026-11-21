using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
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

/// <summary>One row of the demo element lists, and the inspector's subject when it is selected.</summary>
public sealed class WorkflowElementEntry : INotifyPropertyChanged
{
    /// <summary>Creates a row for an element.</summary>
    public WorkflowElementEntry(IWorkflowComponent element, string label, WorkflowElementKind kind)
    {
        Element = element;
        Label = label;
        Kind = kind;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The node, slot or link the row acts on.</summary>
    public IWorkflowComponent Element { get; }

    /// <summary>The text shown for the row.</summary>
    public string Label { get; }

    /// <summary>Which list the row belongs to.</summary>
    public WorkflowElementKind Kind { get; }

    /// <summary>The kind in the inspector's language.</summary>
    public string KindText => Kind switch
    {
        WorkflowElementKind.Node => "节点",
        WorkflowElementKind.Slot => "插槽",
        _ => "连线",
    };

    /// <summary>The element's own properties, one per line, for the inspector card.</summary>
    /// <remarks>
    /// Read live rather than captured at construction: a compile writes the node's order after the list was
    /// built, and a run writes its status on every node. A cached copy made the inspector say "未编译" about a
    /// node that had just been compiled.
    /// </remarks>
    public string DetailText => BuildDetail(Element, Kind);

    /// <summary>Whether this row is a node.</summary>
    public bool IsNode => Kind == WorkflowElementKind.Node;

    /// <summary>Whether this row is a slot.</summary>
    public bool IsSlot => Kind == WorkflowElementKind.Slot;

    /// <summary>Whether this row is a link.</summary>
    public bool IsLink => Kind == WorkflowElementKind.Link;

    // 列表里的行对象活到下一次重建；它们订阅的是节点的属性变化，重建后由 catalog 换掉。
    internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailText)));

    private static string BuildDetail(IWorkflowComponent element, WorkflowElementKind kind)
    {
        var text = new StringBuilder();
        switch (element)
        {
            case INodeComponent node:
                text.AppendLine($"类型    {node.GetType().Name.Replace("ViewModel", string.Empty)}");
                text.AppendLine($"位置    ({node.Anchor.Horizontal:0}, {node.Anchor.Vertical:0}) · 图层 {node.Anchor.Layer}");
                text.AppendLine($"大小    {node.Size.Width:0} × {node.Size.Height:0}");
                text.AppendLine($"编译    {CompileText(node)}");
                text.AppendLine($"插槽    {node.Slots.Count}");

                switch (node)
                {
                    case PythonScriptNodeViewModel python:
                        text.AppendLine($"运行    {python.LastStatus} · {python.LastRun}");
                        break;
                    case TimerNodeViewModel timer:
                        text.AppendLine($"定时    {timer.IntervalMilliseconds} ms · 最近 {timer.LastTick}");
                        break;
                    case EnumSelectorNodeViewModel selector:
                        text.AppendLine($"路由    {selector.CompileMode} · 最近 {selector.LastRouted}");
                        break;
                    case ControllerViewModel controller:
                        text.AppendLine($"编译图  {controller.Compiler.Graphs.Count} 张");
                        break;
                }
                break;

            case ISlotComponent slot:
                text.AppendLine($"所属    {WorkflowElementCatalog.Describe(slot.Parent)}");
                text.AppendLine($"通道    {slot.Channel}");
                text.AppendLine($"状态    {slot.State}");
                text.AppendLine($"连接    出 {slot.Targets.Count} · 入 {slot.Sources.Count}");
                text.AppendLine($"位置    ({slot.Anchor.Horizontal:0}, {slot.Anchor.Vertical:0})");
                break;

            case ILinkComponent link:
                text.AppendLine($"起点    {WorkflowElementCatalog.Describe(link.Sender?.Parent)}");
                text.AppendLine($"终点    {WorkflowElementCatalog.Describe(link.Receiver?.Parent)}");
                text.AppendLine($"可见    {(link.IsVisible ? "是" : "否")}");
                break;

            default:
                text.AppendLine($"类型    {element.GetType().Name}");
                break;
        }

        return text.ToString().TrimEnd();
    }

    private static string CompileText(INodeComponent node)
    {
        if (node is not ICompileTimeAware aware || aware.CompileContext is not { } context)
            return "未编译";

        return context.Order < 0
            ? "已剪枝（Order = -1）"
            : $"顺序 #{context.Order + 1} · 链 {context.ChainIndex} · 偏移 {context.Offset}";
    }
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
    private readonly HashSet<INodeComponent> _trackedNodes = [];
    private ITreeComponent? _tree;

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
    public WorkflowElementCatalog(ITreeComponent? tree = null) => SetTree(tree);

    /// <summary>Rebinds the catalog to <paramref name="tree"/>; the previous tree's events are detached.</summary>
    public void SetTree(ITreeComponent? tree)
    {
        if (ReferenceEquals(_tree, tree)) return;

        Detach();
        _tree = tree;
        if (_tree is not null)
        {
            _tree.Nodes.CollectionChanged += OnNodesChanged;
            _tree.Links.CollectionChanged += OnLinksChanged;
            if (_tree is TreeViewModel typed) typed.ElementDetailsInvalidated += OnElementDetailsInvalidated;
        }

        Rebuild();
    }

    /// <summary>Centers the row's element in the viewport of its surface.</summary>
    /// <returns><see langword="true"/> when a surface navigator handled it.</returns>
    public bool Locate(WorkflowElementEntry entry) => entry.Kind switch
    {
        WorkflowElementKind.Node => ((INodeComponent)entry.Element).Locate(),
        WorkflowElementKind.Slot => ((ISlotComponent)entry.Element).Locate(),
        WorkflowElementKind.Link => ((ILinkComponent)entry.Element).Locate(),
        _ => false,
    };

    /// <summary>Deletes the row's element. Nodes and slots take their links with them.</summary>
    public void Delete(WorkflowElementEntry entry)
    {
        switch (entry.Element)
        {
            case INodeComponent node:
                node.GetHelper().Delete();
                break;
            case ISlotComponent slot:
                slot.GetHelper().Delete();
                break;
            case ILinkComponent link:
                link.GetHelper().Delete();
                break;
        }
    }

    private void Detach()
    {
        if (_tree is null) return;

        _tree.Nodes.CollectionChanged -= OnNodesChanged;
        _tree.Links.CollectionChanged -= OnLinksChanged;
        if (_tree is TreeViewModel typed) typed.ElementDetailsInvalidated -= OnElementDetailsInvalidated;
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

        // 列表重建后入口对象换了一批；选中的元素若还在，换到新的那一行，若已不在（被删或被 Agent 改掉）就清空。
        // 按元素而不是按入口对象找：否则任何一次 Agent 编辑都会把检查器的选择一并清掉。
        if (_tree is TreeViewModel typed && typed.SelectedElement is { } selected)
        {
            var replacement = Nodes.Concat(Slots).Concat(Links)
                .FirstOrDefault(entry => ReferenceEquals(entry.Element, selected.Element));
            if (!ReferenceEquals(replacement, selected)) typed.SelectedElement = replacement;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Slots are not exposed as one collection on the tree; subscribe to each node's own.
    private void SyncSlotHandlers(ITreeComponent tree)
    {
        var current = new HashSet<INodeComponent>(tree.Nodes);
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

    // 只在“编译完成 / 某个节点被驱动”这些明确时点刷新检查器的选中行，不订阅节点的每一次属性变化：
    // 后者会在启动布局期把每一行都打成脏的，WPF 的 CollectionView 会因此反复重整列表。
    private void OnElementDetailsInvalidated(INodeComponent? node)
    {
        if (_ui is not null && !ReferenceEquals(_ui, SynchronizationContext.Current))
        {
            _ui.Post(_ => OnElementDetailsInvalidated(node), null);
            return;
        }

        // 只有检查器正在描述的那一行会重新读；列表行的文字不变量，没有必要让 CollectionView 跟着重排。
        if (_tree is not TreeViewModel tree || tree.SelectedElement is not { } selected) return;

        var matches = node is null
            || ReferenceEquals(selected.Element, node)
            || (selected.Element is ISlotComponent slot && ReferenceEquals(slot.Parent, node));

        if (matches) selected.Refresh();
    }

    internal static string Describe(INodeComponent? node)
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
