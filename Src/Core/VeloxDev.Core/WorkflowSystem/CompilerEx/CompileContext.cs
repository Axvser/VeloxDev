using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 编译期上下文：编译时为每个节点分配的编译身份。
/// 全局序号单调连续：分支下游的 Graph 起点带偏移（不归零）；-1 表示绝对停止状态（未选中分支/终止）。
/// </summary>
public sealed partial class CompileContext : ICompileContext
{
    [VeloxProperty] private int _order = -1;         // 全局计算序号；-1 = 绝对停止状态
    [VeloxProperty] private int _chainIndex = -1;    // 在所属链路内的序号（从 0 起）
    [VeloxProperty] private int _offset = 0;         // 进入本图的起点偏移（Router 下游 > 0）
}
