using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 一次运行期的执行会话（也是那个公有携带 UID 的 VM）：
/// 上下文共享（UID / 顺序 / 日志 / 共享变量）+ 执行位置/决策状态（引擎维护、UI 绑定进度）。
/// 节点实现 <see cref="IRuntimeAware"/>，由引擎在驱动前注入本对象。
/// </summary>
public sealed partial class RuntimeContext
{
    // ── 上下文共享 ──
    [VeloxProperty] private Guid _uid = Guid.NewGuid();
    [VeloxProperty] private int _sequence = 0;
    [VeloxProperty] private ObservableCollection<string> _logs = [];

    // ── 执行位置 / 决策状态（引擎维护）──
    [VeloxProperty] private ActionEntry? _currentEntry;
    [VeloxProperty] private int _nodeIndex = -1;
    [VeloxProperty] private object? _branchKey;
    [VeloxProperty] private int _attempt;
    [VeloxProperty] private bool _isRunning;
    [VeloxProperty] private string _status = "Idle";

    /// <summary>
    /// 当前执行状态码 = 正在执行节点的**编译期固定编号**（CompileContext.Order）。
    /// 运行期只在这些固定编号之间跳跃，不重排编号。
    /// </summary>
    [VeloxProperty] private int _currentOrder = -1;

    // 共享变量（黑板）：节点/引擎/UI 都可读写，非直接 UI 绑定，走方法访问
    private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>取下一个执行顺序号（自增）。</summary>
    public int Next() => Interlocked.Increment(ref _sequence);

    /// <summary>节点/引擎推送一条普通日志（带顺序前缀）。</summary>
    public void Log(string entry) => _logs.Add($"{Next():00}. {entry}");

    /// <summary>节点/引擎推送一条异常/错误消息（带顺序前缀与 ✗ 标记）。</summary>
    public void Error(string message) => _logs.Add($"{Next():00}. ✗ {message}");

    /// <summary>写入一个共享变量（key 为空则忽略）。</summary>
    public void Set(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _variables[key] = value;
    }

    /// <summary>读取一个共享变量。</summary>
    public bool TryGet(string key, out object? value) => _variables.TryGetValue(key, out value);
}
