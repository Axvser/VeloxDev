using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 编译器运行时会话上下文接口。一次运行期的执行会话（UID / 日志 / 共享变量 / 执行位置）。
/// 继承 <see cref="ITaskContext"/>：编译驱动节点时,引擎把本会话对象**作为任务上下文**
/// 直接传入 <see cref="IWorkflowNodeViewModelHelper.ReceiveAsync"/>。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "编译器运行时会话上下文（继承 ITaskContext）：UID/日志/共享变量/执行位置，编译驱动时作为任务上下文传入节点")]
[AgentContext(AgentLanguages.English, "Compiler runtime session context (inherits ITaskContext): UID/logs/shared variables/execution position, passed to nodes as the task context during compiled runs")]
public interface IRuntimeContext : ITaskContext
{
    /// <summary>本次运行会话的唯一标识。</summary>
    Guid Uid { get; set; }

    /// <summary>下一个执行顺序号（自增）。</summary>
    int Sequence { get; set; }

    /// <summary>运行期日志（带顺序前缀）。</summary>
    ObservableCollection<string> Logs { get; set; }

    /// <summary>当前执行的 ActionEntry。</summary>
    ActionEntry? CurrentEntry { get; set; }

    /// <summary>当前节点在链内索引。</summary>
    int NodeIndex { get; set; }

    /// <summary>当前分支键。</summary>
    object? BranchKey { get; set; }

    /// <summary>当前重试次数。</summary>
    int Attempt { get; set; }

    /// <summary>是否正在运行。</summary>
    bool IsRunning { get; set; }

    /// <summary>运行状态字符串（Idle/Running/Completed/Stopped）。</summary>
    string Status { get; set; }

    /// <summary>当前执行状态码 = 编译期固定编号。</summary>
    int CurrentOrder { get; set; }

    /// <summary>
    /// 链式传递结果（遮蔽 <see cref="IContext.Data"/> 增加 setter）：引擎逐节点驱动后把
    /// <see cref="IWorkflowNodeViewModelHelper.ReceiveAsync"/> 的返回值写回，供下游节点读取。
    /// 基础数据流载体 <see cref="ITaskContext"/> 保持只读，只有运行时会话可写。
    /// </summary>
    new object? Data { get; set; }

    /// <summary>节点是否在本次驱动中请求了重定向（调用了 <see cref="Error"/> 或 <see cref="Warn"/>）。引擎每次驱动前清除、驱动后检查。</summary>
    bool RedirectRequested { get; set; }

    /// <summary>流程是否因「节点报错但未实现 <see cref="IRedirectable"/>」而提前结束（状态置为 -1）。</summary>
    bool EndedWithError { get; set; }

    /// <summary>引擎请求的回退目标 Order（可为跨链）。<see cref="CompilerEngine.RunAsync"/> 读取后带该目标重跑整张图。</summary>
    int? PendingRedirectTarget { get; set; }

    /// <summary>推送一条普通日志（带顺序前缀）。</summary>
    void Log(string entry);

    /// <summary>推送一条异常/错误日志（带顺序前缀与 ✗ 标记）。同时标记「请求重定向」——由引擎依据节点是否实现 IRedirectable 决定回退或结束。</summary>
    void Error(string message);

    /// <summary>推送一条警告日志（带顺序前缀与 ⚠ 标记）。同时标记「请求重定向」——由引擎依据节点是否实现 IRedirectable 决定回退或结束。</summary>
    void Warn(string message);

    /// <summary>写入一个共享变量（key 为空则忽略）。</summary>
    void Set(string key, object? value);

    /// <summary>读取一个共享变量。</summary>
    bool TryGet(string key, out object? value);

    /// <summary>登记节点本次运行的产物（引擎在 DriveAsync 驱动后写入），供下游汇合点聚合。</summary>
    [AgentContext(AgentLanguages.Chinese, "登记节点本次运行的产物：引擎逐节点驱动后写入，多输入汇合点按输入组聚合")]
    [AgentContext(AgentLanguages.English, "Register a node's output for this run: the engine writes it after driving each node, and multi-input joins aggregate per input group")]
    void RegisterOutput(IWorkflowNodeViewModel node, object? value);

    /// <summary>清空产物登记表（每次 <see cref="CompilerEngine.RunAsync"/> 开始调用一次）。</summary>
    [AgentContext(AgentLanguages.Chinese, "清空产物登记表：每次 RunAsync 开始清空一次，重定向重跑不清空")]
    [AgentContext(AgentLanguages.English, "Clear the product registry once at the start of each RunAsync (not cleared on redirect re-runs)")]
    void ResetOutputs();

    /// <summary>
    /// 收集一组输入节点的产物为只读字典（Key=来源 Node 引用身份，Value=该节点产物）。
    /// 未登记的节点不包含——<see cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue"/> 返回 false。
    /// </summary>
    [AgentContext(AgentLanguages.Chinese, "按输入组收集产物为只读字典：未登记的来源不包含（TryGetValue 返回 false）")]
    [AgentContext(AgentLanguages.English, "Collect outputs for a group of input nodes as a read-only dictionary; unregistered sources are absent (TryGetValue returns false)")]
    IReadOnlyDictionary<IWorkflowNodeViewModel, object?> CollectGroupedInputs(IEnumerable<IWorkflowNodeViewModel> inputNodes);
}
