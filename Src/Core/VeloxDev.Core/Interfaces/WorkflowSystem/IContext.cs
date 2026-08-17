using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// 工作流上下文体系的根契约。所有进入节点执行/编译器机制的上下文都以此为基础，
/// 统一携带一份负载 <see cref="Data"/>——运行期上下文为真实数据，编译身份恒为 null。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "工作流上下文体系的根契约：统一携带负载 Data（可空）。派生 IAccessContext（数据流访问）、ITaskContext（数据流任务上下文）、IRuntimeContext（编译器运行时会话）、ICompileContext（编译身份）")]
[AgentContext(AgentLanguages.English, "Root contract of the workflow context hierarchy, carrying a universal payload Data (nullable). Derives IAccessContext (dataflow access), ITaskContext (dataflow task context), IRuntimeContext (compiler runtime session) and ICompileContext (compile-time identity)")]
public interface IContext
{
    /// <summary>负载数据；运行期携带真实数据，编译身份恒为 null。</summary>
    [AgentContext(AgentLanguages.Chinese, "负载数据（可空）：运行期为真实数据，编译期恒为 null")]
    [AgentContext(AgentLanguages.English, "Payload data (nullable): real data at runtime, always null at compile time")]
    object? Data { get; }
}
