namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 路由器的编译模式：决定编译期 <see cref="ICompileTimeRouter.GetRouteTable"/> 返回的分支字典。
/// - <see cref="Static"/>：编译期只返回当前选中分支 → 编译图只有选中路径，顺序编译期固定。
/// - <see cref="Dynamic"/>：编译期返回全部分支 → 编译图保留全部分支，运行期经 ResolveRouteKey 定 key。
/// </summary>
public enum RouterCompileMode
{
    /// <summary>编译期只返回当前选中分支（编译图 = 单一路径）。</summary>
    Static,

    /// <summary>编译期返回全部分支（编译图 = 分支结构），运行期决定走哪条。</summary>
    Dynamic,
}
