using VeloxDev.MVVM;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 执行条目基类：所有条目共享的 UI / 结构状态。
/// 具体条目：<see cref="ExecuteEntry"/>（线性段）、<see cref="BranchEntry"/>（分支点）、<see cref="RetryEntry"/>（环路）。
/// </summary>
public abstract partial class ActionEntry
{
    [VeloxProperty] private Guid _id = Guid.NewGuid();   // 条目 UID（UI 树节点标识）
    [VeloxProperty] private int _depth = 0;              // 嵌套层级（UI 缩进）
    [VeloxProperty] private bool _isSkipped = false;     // 编译期剪除（未选中静态分支）
}
