using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// A Razor/Blazor workflow tree surface composing the surface behavior, grid decorator,
/// minimap, links layer, and a pooled node view layer. Set <see cref="Tree"/> to an
/// <see cref="IWorkflowTreeViewModel"/> to render. Node cards are rendered by the
/// generated <c>NodeView</c> (or <see cref="NodeTemplate"/>), with input/output slot
/// hosts populated generically from <c>Node.Slots</c> (channel-based input/output split).
///
/// Blazor has no data-binding auto-refresh, so this component subscribes to the tree model
/// (nodes/links collections, tree, virtual link, and node anchor/position changes) and
/// re-renders when connections are added, removed, or dragged. Without it, a newly created
/// link would never appear: the <c>@foreach (var link in Tree.Links)</c> layer is re-run
/// only when this component calls <c>StateHasChanged</c>.
/// </summary>
public partial class TreeView : ComponentBase, IDisposable
{
    /// <summary>Gets or sets the workflow tree rendered by this surface.</summary>
    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    /// <summary>Gets or sets the scroll container element id.</summary>
    [Parameter]
    public string ScrollViewerId { get; set; } = "veloxdev-wf-scroll";

    /// <summary>Gets or sets the canvas element id.</summary>
    [Parameter]
    public string CanvasId { get; set; } = "veloxdev-wf-canvas";

    /// <summary>Gets or sets an optional per-node template (overrides the generated <c>NodeView</c>).</summary>
    [Parameter]
    public RenderFragment<IWorkflowNodeViewModel>? NodeTemplate { get; set; }

    /// <summary>Gets or sets the minor grid spacing in pixels.</summary>
    [Parameter]
    public double GridSpacing { get; set; } = 40;

