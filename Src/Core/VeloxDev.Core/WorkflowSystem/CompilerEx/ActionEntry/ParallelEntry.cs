using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 扇出组：一个分支条件投递给多个下游时，把所有子图包成一组。
/// 顺序执行各分支即满足"等待所有上游到达"的汇聚语义（共享 RuntimeContext 黑板非线程安全，不做真并行）。
/// </summary>
public sealed partial class ParallelEntry : ActionEntry
{
    /// <summary>各扇出子图（顺序执行）。</summary>
    [VeloxProperty] private ObservableCollection<CompiledGraph> _branches = [];
}
