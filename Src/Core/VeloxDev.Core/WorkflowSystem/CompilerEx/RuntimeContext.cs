using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 一次运行期的执行会话（也是那个公有携带 UID 的 VM）：
/// 上下文共享（UID / 顺序 / 日志 / 共享变量）+ 执行位置/决策状态（引擎维护、UI 绑定进度）。
/// 节点实现 <see cref="IRuntimeAware"/>，由引擎在驱动前注入本对象。
/// 继承 <see cref="ITaskContext"/>：编译器把本会话对象直接传入
/// <see cref="IWorkflowNodeViewModelHelper.ReceiveAsync"/>，并逐节点写 <see cref="Data"/>。
/// </summary>
public sealed partial class RuntimeContext : IRuntimeContext
{
    /// <summary>运行期执行会话，恒为 false（编译期才有 true）。</summary>
    public bool IsCompilePhase => false;

    // ── 上下文共享 ──
    [VeloxProperty] private Guid _uid = Guid.NewGuid();
    [VeloxProperty] private int _sequence = 0;
    [VeloxProperty] private ObservableCollection<string> _logs = [];

    // ── 执行位置 / 决策状态（引擎维护）──
    [VeloxProperty] private ActionEntry? _currentEntry;
    [VeloxProperty] private int _nodeIndex = -1;
    [VeloxProperty] private object? _branchKey;
    [VeloxProperty] private int _attempt;
    [VeloxProperty] private bool _isRunning;
    [VeloxProperty] private string _status = "Idle";

    /// <summary>
    /// 当前执行状态码 = 正在执行节点的**编译期固定编号**（CompileContext.Order）。
    /// 运行期只在这些固定编号之间跳跃，不重排编号。
    /// </summary>
    [VeloxProperty] private int _currentOrder = -1;

    // ── 数据流载荷（编译器驱动时逐节点注入；作为 ITaskContext 提供）──
    [VeloxProperty] private object? _data;
    [VeloxProperty] private IWorkflowSlotViewModel? _sender;
    [VeloxProperty] private IWorkflowSlotViewModel? _receiver;

    // 共享变量（黑板）：节点/引擎/UI 都可读写，非直接 UI 绑定，走方法访问
    private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);

    // 产物登记表（Key = 来源 Node 引用身份，Value = 该 pass 登记的产物 + pass 戳）：
    // 引擎逐节点驱动后登记，汇合点按输入组聚合；pass 戳用于区分「本 pass 真跑过」与「重跑被跳过的旧产物」。
    private readonly Dictionary<IWorkflowNodeViewModel, (int Attempt, object? Value)> _outputs =
        new(WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance);

    /// <summary>
    /// 节点是否在本次驱动中调用了 <see cref="Error"/> 或 <see cref="Warn"/>（请求重定向）。
    /// 由引擎在每次驱动节点前清除、驱动后检查。经 <see cref="IRuntimeContext"/> 读写。
    /// </summary>
    public bool RedirectRequested { get; set; }

    /// <summary>流程是否因「节点报错但未实现 <see cref="IRedirectable"/>」而提前结束（状态置为 -1）。</summary>
    public bool EndedWithError { get; set; }

    /// <summary>引擎请求的回退目标 Order（可为跨链）。RunAsync 读取后带该目标重跑整张图。</summary>
    public int? PendingRedirectTarget { get; set; }

    /// <summary>
    /// 引擎每 pass 开头写入的「当前重跑目标 Order」（首 pass 为 null）。
    /// 供产物收集区分两类被跳过的节点：目标之前的是契约保留 prefix（Order &lt; 该值），目标之后未重驱动的是陈旧分支。
    /// </summary>
    public int? ActiveRedirectTarget { get; set; }

    /// <summary>取下一个执行顺序号（自增）。</summary>
    public int Next() => Interlocked.Increment(ref _sequence);

    /// <summary>节点/引擎推送一条普通日志（带顺序前缀）。</summary>
    public void Log(string entry) => _logs.Add($"{Next():00}. {entry}");

    /// <summary>节点/引擎推送一条异常/错误消息（带顺序前缀与 ✗ 标记）。同时请求重定向。</summary>
    public void Error(string message)
    {
        _logs.Add($"{Next():00}. ✗ {message}");
        RedirectRequested = true;
    }

    /// <summary>节点/引擎推送一条警告消息（带顺序前缀与 ⚠ 标记）。同时请求重定向。</summary>
    public void Warn(string message)
    {
        _logs.Add($"{Next():00}. ⚠ {message}");
        RedirectRequested = true;
    }

    /// <summary>写入一个共享变量（key 为空则忽略）。</summary>
    public void Set(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _variables[key] = value;
    }

    /// <summary>读取一个共享变量。</summary>
    public bool TryGet(string key, out object? value) => _variables.TryGetValue(key, out value);

    /// <summary>登记节点本次运行的产物（引擎在 DriveAsync 驱动后写入），带当前 pass 戳。</summary>
    public void RegisterOutput(IWorkflowNodeViewModel node, object? value)
    {
        if (node is null) return;
        _outputs[node] = (Attempt, value);
    }

    /// <summary>清空产物登记表（每次 RunAsync 开始调用一次；重定向重跑不清空 → 由 pass 戳过滤陈旧产物）。</summary>
    public void ResetOutputs() => _outputs.Clear();

    /// <summary>
    /// 收集一组输入节点的产物为只读字典；未登记的节点不包含（TryGetValue 返回 false）。
    /// 过滤规则：仅保留「本 pass 真跑过」的产物（pass 戳 == 当前 Attempt）或「重定向目标之前的契约保留 prefix」
    /// （来源 Order &lt; ActiveRedirectTarget，resume 契约假设其结果未变）。按输入源数量预置容量，零扩容。
    /// </summary>
    public IReadOnlyDictionary<IWorkflowNodeViewModel, object?> CollectGroupedInputs(
        IEnumerable<IWorkflowNodeViewModel> inputNodes)
    {
        var capacity = inputNodes is IReadOnlyCollection<IWorkflowNodeViewModel> rc ? rc.Count : 0;
        var result = new Dictionary<IWorkflowNodeViewModel, object?>(capacity,
            WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance);
        if (inputNodes is not null)
        {
            foreach (var n in inputNodes)
                if (n is not null && _outputs.TryGetValue(n, out var entry) && IsCurrentPassOrPreserved(n, entry))
                    result[n] = entry.Value;
        }
        return new ReadOnlyDictionary<IWorkflowNodeViewModel, object?>(result);
    }

    /// <summary>
    /// 该来源的产物是否仍视为有效：本 pass 刚登记（pass 戳 == Attempt），
    /// 或来源在重定向目标之前、属于契约保留的 prefix（跳过未重驱动但其结果未变）。
    /// </summary>
    private bool IsCurrentPassOrPreserved(IWorkflowNodeViewModel source, (int Attempt, object? Value) entry)
        => entry.Attempt == Attempt
           || (ActiveRedirectTarget is int t
               && (source as ICompileTimeAware)?.CompileContext?.Order is int o && o < t);
}
