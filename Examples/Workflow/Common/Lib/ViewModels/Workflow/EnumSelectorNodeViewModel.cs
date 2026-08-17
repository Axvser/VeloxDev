using Demo.ViewModels.Workflow.Helper;
using System.ComponentModel;
using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "枚举选择器节点，可将输入按枚举成员路由到多个执行路径。默认大小为 280×380。")]
[AgentContext(AgentLanguages.English, "Enum selector node that routes input to multiple execution paths based on enum members. Default size: 280×380.")]
[WorkflowBuilder.Node<EnumSelectorHelper>(workSemaphore: 1)]
public partial class EnumSelectorNodeViewModel : ICompileTimeRouter, ICompileTimeAware
{
    public EnumSelectorNodeViewModel()
    {
        InitializeWorkflow();
        OutputSlots.SetSelector(typeof(NetworkRequestMethod));
    }

    [AgentContext(AgentLanguages.Chinese, "输入口（接收端）")]
    [AgentContext(AgentLanguages.English, "Input slot (receiver)")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "输出口（发送端）")]
    [AgentContext(AgentLanguages.English, "Output slot (sender). Supports enum types (NetworkRequestMethod, VoltageRange, ModelProtocol) " +
        "and the instance-driven CustomRouteSelector. " +
        "For CustomRouteSelector pass its JSON to 'selectorTypeOrJson' and 'Demo.ViewModels.CustomRouteSelector' to 'nonEnumTypeName' when calling SetEnumSlotCollection.")]
    [VeloxProperty]
    [SlotSelectors(typeof(NetworkRequestMethod), typeof(VoltageRange), typeof(ModelProtocol), typeof(CustomRouteSelector))]
    public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }

    partial void OnOutputSlotsChanged(SlotEnumerator<SlotViewModel>? oldValue, SlotEnumerator<SlotViewModel>? newValue)
    {
        oldValue?.PropertyChanged -= OnOutputSlotsPropertyChanged;
        newValue?.PropertyChanged += OnOutputSlotsPropertyChanged;
        OnPropertyChanged(nameof(EnumType));
        OnPropertyChanged(nameof(EnumValues));
        OnPropertyChanged(nameof(SelectedValue));
    }

    private void OnOutputSlotsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SelectorTypeName 在 SelectorType 之后触发（SlotEnumerator 先设置类型），
        // 因此触发时 EnumValues 读到的已是新的选择器类型。
        if (e.PropertyName == nameof(SlotEnumerator<>.SelectorTypeName))
        {
            OnPropertyChanged(nameof(EnumType));
            OnPropertyChanged(nameof(EnumValues));
        }
        // CurrentValue 现由枚举器持有，这里同步到节点的 SelectedValue 绑定。
        // UI 框架（WPF/Avalonia）的 ComboBox 在 ItemsSource 换新数组的同一帧内会丢弃
        // 已重绑的选中项：条目容器尚未按新源生成，字符串匹配失败 → 显示为空。
        // 延迟一帧，让 ItemsSource 先落地并生成条目，再重绑选中项。
        if (e.PropertyName == nameof(SlotEnumerator<>.CurrentValue))
        {
            var sc = SynchronizationContext.Current;
            if (sc is not null)
                sc.Post(_ => OnPropertyChanged(nameof(SelectedValue)), null);
            else
                OnPropertyChanged(nameof(SelectedValue));
        }
    }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [VeloxProperty] private string title = "Enum Selector";

    [AgentContext(AgentLanguages.Chinese, "是否自动广播给下游节点")]
    [AgentContext(AgentLanguages.English, "When true, the node automatically forwards the result to all connected downstream nodes after execution.")]
    [VeloxProperty] private bool autoBroadcast = true;

    [AgentContext(AgentLanguages.Chinese, "当前选中的枚举值，决定路由到哪个输出口")]
    [AgentContext(AgentLanguages.English, "Currently selected enum value. Determines which output slot receives the routed input. Set to the desired enum member name (string) or its underlying integer value.")]
    public object? SelectedValue
    {
        get => OutputSlots?.CurrentValue;
        set { if (OutputSlots is not null) OutputSlots.CurrentValue = value; }
    }

    [VeloxProperty] private string lastRouted = "-";

    // 执行序列号（手动实现，生成器暂未覆盖）
    private int lastExecutionOrder;
    public int LastExecutionOrder
    {
        get => lastExecutionOrder;
        set
        {
            if (lastExecutionOrder == value) return;
            lastExecutionOrder = value;
            OnPropertyChanged(nameof(LastExecutionOrder));
            OnPropertyChanged(nameof(HasExecutionOrder));
            OnPropertyChanged(nameof(ExecutionOrderText));
        }
    }
    public bool HasExecutionOrder => LastExecutionOrder > 0 || IsCompileStopped;
    public string ExecutionOrderText => IsCompileStopped ? "⊘" : LastExecutionOrder > 0 ? $"#{LastExecutionOrder}" : "-";

    public bool HasInputSlot => _inputSlot is not null;

    public Type? EnumType => OutputSlots?.SelectorType;

    public string[] EnumValues
    {
        get
        {
            var t = EnumType;
            if (t == null) return [];
            if (t == typeof(bool)) return ["False", "True"];
            return Enum.GetNames(t);
        }
    }

    public SlotViewModel? GetSlotForValue(object value)
        => OutputSlots?.TrySelect(OutputSlots.NormalizeSelectorValue(value), out var slot) == true ? slot : null;

    public string GetSlotLabel(int index)
    {
        var items = OutputSlots?.Items;
        if (items == null || index < 0 || index >= items.Count) return "?";
        return items[index].Slot?.ToString() ?? "?";
    }

    /// <summary>编译期注入的编译身份（Order = -1 表示绝对停止）。</summary>
    public ICompileContext? CompileContext { get; private set; }

    /// <summary>编译期是否处于绝对停止状态（未选中静态分支 / 终止）。</summary>
    public bool IsCompileStopped => CompileContext is { Order: -1 };

    public void AttachCompileTimeContext(ICompileContext context)
    {
        CompileContext = context;
        LastExecutionOrder = context.Order >= 0 ? context.Order + 1 : 0;
        OnPropertyChanged(nameof(CompileContext));
        OnPropertyChanged(nameof(IsCompileStopped));
        OnPropertyChanged(nameof(HasExecutionOrder));
        OnPropertyChanged(nameof(ExecutionOrderText));
    }

    /// <summary>
    /// 编译模式：Static 编译期只返回当前选中分支；Dynamic 返回全部分支（运行期定 key）。
    /// 不同的模式下 <see cref="GetRouteTable"/> 返回的字典不同。
    /// </summary>
    [AgentContext(AgentLanguages.Chinese, "编译模式：Static 编译期锁定当前选中分支（未选中分支被剪除，其下游节点 Order = -1 绝对停止）；Dynamic 运行期按数据负载重新选分支（全部分支存活，编译期 payload 为 null 时 ResolveRouteKey 返回 null）。通过 PatchNodeProperties 设置，如 {\"CompileMode\":\"Static\"}。")]
    [AgentContext(AgentLanguages.English, "Compile mode: Static locks the currently selected branch at compile time (unselected branches are pruned; their downstream nodes get Order = -1 / absolute stop); Dynamic re-selects the branch at runtime from the data payload (all branches stay alive; ResolveRouteKey returns null for a null compile-time payload). Set via PatchNodeProperties, e.g. {\"CompileMode\":\"Static\"}.")]
    [VeloxProperty] private RouterCompileMode _compileMode = RouterCompileMode.Dynamic;

    /// <summary>编译模式下拉数据源。</summary>
    public RouterCompileMode[] CompileModeOptions => [RouterCompileMode.Static, RouterCompileMode.Dynamic];

    /// <summary>
    /// 统一路由入口：
    /// - Static：key 由当前选中的枚举值决定（编译期可定）；
    /// - Dynamic：编译期(null payload)返回 null → IsDynamic；运行期读共享字段 selector.value，否则回退当前选中值。
    /// </summary>
    public Task<object?> ResolveRouteKey(object? payload)
    {
        if (CompileMode == RouterCompileMode.Dynamic && payload is null)
            return Task.FromResult<object?>(null);

        if (payload is IRuntimeContext ctx && ctx.TryGet("selector.value", out var v) && v is string s)
            return Task.FromResult(OutputSlots is not null ? OutputSlots.NormalizeSelectorValue(s) : s);

        return Task.FromResult(OutputSlots is not null
            ? OutputSlots.NormalizeSelectorValue(OutputSlots.CurrentValue)
            : null);
    }

    /// <summary>编译时路由表（随编译模式变化）：Static 只含当前选中分支；Dynamic 含全部分支（保留 1:N 扇出）。</summary>
    public Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable()
    {
        var dict = new Dictionary<object, List<IWorkflowNodeViewModel>>();
        if (OutputSlots is null)
            return Task.FromResult(EmptyRouteTable());

        if (CompileMode == RouterCompileMode.Static)
        {
            // 静态：只返回当前选中分支（无下游则登记为终端分支）
            var key = OutputSlots.NormalizeSelectorValue(OutputSlots.CurrentValue);
            var slot = key is not null && OutputSlots.TrySelect(key, out var s) ? s : null;
            if (slot is not null)
            {
                if (slot.Targets.Count == 0)
                {
                    if (key is not null && !dict.ContainsKey(key)) dict[key] = [];
                }
                else
                {
                    foreach (var target in slot.Targets)
                        if (target.Parent is not null)
                            AddTarget(dict, key!, target.Parent);
                }
            }
        }
        else
        {
            // 动态：全部分支（含无下游的终端分支，登记为空列表）
            foreach (var item in OutputSlots.Items)
            {
                var slot = item.Slot;
                if (item.Value is null || slot is null) continue;
                if (slot.Targets.Count == 0)
                {
                    if (!dict.ContainsKey(item.Value)) dict[item.Value] = [];
                    continue;
                }
                foreach (var target in slot.Targets)
                    if (target.Parent is not null)
                        AddTarget(dict, item.Value, target.Parent);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>>(
            dict.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<IWorkflowNodeViewModel>)kv.Value.AsReadOnly()));
    }

    private static void AddTarget(Dictionary<object, List<IWorkflowNodeViewModel>> dict, object key,
        IWorkflowNodeViewModel target)
    {
        if (!dict.TryGetValue(key, out var list))
            dict[key] = list = [];
        if (!list.Contains(target))
            list.Add(target);
    }

    private static IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>> EmptyRouteTable()
        => new Dictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>();
}
