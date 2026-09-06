namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 声明传给 <see cref="CompilerViewModel.CompileAsync"/> 的那个节点承担的角色:
/// - <see cref="Root"/>:把它当根启动器 —— 沿 Targets 编出从它向下游可达的执行图;
/// - <see cref="Terminal"/>:把它当要结果的终端 —— 沿 Sources 反向收它的祖先锥,从锥的入口前沿
///   编出"算到它为止"的图,执行后其输出即目标的最终结果(免显式启动节点)。
/// </summary>
public enum CompileRole
{
    /// <summary>节点作为根启动器:编译从它开始向下游可达的执行图。</summary>
    Root,

    /// <summary>节点作为结果终端:编译它的祖先锥,只算到该节点为止。</summary>
    Terminal,
}
