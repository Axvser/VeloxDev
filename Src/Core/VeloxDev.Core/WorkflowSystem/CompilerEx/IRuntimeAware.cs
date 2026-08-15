namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 运行期注入：节点实现此接口，编译执行引擎在驱动它之前，用函数入口把本次运行的
/// <see cref="RuntimeContext"/> 交给它，供节点记顺序、写日志、读写共享变量。
/// </summary>
public interface IRuntimeAware
{
    void AttachRuntimeContext(RuntimeContext context);
}
