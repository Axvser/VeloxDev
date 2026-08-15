using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 分支点：一个选择器节点 + 全部分支选项。
/// 静态分支（编译期已知 key）可剪枝未选中选项，运行期以 <see cref="CompileKey"/> 为准
/// （编译瞬间锁定的选中值，不随运行期改动变化）；动态分支（<see cref="IsDynamic"/> = true）
/// 运行期经 <see cref="ICompileTimeRouter.ResolveRouteKey"/> 重新定 key。
/// </summary>
public sealed partial class BranchEntry : ActionEntry
{
    [VeloxProperty] private IWorkflowNodeViewModel? _router;
    [VeloxProperty] private ObservableCollection<BranchOption> _options = [];
    [VeloxProperty] private bool _isDynamic;
    [VeloxProperty] private object? _compileKey;
}
