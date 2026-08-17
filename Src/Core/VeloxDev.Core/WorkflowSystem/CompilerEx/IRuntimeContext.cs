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
}
