using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 多条执行条目的有序集合视为一个编译图。
/// 可嵌套：BranchEntry / RetryEntry 内部各持一个子 <see cref="CompiledGraph"/>。
/// 编译产物不可变——只描述执行的可能集合，实际路径由运行期状态驱动。
/// </summary>
public sealed partial class CompiledGraph
{
    [VeloxProperty] private ObservableCollection<ActionEntry> _entries = [];
}