    private readonly List<IWorkflowNodeViewModel> _subscribedNodes = [];
    private INotifyPropertyChanged? _subscribedTree;
    private INotifyPropertyChanged? _subscribedVirtualLink;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();
        SubscribeTree(Tree);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        // Rebind if the Tree parameter changed to a different instance.
        if (!ReferenceEquals(_subscribedTree, Tree))
        {
            UnsubscribeTree();
            SubscribeTree(Tree);
        }
    }

    private void SubscribeTree(IWorkflowTreeViewModel? tree)
    {
        if (tree is null) return;

        if (tree is INotifyPropertyChanged np)
        {
            _subscribedTree = np;
            np.PropertyChanged += OnTreePropertyChanged;
        }

        tree.Nodes.CollectionChanged += OnNodesOrLinksChanged;
        tree.Links.CollectionChanged += OnNodesOrLinksChanged;

        // The VirtualLink raises its own PropertyChanged (Send/Receive/Reset only mutate the
        // VirtualLink object, not the tree), so subscribe directly to redraw the gesture.
        if (tree.VirtualLink is INotifyPropertyChanged vp)
        {
            _subscribedVirtualLink = vp;
            vp.PropertyChanged += OnVirtualLinkPropertyChanged;
        }

        SubscribeNodeChanges(tree);
    }

    private void UnsubscribeTree()
    {
        if (_subscribedTree is not null)
        {
            _subscribedTree.PropertyChanged -= OnTreePropertyChanged;
            _subscribedTree = null;
        }

        if (Tree is not null)
        {
            Tree.Nodes.CollectionChanged -= OnNodesOrLinksChanged;
            Tree.Links.CollectionChanged -= OnNodesOrLinksChanged;
        }

        if (_subscribedVirtualLink is not null)
        {
            _subscribedVirtualLink.PropertyChanged -= OnVirtualLinkPropertyChanged;
            _subscribedVirtualLink = null;
        }

        UnsubscribeNodeChanges();
    }

    private void SubscribeNodeChanges(IWorkflowTreeViewModel tree)
    {
        foreach (var node in tree.Nodes)
        {
            if (node is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += OnNodePropertyChanged;
                _subscribedNodes.Add(node);
            }
        }
    }

    private void UnsubscribeNodeChanges()
    {
        foreach (var node in _subscribedNodes)
        {
            if (node is INotifyPropertyChanged npc)
                npc.PropertyChanged -= OnNodePropertyChanged;
        }
        _subscribedNodes.Clear();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Live node position + link updates while dragging (Anchor/Size changes).
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
            InvokeAsync(StateHasChanged);
    }

    private void OnNodesOrLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Node add/remove changes the per-node subscription set; links change the links layer.
        UnsubscribeNodeChanges();
        if (Tree is not null) SubscribeNodeChanges(Tree);
        InvokeAsync(StateHasChanged);
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnVirtualLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    /// <inheritdoc />
    public void Dispose()
    {
        UnsubscribeTree();
    }

    private string Background { get; } = ToCss("#1E1E1E");
    private string MinorGridColor { get; } = ToCss("#2A2D2E");
    private string RulerBackground { get; } = ToCss("#C8252526");
    private string RulerTickColor { get; } = ToCss("#555555");
    private string RulerDividerColor { get; } = ToCss("#3A3D40");
    private string NodeForegroundCss { get; } = ToCss("#DD1E1E1E");

    /// <summary>
    /// Input slots are the pure link-sources rendered on the node's left edge. The
    /// enumerated selector slots default to <see cref="SlotChannel.MultipleBoth"/>, which
    /// also carries a source flag, so the target flags are what separate the right-edge
    /// output slots from the single left-edge input slot.
    /// </summary>
    private static IEnumerable<IWorkflowSlotViewModel> InputSlotsOf(IWorkflowNodeViewModel node)
        => node.Slots.Where(s => (s.Channel.HasFlag(SlotChannel.OneSource)
                                  || s.Channel.HasFlag(SlotChannel.MultipleSources))
                                 && !s.Channel.HasFlag(SlotChannel.OneTarget)
                                 && !s.Channel.HasFlag(SlotChannel.MultipleTargets));

    /// <summary>Output slots are the link-targets rendered on the node's right edge.</summary>
    private static IEnumerable<IWorkflowSlotViewModel> OutputSlotsOf(IWorkflowNodeViewModel node)
        => node.Slots.Where(s => s.Channel.HasFlag(SlotChannel.OneTarget)
                                  || s.Channel.HasFlag(SlotChannel.MultipleTargets));

    /// <summary>
    /// Builds a slot → name lookup for the node's enumerated selector slots. The names live
    /// on the <see cref="ConditionalSlot{TSlot}"/> wrappers inside each
    /// <see cref="SlotEnumerator{TSlot}"/> property, not on the slot view models themselves,
    /// so they are surfaced via reflection over any property implementing
    /// <see cref="IConditionalSlotProvider{TSlot}"/>.
    /// </summary>
    private static Dictionary<IWorkflowSlotViewModel, string> SlotNamesOf(IWorkflowNodeViewModel node)
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

    /// <summary>
    /// Converts XAML-style <c>#AARRGGBB</c> color literals (as used by the template symbols)
    /// into CSS color values, so symbol-driven colors work in Razor views. Also passes
    /// through named colors and CSS <c>rgb()/rgba()</c> strings unchanged.
    /// </summary>
    private static string ToCss(string value)
    {
        var text = value.Trim();
        if (text.Length == 9 && text[0] == '#')
        {
            var alpha = text.Substring(1, 2);
            var rgb = text.Substring(3);
            if (byte.TryParse(alpha, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var a))
            {
                return $"rgba({HexByte(rgb, 0)},{HexByte(rgb, 2)},{HexByte(rgb, 4)},{a / 255d:0.###})";
            }
        }

        if (text.Length == 7 && text[0] == '#')
        {
            return text;
        }

        return text;
    }

    private static int HexByte(string hex, int offset)
        => Convert.ToInt32(hex.Substring(offset, 2), 16);
}
