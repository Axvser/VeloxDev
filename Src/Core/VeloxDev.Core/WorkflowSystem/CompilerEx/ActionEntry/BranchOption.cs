using VeloxDev.MVVM;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 一个分支选项：key + 标签 + 下游子图。
/// 动态分支全部分支存活（运行期按 key 选择）。
/// <see cref="IsTerminal"/>：分支登记了路由 key 但无下游节点——运行时选中该分支即结束整个运行
/// （不透传汇合点尾段）。
/// </summary>
public sealed partial class BranchOption
{
    [VeloxProperty] private object? _key;
    [VeloxProperty] private string? _label;
    [VeloxProperty] private CompiledGraph? _graph;
    [VeloxProperty] private bool _isTerminal;
}
