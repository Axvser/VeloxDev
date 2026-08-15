using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "布尔选择器节点，将输入路由到 True 或 False 输出口。默认大小为 260*200")]
[AgentContext(AgentLanguages.English, "Bool selector node that routes input to True or False output slot based on Condition. Default size: 260×200")]
[WorkflowBuilder.Node<BoolSelectorHelper>(workSemaphore: 1)]
public partial class BoolSelectorNodeViewModel : ICompileTimeRouter, ICompileTimeAware
{
    public BoolSelectorNodeViewModel()
    {
        InitializeWorkflow();
        OutputSlots.SetSelector(typeof(bool));
    }

    [AgentContext(AgentLanguages.Chinese, "输入口")]
    [AgentContext(AgentLanguages.English, "Input slot (receiver). Connect an upstream output slot here to trigger routing.")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "输出口（False/True）")]
    [AgentContext(AgentLanguages.English, "Output slot (False/True)")]
    [VeloxProperty]
    [SlotSelectors(typeof(bool))]
    public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [AgentContext(AgentLanguages.English, "Display title shown in the node header.")]
    [VeloxProperty] private string title = "Bool Selector";

    [AgentContext(AgentLanguages.Chinese, "路由条件，true 走 TrueSlot，false 走 FalseSlot")]
    [AgentContext(AgentLanguages.English, "Routing condition. true routes to TrueSlot; false routes to FalseSlot.")]
    [VeloxProperty] private bool condition = true;

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

    public SlotViewModel? TrueSlot => OutputSlots?.TrySelect(true, out var s) == true ? s : null;
    public SlotViewModel? FalseSlot => OutputSlots?.TrySelect(false, out var s) == true ? s : null;

    /// <summary>编译期注入的编译身份（Order = -1 表示绝对停止）。</summary>
    public CompileContext? CompileContext { get; private set; }

    /// <summary>编译期是否处于绝对停止状态（未选中静态分支 / 终止）。</summary>
    public bool IsCompileStopped => CompileContext is { Order: -1 };

    public void AttachCompileTimeContext(CompileContext context)
    {
        CompileContext = context;
        LastExecutionOrder = context.Order >= 0 ? context.Order + 1 : 0;
        OnPropertyChanged(nameof(CompileContext));
        OnPropertyChanged(nameof(IsCompileStopped));
        OnPropertyChanged(nameof(HasExecutionOrder));
        OnPropertyChanged(nameof(ExecutionOrderText));
    }

    /// <summary>编译模式：Static 只返回当前选中分支；Dynamic 返回 True/False 全部分支。</summary>
    [VeloxProperty] private RouterCompileMode _compileMode = RouterCompileMode.Dynamic;

    /// <summary>编译模式下拉数据源。</summary>
    public RouterCompileMode[] CompileModeOptions => [RouterCompileMode.Static, RouterCompileMode.Dynamic];

    /// <summary>
    /// 统一路由入口：Static → Condition（编译期可定）；Dynamic → 编译期(null)返回 null（IsDynamic），
    /// 运行期读共享字段 selector.bool，否则回退 Condition。
    /// </summary>
    public Task<object?> ResolveRouteKey(object? payload)
    {
        if (CompileMode == RouterCompileMode.Dynamic && payload is null)
            return Task.FromResult<object?>(null);

        if (payload is RuntimeContext ctx && ctx.TryGet("selector.bool", out var v) && v is string s)
            return Task.FromResult<object?>(bool.TryParse(s, out var b) ? b : Condition);

        return Task.FromResult<object?>(Condition);
    }

    /// <summary>编译时路由表（随模式变化）：Static 只含当前选中分支；Dynamic 含 True/False（保留 1:N 扇出）。</summary>
    public Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable()
    {
        var dict = new Dictionary<object, List<IWorkflowNodeViewModel>>();
        if (CompileMode == RouterCompileMode.Static)
        {
            var slot = Condition ? TrueSlot : FalseSlot;
            if (slot is not null)
                foreach (var target in slot.Targets)
                    if (target.Parent is not null) AddTarget(dict, Condition, target.Parent);
        }
        else
        {
            if (TrueSlot is not null)
                foreach (var target in TrueSlot.Targets)
                    if (target.Parent is not null) AddTarget(dict, true, target.Parent);
            if (FalseSlot is not null)
                foreach (var target in FalseSlot.Targets)
                    if (target.Parent is not null) AddTarget(dict, false, target.Parent);
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
}
