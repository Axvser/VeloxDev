using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 单条首尾不衔接的有序节点集合视为一个执行条目（线性段）。
/// </summary>
public sealed partial class ExecuteEntry : ActionEntry
{
    [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> _nodes = [];
}
