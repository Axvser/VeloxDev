using System.Collections;
using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 汇合数据契约（非泛型）：多输入汇合点从 <see cref="IRuntimeContext.Data"/> 读取各上游节点的产物。
/// Key = 数据来源 Node（引用身份），Value = 该节点本次运行的产物。
/// 消费方：<c>context.Data is IGroupData g</c>，经 <see cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue"/> /
/// 索引器 / <see cref="IReadOnlyDictionary{TKey,TValue}.Keys"/> 读取；未登记的上游（分支未执行/被跳过）
/// 不在字典中，TryGetValue 返回 false。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "汇合数据：多输入节点以只读字典读取各上游产物（Key=来源 Node 引用身份，Value=该节点本次产物）")]
[AgentContext(AgentLanguages.English, "Group data: multi-input join reads each upstream node's output as a read-only dictionary (Key=source node reference, Value=its output this run)")]
public interface IGroupData : IReadOnlyDictionary<IWorkflowNodeViewModel, object?>
{
}

/// <summary>
/// 汇合数据的结构体实现：包装一个只读字典（Key=来源 Node 引用身份，Value=该节点本次产物）。
/// 引擎在驱动汇合点前构造并装箱注入 <see cref="IRuntimeContext.Data"/>。
/// </summary>
public readonly struct GroupData : IGroupData
{
    private readonly IReadOnlyDictionary<IWorkflowNodeViewModel, object?> _entries;

    public GroupData(IReadOnlyDictionary<IWorkflowNodeViewModel, object?> entries) => _entries = entries;

    /// <summary>取指定来源节点的产物；来源未登记时抛 <see cref="KeyNotFoundException"/>（用 TryGetValue 安全读取）。</summary>
    public object? this[IWorkflowNodeViewModel key] => _entries[key];

    public IEnumerable<IWorkflowNodeViewModel> Keys => _entries.Keys;
    public IEnumerable<object?> Values => _entries.Values;
    public int Count => _entries.Count;
    public bool ContainsKey(IWorkflowNodeViewModel key) => _entries.ContainsKey(key);
    public bool TryGetValue(IWorkflowNodeViewModel key, out object? value) => _entries.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<IWorkflowNodeViewModel, object?>> GetEnumerator() => _entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
