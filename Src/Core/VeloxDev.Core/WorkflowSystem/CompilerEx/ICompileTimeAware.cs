namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 编译期注入：节点实现此接口，编译完成时拿到自己的编译身份
/// （全局序号、链路内序号、本图起点偏移；Order = -1 表示绝对停止状态）。
/// </summary>
public interface ICompileTimeAware
{
    void AttachCompileTimeContext(CompileContext context);

    /// <summary>编译期注入的编译身份（只读；Order = -1 表示绝对停止）。运行期据此跳转执行状态码。</summary>
    CompileContext? CompileContext { get; }
}
