using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 节点自报「上游数据 → 广播目标」的路由契约：
/// - 编译期 <see cref="GetRouteTable"/> 声明分支结构（key → 下游节点），编译器据此生成 BranchEntry；
/// - 运行期 <see cref="ResolveRouteKey"/> 给定当前数据负载，返回 key → 决定广播给哪个分支。
/// 静态选择器（预设 key）忽略 payload；动态路由从 payload 计算 key，编译期以 null 调用时返回 null
/// （编译期无法决定 → 分支全部存活）。
/// </summary>
public interface ICompileTimeRouter
{
    /// <summary>分支表：key → 下游节点。一个分支可扇出到多个目标。</summary>
    Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable();

    /// <summary>给定当前数据负载（运行期为 <see cref="IRuntimeContext"/> 实例，编译期为 null），返回路由 key。</summary>
    Task<object?> ResolveRouteKey(object? payload);
}
