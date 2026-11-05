using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSlotLayoutBehavior : AvaloniaObject
{
    private sealed class LayoutState
    {
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public bool Syncing { get; set; }
        public HashSet<string> SlotPropertyNames { get; } = [];
    }

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, bool>("IsEnabled");

    public static readonly AttachedProperty<string?> SlotNamesProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, string?>("SlotNames");

    public static readonly AttachedProperty<string?> SlotEnumeratorNamesProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, string?>("SlotEnumeratorNames");

    public static readonly AttachedProperty<string?> CoordinateHostNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, string?>("CoordinateHostName");

    public static readonly AttachedProperty<Type?> CoordinateHostTypeProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, Type?>("CoordinateHostType");

    private static readonly AttachedProperty<LayoutState?> StateProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, LayoutState?>("State");

    static WorkflowSlotLayoutBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<UserControl>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetSlotNames(AvaloniaObject element) => element.GetValue(SlotNamesProperty);

    public static void SetSlotNames(AvaloniaObject element, string? value) => element.SetValue(SlotNamesProperty, value);

    public static string? GetSlotEnumeratorNames(AvaloniaObject element) => element.GetValue(SlotEnumeratorNamesProperty);

    public static void SetSlotEnumeratorNames(AvaloniaObject element, string? value) => element.SetValue(SlotEnumeratorNamesProperty, value);

    public static string? GetCoordinateHostName(AvaloniaObject element) => element.GetValue(CoordinateHostNameProperty);

    public static void SetCoordinateHostName(AvaloniaObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    public static Type? GetCoordinateHostType(AvaloniaObject element) => element.GetValue(CoordinateHostTypeProperty);

    public static void SetCoordinateHostType(AvaloniaObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    private static void OnIsEnabledChanged(UserControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Attach(control);
            return;
        }

        Detach(control);
    }

    private static void Attach(UserControl control)
    {
        Detach(control);

        control.SetValue(StateProperty, new LayoutState());
        control.AttachedToVisualTree += OnAttachedToVisualTree;
        control.DetachedFromVisualTree += OnDetachedFromVisualTree;
        control.DataContextChanged += OnDataContextChanged;
        control.LayoutUpdated += OnLayoutUpdated;
        control.PropertyChanged += OnControlPropertyChanged;
        UpdatePropertyChangedSubscription(control);
        RequestSync(control, force: true);
    }

    private static void Detach(UserControl control)
    {
        control.AttachedToVisualTree -= OnAttachedToVisualTree;
        control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        control.DataContextChanged -= OnDataContextChanged;
        control.LayoutUpdated -= OnLayoutUpdated;
        control.PropertyChanged -= OnControlPropertyChanged;

        if (control.GetValue(StateProperty) is LayoutState state && state.PropertyChangedSource is not null)
            state.PropertyChangedSource.PropertyChanged -= OnNodePropertyChanged;

        CancelSync(control);
        control.ClearValue(StateProperty);
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is UserControl control)
            RequestSync(control, force: true);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is UserControl control)
            CancelSync(control);
    }

    private static void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not UserControl control)
            return;

        UpdatePropertyChangedSubscription(control);
        RequestSync(control, force: true);
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // 布局后同步回写（与 WinUI 家一致）：在渲染前读插槽中心，保证连线端点锚点与节点折叠同帧刷新，
        // 而不是迟一个 Dispatcher.Post(Render) 节拍（插槽漂移/闪烁的来源）。
        // 整棵树每次布局都会走到这里，所以先用指纹挡掉「输入没变」的节点，再进共享批次同步执行。
        if (sender is UserControl control)
            RequestSync(control, force: false);
    }

    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is UserControl control && e.Property == Visual.BoundsProperty)
            RequestSync(control, force: true);
    }

    private static void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not StyledElement element)
            return;

        if (element.GetValue(StateProperty) is not LayoutState state
            || (e.PropertyName is null || !state.SlotPropertyNames.Contains(e.PropertyName)))
            return;

        var control = element as UserControl;
        if (control is null && element is Visual visual)
            control = visual.GetVisualAncestors().OfType<UserControl>().FirstOrDefault(GetIsEnabled);

        if (control is not null)
            RequestSync(control, force: true);
    }

    private static void UpdatePropertyChangedSubscription(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
            return;

        if (state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= OnNodePropertyChanged;
            state.PropertyChangedSource = null;
        }

        if (control.DataContext is INotifyPropertyChanged notify)
        {
            state.PropertyChangedSource = notify;
            notify.PropertyChanged += OnNodePropertyChanged;
        }
    }

    // 同一棵树的节点共用一个批次：布局 pass 的同步回写走 Flush（同帧），其余触发走 Schedule（到 Render 优先级合并）。
    // 指纹只看模型几何、实测尺寸与插槽控件数 —— 宿主每帧布局时，没动过的节点在这里就被挡掉了。
    private static void RequestSync(UserControl control, bool force)
    {
        if (control.GetValue(StateProperty) is null
            || control.DataContext is not IWorkflowNodeViewModel node)
        {
            return;
        }

        var batch = WorkflowLayoutBatch.For(OwnerOf(control, node), PostToUiThread);
        var work = new Action(() => Sync(control));

        if (force)
        {
            batch.Request(control, work);
            batch.Schedule();
            return;
        }

        var stamp = WorkflowLayoutStamp.Capture(
            node, control.Bounds.Width, control.Bounds.Height, CountSlotControls(control));
        if (batch.Request(control, stamp, work))
        {
            batch.Flush();
        }
    }

    private static void CancelSync(UserControl control)
    {
        object owner = control.DataContext is IWorkflowNodeViewModel { Parent: { } tree } ? tree : control;
        WorkflowLayoutBatch.For(owner).Cancel(control);
    }

    private static object OwnerOf(UserControl control, IWorkflowNodeViewModel node)
        => node.Parent ?? (object)control;

    private static bool PostToUiThread(Action flush)
    {
        Dispatcher.UIThread.Post(flush, DispatcherPriority.Render);
        return true;
    }

    // 指纹里的插槽数：容器数量变了（枚举插槽增删、模板重建）必须重测，哪怕节点外框尺寸没变。
    private static int CountSlotControls(UserControl control)
    {
        var count = 0;

        foreach (var name in GetAllSlotNames(control))
        {
            if (!string.IsNullOrWhiteSpace(name) && control.FindControl<Control>(name) is not null)
            {
                count++;
            }
        }

        foreach (var name in GetAllSlotEnumeratorNames(control))
        {
            if (!string.IsNullOrWhiteSpace(name) && control.FindControl<ItemsControl>(name) is { } items)
            {
                count += items.ItemCount;
            }
        }

        return count;
    }

    private static void Sync(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
            return;

        if (state.Syncing)
            return;

        if (control.DataContext is not IWorkflowNodeViewModel node)
            return;

        state.Syncing = true;
        try
        {
            var parentHost = control;
            var coordinateHost = ResolveCoordinateHost(control, parentHost);
            var slotNames = GetAllSlotNames(control);
            var enumeratorNames = GetAllSlotEnumeratorNames(control);

            // 重建「变化时该触发同步」的属性名集合。
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

            foreach (var slotName in slotNames)
                SyncNamedSlot(parentHost, control, coordinateHost, node, slotName);

            foreach (var enumeratorName in enumeratorNames)
                SyncSlotEnumerator(parentHost, control, coordinateHost, node, enumeratorName);
        }
        finally
        {
            state.Syncing = false;
        }
    }

    private static void SyncNamedSlot(UserControl parentHost, UserControl host, Control? coordinateHost, IWorkflowNodeViewModel node, string? controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName))
            return;

        var slotControl = parentHost.FindControl<Control>(controlName!);
        if (slotControl is not null)
            SyncSlot(host, coordinateHost, slotControl, node);
    }

    private static void SyncSlotEnumerator(UserControl parentHost, UserControl host, Control? coordinateHost, IWorkflowNodeViewModel node, string enumeratorName)
    {
        var itemsControl = parentHost.FindControl<ItemsControl>(enumeratorName);
        if (itemsControl is null || itemsControl.ItemCount == 0)
            return;

        for (int i = 0; i < itemsControl.ItemCount; i++)
        {
            var container = itemsControl.ContainerFromIndex(i);
            var slotView = container?.GetVisualDescendants().OfType<Control>().FirstOrDefault(static x => x.DataContext is IWorkflowSlotViewModel);
            if (slotView is not null)
                SyncSlot(host, coordinateHost, slotView, node);
        }
    }

    private static void SyncSlot(UserControl host, Control? coordinateHost, Control control, IWorkflowNodeViewModel node)
    {
        if (control.DataContext is not IWorkflowSlotViewModel slot || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            return;

        if (coordinateHost is not null)
        {
            var centerOnCanvas = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), coordinateHost);
            if (centerOnCanvas is not null)
            {
                slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromVisualCenter(
                    centerOnCanvas.Value.X, centerOnCanvas.Value.Y, slot.Anchor.Layer, node.Parent?.Layout ?? new CanvasLayout());
                return;
            }
        }

        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), host);
        if (center is null)
            return;

        slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromNode(
            node.Anchor.Horizontal, node.Anchor.Vertical,
            center.Value.X, center.Value.Y,
            slot.Anchor.Layer);
    }

    private static Control? ResolveCoordinateHost(UserControl control, UserControl parentHost)
    {
        var hostName = GetCoordinateHostName(control);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var namedHost = ResolveNamedHost(parentHost, hostName);
            if (namedHost is not null)
                return namedHost;
        }

        var hostType = GetCoordinateHostType(control) ?? typeof(Canvas);
        return parentHost.GetVisualAncestors()
            .OfType<Control>()
            .Prepend(parentHost)
            .FirstOrDefault(x => hostType.IsAssignableFrom(x.GetType()));
    }

    private static Control? ResolveNamedHost(Control control, string? hostName)
    {
        if (hostName is null)
            return null;

        if (control.Name == hostName)
            return control;

        if (control.GetVisualAncestors().OfType<Control>().FirstOrDefault(x => x.Name == hostName) is { } ancestor)
            return ancestor;

        return null;
    }

    private static string[] GetAllSlotNames(UserControl control)
        => [.. EnumerateConfiguredNames(GetSlotNames(control))
            .Distinct(StringComparer.Ordinal)];

    private static string[] GetAllSlotEnumeratorNames(UserControl control)
        => [.. EnumerateConfiguredNames(GetSlotEnumeratorNames(control))
            .Distinct(StringComparer.Ordinal)];

    private static string[] EnumerateConfiguredNames(string? names)
        => string.IsNullOrWhiteSpace(names)
            ? []
            : names!.Split(',')
                .Select(static x => x.Trim())
                .Where(static x => x.Length > 0)
                .ToArray();
}
