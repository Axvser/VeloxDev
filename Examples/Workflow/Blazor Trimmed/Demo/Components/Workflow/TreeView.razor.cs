using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Workflow;

/// <summary>
/// A Razor/Blazor workflow tree surface composing the surface behavior, grid decorator,
/// minimap, and a pooled node/link view layer. Set <see cref="Tree"/> to an
/// <see cref="IWorkflowTreeViewModel"/> to render. The pooled layer is fed the tree's
/// visible items and renders one generated <c>LinkView</c> per link; node cards are rendered
/// by the generated <c>NodeView</c> (or <see cref="NodeTemplate"/>), with input/output slot
/// hosts populated generically from <c>Node.Slots</c> (channel-based input/output split).
///
/// Blazor has no data-binding auto-refresh, so this component subscribes to the tree model
/// (nodes/links collections, tree, virtual link, and node anchor/position changes) and
/// re-renders when connections are added, removed, or dragged.
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

    /// <summary>
    /// The tree's link interaction hub, or <see langword="null"/> until a <see cref="Tree"/> is set.
    /// Subscribe to it to turn link hover, press and the Delete key into app policy.
    /// </summary>
    public LinkInteraction? Interaction => Tree is null ? null : LinkInteraction.For(Tree);

    private readonly List<IWorkflowNodeViewModel> _subscribedNodes = [];
    private INotifyPropertyChanged? _subscribedTree;
    private INotifyPropertyChanged? _subscribedVirtualLink;

    // 右键菜单由表面弹：屏幕坐标只有这里的 DOM 事件知道，画布坐标由 hub 的 ContextMenuRequested 带回。
    private WorkflowSurfaceBehavior? _surface;
    private LinkInteraction? _interaction;
    private IWorkflowLinkViewModel? _menuLink;
    private Anchor _menuPosition = new();
    private int _menuLeft;
    private int _menuTop;

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

        _interaction = LinkInteraction.For(tree);
        _interaction.ContextMenuRequested += OnContextMenuRequested;
        _interaction.ContextMenuDismissRequested += OnContextMenuDismissRequested;

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

        if (_interaction is not null)
        {
            _interaction.ContextMenuRequested -= OnContextMenuRequested;
            _interaction.ContextMenuDismissRequested -= OnContextMenuDismissRequested;
            // 菜单还开着就换树 / 收尾：把 Closed 报回去，旧枢纽的挂起状态不会留在那儿。
            if (_menuLink is not null)
            {
                _interaction.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, _menuLink));
            }
            _interaction = null;
        }

        _menuLink = null;

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
        // Live node position + link updates while dragging (Anchor/Size changes). During a zoom the
        // surface stamps collapsed node geometry + links synchronously in JS (applyZoomSurface);
        // re-rendering the whole tree here would rebuild the links/nodes layer from round-tripped
        // values mid-zoom and flicker.
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            if (WorkflowGeometryScope.IsZooming) return;
            InvokeAsync(StateHasChanged);
        }
    }

    private void OnNodesOrLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Node add/remove changes the per-node subscription set; both feed the pooled layer.
        UnsubscribeNodeChanges();
        if (Tree is not null) SubscribeNodeChanges(Tree);
        InvokeAsync(StateHasChanged);
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnVirtualLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    // 右键落在表面上：DOM 事件给出屏幕坐标（先记下），把这次右键喂进 hub 后由 hub 命中并报
    // ContextMenuRequested。菜单因此跟着指针弹；空白画布也会走到这里，但那里不给菜单。
    private async Task OnSurfaceContextMenu(MouseEventArgs e)
    {
        // 客户端坐标取整后写出去：整数字符串没有小数点，区域设置就碰不到它
        _menuLeft = (int)Math.Round(e.ClientX);
        _menuTop = (int)Math.Round(e.ClientY);

        if (_surface is not null)
        {
            await _surface.ForwardPointerAsync(PointerPhase.Pressed, e.ClientX, e.ClientY, PointerButtonKind.Right);
        }
    }

    // ContextMenuRequested 是「谁弹菜单谁订」的那一相，否决归 ContextMenuRequesting，这里不查 PreventDefault。
    // 命中连线就弹；Position 是画布坐标（报回 hub 用），屏幕坐标用上面右键时记下的那两个。
    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        if (e.Link is null) return;

        _menuLink = e.Link;
        _menuPosition = e.Position;
        // 报回 hub：菜单在屏期间挂起悬停，指针移到菜单上不会清掉这次选中的连线。
        _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, e.Position, e.Link));
        InvokeAsync(StateHasChanged);
    }

    // 菜单指着的那条线已经不在树上：hub 请宿主收起这份菜单（它收不了宿主的弹窗）。
    // 收起照常报 Closed，挂起随之放开。
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link)) return;
        CloseContextMenu();
    }

    private void CloseContextMenu()
    {
        if (_menuLink is null) return;

        var link = _menuLink;
        _menuLink = null;
        _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, link));
        InvokeAsync(StateHasChanged);
    }

    private void DeleteLinkFromMenu()
    {
        var link = _menuLink;
        CloseContextMenu();
        // 与其余平台一致：不看 CanExecute，命令自己会排队或拒绝。
        link?.DeleteCommand.Execute(null);
    }

    // 包在表面外的一层：表面根的键盘宿主只认 Delete，Escape 在这里收口，两边的按键路由互不打扰。
    private void OnSurfaceMenuKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape") CloseContextMenu();
    }

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

    private static int HexByte(string hex, int offset)
        => Convert.ToInt32(hex.Substring(offset, 2), 16);
}
