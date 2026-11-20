using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms workflow slot layout synchronization behavior.
/// </summary>
public sealed class WorkflowSlotLayoutBehavior
{
    private sealed class LayoutState
    {
        public bool IsEnabled { get; set; }
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
        public string? SlotNames { get; set; }
        public string? SlotEnumeratorNames { get; set; }
        public string? CoordinateHostName { get; set; }
        public Type? CoordinateHostType { get; set; }
        public string? ParentHostName { get; set; }
        public HashSet<string> SlotPropertyNames { get; } = [];

        // 自动模式（没有命名插槽）每次同步都要遍历整棵子控件树并反射找 ViewModel —— 平移的每个鼠标
        // 消息都会走一遍。树在两次行重建之间是稳定的，所以把解析结果缓存起来；ControlAdded/Removed
        // 与 Refresh 把它置脏。
        public List<Control>? CachedSlotControls { get; set; }
        public bool SlotControlsDirty { get; set; } = true;
    }

    private static readonly ConditionalWeakTable<Control, LayoutState> States = new();
    private static readonly ConditionalWeakTable<IWorkflowSlotViewModel, LocalAnchorBox> LocalAnchors = new();

    /// <summary>The slot's centre in its node view's client coordinates, in the same units as the node's Anchor/Size.</summary>
    private sealed class LocalAnchorBox
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    // 曲线要的是「端口在节点内的局部位置」：它只随布局/缩放变，不随画布平移变。从控件左/上逐级
    // 累加到节点根即可得到，完全不经过屏幕坐标，因此不会在测量滞后时把平移量混进来。
    internal static bool TryGetLocalAnchor(IWorkflowSlotViewModel slot, out double x, out double y)
    {
        if (slot is not null && LocalAnchors.TryGetValue(slot, out var box))
        {
            x = box.X;
            y = box.Y;
            return true;
        }

        x = 0d;
        y = 0d;
        return false;
    }

