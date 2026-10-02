using VeloxDev.AI;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// The router's compile mode: decides the branch dictionary returned by
/// <see cref="ICompileTimeRouter.GetRouteTable"/> at compile-time.
/// - <see cref="Static"/>: returns only the currently selected branch at compile-time; the compiled graph contains
///   only the selected path, with order fixed at compile-time.
/// - <see cref="Dynamic"/>: returns all branches at compile-time; the compiled graph keeps every branch and the key
///   is resolved via ResolveRouteKey at runtime.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "路由器的编译模式：Static 表示编译期只返回当前选中的分支，编译图只含那条路径、顺序也在编译期定死；Dynamic 表示编译期返回全部分支，编译图保留整棵分支结构，运行期再解析走哪一条。")]
[AgentContext(AgentLanguages.English, "The router's compile mode: Static returns only the currently selected branch at compile time, so the compiled graph holds that single path with its order fixed; Dynamic returns every branch at compile time, so the compiled graph keeps the whole branch structure and the key is resolved at run time.")]
public enum RouterCompileMode
{
    /// <summary>Returns only the currently selected branch at compile-time (compiled graph = single path).</summary>
    Static,

    /// <summary>Returns all branches at compile-time (compiled graph = branch structure); runtime decides which one runs.</summary>
    Dynamic,
}
