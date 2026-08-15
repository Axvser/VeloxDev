using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// 工作流上下文体系的根契约。所有进入节点执行/编译器机制的上下文都以此为基础。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "工作流上下文体系的根契约，派生 ITaskContext（数据流任务上下文）、IRuntimeContext（编译器运行时会话）、ICompileContext（编译身份）")]
[AgentContext(AgentLanguages.English, "Root contract of the workflow context hierarchy. Derives ITaskContext (dataflow task context), IRuntimeContext (compiler runtime session) and ICompileContext (compile-time identity)")]
public interface IContext
{
}