    /// <summary>
    /// Gets whether workflow slot layout behavior is enabled for the specified control.
    /// </summary>
    public static bool GetIsEnabled(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).IsEnabled;
    }

    /// <summary>
    /// Sets whether workflow slot layout behavior is enabled for the specified control.
    /// </summary>
    public static void SetIsEnabled(Control element, bool value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var state = GetState(element);
        if (state.IsEnabled == value)
        {
            return;
        }

        Detach(element);
        state.IsEnabled = value;
        if (value)
        {
            Attach(element);
        }
    }

    /// <summary>
    /// Gets the configured named slot controls.
    /// </summary>
    public static string? GetSlotNames(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).SlotNames;
    }

    /// <summary>
    /// Sets the configured named slot controls.
    /// </summary>
    public static void SetSlotNames(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var state = GetState(element);
        state.SlotNames = value;
        state.SlotControlsDirty = true;
        RequestSync(element);
    }

    /// <summary>
    /// Gets the configured slot enumerator member names.
    /// </summary>
    public static string? GetSlotEnumeratorNames(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).SlotEnumeratorNames;
    }

    /// <summary>
    /// Sets the configured slot enumerator member names.
    /// </summary>
    public static void SetSlotEnumeratorNames(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var state = GetState(element);
        state.SlotEnumeratorNames = value;
        state.SlotControlsDirty = true;
        RequestSync(element);
    }

    /// <summary>
    /// Gets the configured coordinate host name.
    /// </summary>
    public static string? GetCoordinateHostName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).CoordinateHostName;
    }

    /// <summary>
    /// Sets the configured coordinate host name.
    /// </summary>
    public static void SetCoordinateHostName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).CoordinateHostName = value;
        RequestSync(element);
    }

    /// <summary>
    /// Gets the configured coordinate host type.
    /// </summary>
    public static Type? GetCoordinateHostType(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).CoordinateHostType;
    }

    /// <summary>
    /// Sets the configured coordinate host type.
    /// </summary>
    public static void SetCoordinateHostType(Control element, Type? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).CoordinateHostType = value;
        RequestSync(element);
    }

    /// <summary>
    /// Gets the configured parent host name.
    /// </summary>
    public static string? GetParentHostName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).ParentHostName;
    }

    /// <summary>
    /// Sets the configured parent host name.
    /// </summary>
    public static void SetParentHostName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).ParentHostName = value;
        RequestSync(element);
    }

    /// <summary>
    /// Requests the control to refresh layout-related workflow visuals.
    /// </summary>
    public static void Refresh(Control control)
    {
        if (control is null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        GetState(control).SlotControlsDirty = true;
        RequestSync(control);
    }

    /// <summary>
    /// Synchronously re-measures and writes back the slot anchors of a node view.
    /// The deferred <see cref="Refresh"/> path coalesces to the message loop, which
    /// drains after any forced synchronous repaint (e.g. a surface ApplyPan that
    /// calls <c>Update()</c>) — so during zoom collapse / canvas expansion the links
    /// repaint with stale endpoints for one frame. Call this right after the node
    /// view's geometry has been moved or scaled so link endpoints refresh in the
    /// same turn, before that forced repaint.
    /// </summary>
    /// <remarks>
    /// Falls back to the deferred path when the control has no handle yet (nothing
    /// to measure) or measurement throws because a descendant slot's geometry is
    /// still settling.
    /// </remarks>
    public static void SyncNow(Control control)
    {
        if (control is null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        if (!States.TryGetValue(control, out var state) || !state.IsEnabled || control.IsDisposed)
        {
            return;
        }

        if (!control.IsHandleCreated)
        {
            RequestSync(control);
            return;
        }

        try
        {
            Sync(control);
        }
        catch (InvalidOperationException)
        {
            // 后代插槽控件还没有可用几何（例如在句柄创建期间、子句柄尚未存在时被调用）；重新排队，让锚点在布局落定后仍能落下。
            RequestSync(control);
        }
    }

    private static void Attach(Control control)
    {
        var state = GetState(control);
        control.HandleCreated += OnHandleCreated;
        control.VisibleChanged += OnVisibleChanged;
        control.Layout += OnLayout;
        control.SizeChanged += OnSizeChanged;
        control.ControlAdded += OnControlCollectionChanged;
        control.ControlRemoved += OnControlCollectionChanged;
        control.Disposed += OnDisposed;
        UpdatePropertyChangedSubscription(control, state);
        RequestSync(control);
    }

    private static void Detach(Control control)
    {
        if (!States.TryGetValue(control, out var state))
        {
            return;
        }

        control.HandleCreated -= OnHandleCreated;
        control.VisibleChanged -= OnVisibleChanged;
        control.Layout -= OnLayout;
        control.SizeChanged -= OnSizeChanged;
        control.ControlAdded -= OnControlCollectionChanged;
        control.ControlRemoved -= OnControlCollectionChanged;
        control.Disposed -= OnDisposed;

        if (state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            state.PropertyChangedSource = null;
            state.PropertyChangedHandler = null;
        }

        // Disposed 也走这里；不置位的话，销毁前排队的同步仍会通过 IsEnabled 闸门。
        state.IsEnabled = false;
        state.CachedSlotControls = null;
        state.SlotControlsDirty = true;
        CancelSync(control);
    }

    private static void OnHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            RequestSync(control);
        }
    }

    private static void OnVisibleChanged(object? sender, EventArgs e)
    {
        if (sender is Control control && control.Visible)
        {
            RequestSync(control);
        }
    }

    private static void OnLayout(object? sender, LayoutEventArgs e)
    {
        if (sender is Control control)
        {
            RequestSync(control);
        }
    }

    private static void OnSizeChanged(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            RequestSync(control);
        }
    }

    private static void OnControlCollectionChanged(object? sender, ControlEventArgs e)
    {
        if (sender is Control control)
        {
            // 子树（含插槽行）变了：下一次同步重新解析，绝不沿用可能已经销毁的控件。
            if (States.TryGetValue(control, out var state))
            {
                state.SlotControlsDirty = true;
            }

            RequestSync(control);
        }
    }

    private static void OnDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            Detach(control);
        }
    }

    private static void OnNodePropertyChanged(Control control, PropertyChangedEventArgs e)
    {
        if (!States.TryGetValue(control, out var state)
            ||
           (e.PropertyName is not null && !state.SlotPropertyNames.Contains(e.PropertyName)))
        {
            return;
        }

        RequestSync(control);
    }

    private static void UpdatePropertyChangedSubscription(Control control, LayoutState state)
    {
        var dataSource = ResolveDataSource(control);
        if (ReferenceEquals(dataSource, state.PropertyChangedSource))
        {
            return;
        }

        if (state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            state.PropertyChangedSource = null;
            state.PropertyChangedHandler = null;
        }

        if (dataSource is INotifyPropertyChanged notify)
        {
            PropertyChangedEventHandler handler = (_, e) => OnNodePropertyChanged(control, e);
            state.PropertyChangedSource = notify;
            state.PropertyChangedHandler = handler;
            notify.PropertyChanged += handler;
        }
    }

    // 同一棵树的请求并进共享批次，由一次 BeginInvoke 合并执行；句柄还没建好时留在队里，下一次触发再排。
    private static void RequestSync(Control control)
    {
        if (!States.TryGetValue(control, out var state) || !state.IsEnabled)
        {
            return;
        }

        var node = ResolveNode(control);
        object owner = node?.Parent ?? (object)control;
        var target = control.FindForm() ?? control;
        var batch = WorkflowLayoutBatch.For(owner, action => Post(target, action));

        // 已排队的工作可能在行为被关掉之后才轮到：那一刻不要再量一次。
        batch.Request(control, new Action(() =>
        {
            if (States.TryGetValue(control, out var current) && current.IsEnabled)
            {
                Sync(control);
            }
        }));
        batch.Schedule();
    }

    private static void CancelSync(Control control)
    {
        object owner = ResolveNode(control)?.Parent ?? (object)control;
        WorkflowLayoutBatch.For(owner).Cancel(control);
    }

    // 窗口正在销毁时 BeginInvoke 会抛 InvalidOperationException；返回 false 让批次保留队列、等下一个触发再试。
    private static bool Post(Control target, Action flush)
    {
        if (target.IsDisposed || !target.IsHandleCreated)
        {
            return false;
        }

        try
        {
            target.BeginInvoke(flush);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void Sync(Control control)
    {
        if (States.TryGetValue(control, out var state))
        {
            // 重新建立节点的 PropertyChanged 订阅：Attach 在构造时跑，那时节点 VM 还是 null，订阅是空操作；不在这里重跑，插槽锚点只会被量一次、连线永不跟随节点拖拽。
            // UpdatePropertyChangedSubscription 里的 ReferenceEquals 防护让它在已订阅正确源后成为廉价空操作。
            UpdatePropertyChangedSubscription(control, state);
        }

        var node = ResolveNode(control);
        if (node is null)
        {
            return;
        }

        var parentHost = ResolveParentHost(control);
        var coordinateHost = ResolveCoordinateHost(control, parentHost);

        var slotNames = GetAllSlotNames(control);
        var enumeratorNames = GetAllSlotEnumeratorNames(control);

        // 重建「变化时该触发同步」的属性名集合。
        if (state is not null)
        {
            state.SlotPropertyNames.Clear();
            state.SlotPropertyNames.Add(nameof(IWorkflowNodeViewModel.Anchor));
            state.SlotPropertyNames.Add(nameof(IWorkflowNodeViewModel.Size));
            // 控件名（如 "PART_OutputSlots"）与 ViewModel 属性名（"OutputSlots"）不同；两者都加，
            // OnPropertyChanged("OutputSlots") 才匹配得上。
            foreach (var name in slotNames)
            {
                state.SlotPropertyNames.Add(name);
                if (name.StartsWith("PART_"))
                    state.SlotPropertyNames.Add(name.Substring(5));
            }
            foreach (var name in enumeratorNames)
            {
                state.SlotPropertyNames.Add(name);
                if (name.StartsWith("PART_"))
                    state.SlotPropertyNames.Add(name.Substring(5));
            }
            // 标准属性名一律带兜底默认值，覆盖 ViewModel 直接属性与 SlotEnumerator 成员。
            state.SlotPropertyNames.Add("InputSlot");
            state.SlotPropertyNames.Add("OutputSlot");
            state.SlotPropertyNames.Add("OutputSlots");
        }

        if (slotNames.Length == 0 && enumeratorNames.Length == 0)
        {
            var slotViews = ResolveAutoSlotControls(control, state);
            foreach (var slotView in slotViews)
            {
                if (slotView.IsDisposed)
                {
                    continue;
                }

                SyncSlot(control, coordinateHost, slotView, node);
            }

            return;
        }

        foreach (var slotName in slotNames)
        {
            SyncNamedSlot(parentHost, control, coordinateHost, node, slotName);
        }

        foreach (var enumeratorName in enumeratorNames)
        {
            SyncSlotEnumerator(parentHost, control, coordinateHost, node, enumeratorName);
        }

    }

    // 自动模式下的插槽控件表。控件树在两次行重建之间稳定，所以只在脏标记或缓存失效时重新解析。
    private static List<Control> ResolveAutoSlotControls(Control control, LayoutState? state)
    {
        if (state is not null
            && !state.SlotControlsDirty
            && state.CachedSlotControls is { } cached
            && IsSlotCacheValid(cached, control))
        {
            return cached;
        }

        var list = new List<Control>();
        foreach (var descendant in EnumerateDescendants(control))
        {
            if (ResolveSlot(descendant) is not null)
            {
                list.Add(descendant);
            }
        }

        if (state is not null)
        {
            state.CachedSlotControls = list;
            state.SlotControlsDirty = false;
        }

        return list;
    }

    private static bool IsSlotCacheValid(List<Control> cached, Control root)
    {
        for (var i = 0; i < cached.Count; i++)
        {
            var slot = cached[i];
            if (slot is null || slot.IsDisposed)
            {
                return false;
            }

            var ancestor = slot.Parent;
            while (ancestor is not null && !ReferenceEquals(ancestor, root))
            {
                ancestor = ancestor.Parent;
            }

            if (ancestor is null)
            {
                return false;
            }
        }

        return true;
    }

    private static void SyncNamedSlot(Control parentHost, Control host, Control? coordinateHost, IWorkflowNodeViewModel node, string slotName)
    {
        var slotControl = FindControlByName(parentHost, slotName);
        if (slotControl is null)
        {
            return;
        }

        // 命名控件可能是插槽视图本身，也可能是包含插槽视图的宿主面板（如节点模板里的 PART_InputSlot）。把它下面解析出的 SlotView 都同步 —— 否则被命名为插槽的容器其子插槽视图永不被测量、锚点停在 NaN，
        // WorkflowSlotUpdateGate 的 NaN 检查会隐藏所有以此结尾的连线。
        if (ResolveSlot(slotControl) is not null)
        {
            SyncSlot(host, coordinateHost, slotControl, node);
            return;
        }

        foreach (var slotView in EnumerateDescendants(slotControl).Where(x => ResolveSlot(x) is not null))
        {
            SyncSlot(host, coordinateHost, slotView, node);
        }
    }

    private static void SyncSlotEnumerator(Control parentHost, Control host, Control? coordinateHost, IWorkflowNodeViewModel node, string enumeratorName)
    {
        var itemsHost = FindControlByName(parentHost, enumeratorName);
        if (itemsHost is null)
        {
            return;
        }

        foreach (var slotView in EnumerateDescendants(itemsHost).Where(x => ResolveSlot(x) is not null))
        {
            SyncSlot(host, coordinateHost, slotView, node);
        }
    }

    private static void SyncSlot(Control host, Control? coordinateHost, Control slotControl, IWorkflowNodeViewModel node)
    {
        var slot = ResolveSlot(slotControl);
        if (slot is null || slotControl.Width <= 0 || slotControl.Height <= 0)
        {
            return;
        }

        var center = new System.Drawing.Point(slotControl.Width / 2, slotControl.Height / 2);
        if (ResolveLocalCenter(host, slotControl) is { } localCenter)
        {
            var box = LocalAnchors.GetValue(slot, static _ => new LocalAnchorBox());
            box.X = localCenter.X;
            box.Y = localCenter.Y;
        }

        var screenPoint = slotControl.PointToScreen(center);

        if (coordinateHost is not null)
        {
            // PointToClient 得到的是坐标宿（画布）客户区里的中心，正是连线渲染所在的系 —— 节点的 Location 已含平移 + ActualOffset，所以不减偏移。
            // 这里若用 SlotAnchorFromVisualCenter 会减掉 ActualOffset，让每条连线偏移 -ActualOffset（只要设了 NegativeOffset 就恒定左上移）。
            var coordinatePoint = coordinateHost.PointToClient(screenPoint);
            slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromCanvasLocal(
                coordinatePoint.X, coordinatePoint.Y, slot.Anchor.Layer);
            return;
        }

        var localPoint = host.PointToClient(screenPoint);
        slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromNode(
            node.Anchor.Horizontal, node.Anchor.Vertical, localPoint.X, localPoint.Y, slot.Anchor.Layer);
    }

    // 从插槽控件沿父链累加 Left/Top 到节点根，得到节点客户区里的中心点；不经过屏幕坐标。
    private static System.Drawing.PointF? ResolveLocalCenter(Control host, Control slotControl)
    {
        var x = slotControl.Width / 2d;
        var y = slotControl.Height / 2d;

        for (var current = slotControl; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, host))
            {
                return new System.Drawing.PointF((float)x, (float)y);
            }

            x += current.Left;
            y += current.Top;
        }

        return null;
    }

    private static Control? ResolveCoordinateHost(Control control, Control parentHost)
    {
        var hostName = GetCoordinateHostName(control);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var namedHost = FindControlByName(parentHost, hostName!);
            if (namedHost is not null)
            {
                return namedHost;
            }
        }

        // 没有显式坐标宿时，先认表面行为手里那块画布；泛型 v:Canvas 不继承 Panel，按类型找不到。
        if (WorkflowSurfaceBehavior.FindSurfaceCanvas(control) is { } surfaceCanvas)
        {
            return surfaceCanvas;
        }

        var hostType = GetCoordinateHostType(control) ?? typeof(Panel);
        var current = parentHost;
        while (current is not null)
        {
            if (hostType.IsAssignableFrom(current.GetType()))
            {
                return current;
            }

            current = current.Parent;
        }

        return parentHost;
    }

    private static Control ResolveParentHost(Control control)
    {
        var hostName = GetParentHostName(control);
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return control;
        }

        return FindControlByName(control, hostName!) ?? control;
    }

    private static string[] GetAllSlotNames(Control control)
        => EnumerateConfiguredNames(GetSlotNames(control)).Distinct(StringComparer.Ordinal).ToArray();

    private static string[] GetAllSlotEnumeratorNames(Control control)
        => EnumerateConfiguredNames(GetSlotEnumeratorNames(control)).Distinct(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> EnumerateConfiguredNames(string? names)
    {
        if (string.IsNullOrWhiteSpace(names))
        {
            return Enumerable.Empty<string>();
        }

        return names!.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);
    }

    private static Control? FindControlByName(Control root, string name)
    {
        foreach (var control in EnumerateSelfAndDescendants(root))
        {
            if (string.Equals(control.Name, name, StringComparison.Ordinal))
            {
                return control;
            }
        }

        return null;
    }

    private static IEnumerable<Control> EnumerateSelfAndDescendants(Control root)
    {
        yield return root;
        foreach (var descendant in EnumerateDescendants(root))
        {
            yield return descendant;
        }
    }

    private static IEnumerable<Control> EnumerateDescendants(Control root)
    {
        foreach (var child in root.Controls.OfType<Control>())
        {
            yield return child;
            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static object? ResolveDataSource(Control control)
    {
        if (control.DataBindings.Count > 0)
        {
            foreach (var binding in control.DataBindings.OfType<Binding>())
            {
                if (binding.DataSource is not null)
                {
                    return binding.DataSource;
                }
            }
        }

        return ResolveValue(control, "ViewModel") ?? ResolveValue(control, "DataContext") ?? ResolveValue(control, "BindingContext") ?? control.Tag;
    }

    private static IWorkflowNodeViewModel? ResolveNode(Control control)
    {
        var current = control;
        while (current is not null)
        {
            var node = ResolveValue(current, "ViewModel") as IWorkflowNodeViewModel
                ?? ResolveValue(current, "DataContext") as IWorkflowNodeViewModel
                ?? ResolveValue(current, "BindingContext") as IWorkflowNodeViewModel
                ?? current.Tag as IWorkflowNodeViewModel;

            if (node is not null)
            {
                return node;
            }

            current = current.Parent;
        }

        return null;
    }

    private static IWorkflowSlotViewModel? ResolveSlot(Control control)
    {
        return control.Tag as IWorkflowSlotViewModel
            ?? ResolveValue(control, "ViewModel") as IWorkflowSlotViewModel
            ?? ResolveValue(control, "DataContext") as IWorkflowSlotViewModel
            ?? ResolveValue(control, "BindingContext") as IWorkflowSlotViewModel;
    }

    private static object? ResolveValue(Control control, string propertyName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var property = control.GetType().GetProperty(propertyName, flags);
        if (property?.CanRead != true || property.GetIndexParameters().Length != 0)
        {
            return null;
        }

        return property.GetValue(control);
    }

    private static LayoutState GetState(Control element)
        => States.GetValue(element, static _ => new LayoutState());
}
