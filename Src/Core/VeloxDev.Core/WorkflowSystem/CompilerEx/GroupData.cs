using System.Collections;
using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// The group-data contract (non-generic): a multi-input join reads each upstream node's output from
/// <see cref="IRuntimeContext.Data"/>. Key = the source Node (reference identity), Value = that node's output
/// for this run. Consumers read it via <c>context.Data is IGroupData g</c>, using
/// <see cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue"/> / the indexer /
/// <see cref="IReadOnlyDictionary{TKey,TValue}.Keys"/>; unregistered upstreams (branch not executed / skipped)
/// are absent from the dictionary and TryGetValue returns false.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "汇合数据：多输入节点以只读字典读取各上游产物（Key=来源 Node 引用身份，Value=该节点本次产物）")]
[AgentContext(AgentLanguages.English, "Group data: multi-input join reads each upstream node's output as a read-only dictionary (Key=source node reference, Value=its output this run)")]
public interface IGroupData : IReadOnlyDictionary<IWorkflowNodeViewModel, object?>
{
}

/// <summary>
/// Struct implementation of group data: wraps a read-only dictionary (Key = source Node reference identity,
/// Value = that node's output this run). The engine constructs it and boxes it into
/// <see cref="IRuntimeContext.Data"/> before driving a join point.
/// </summary>
public readonly struct GroupData : IGroupData
{
    private readonly IReadOnlyDictionary<IWorkflowNodeViewModel, object?> _entries;

    public GroupData(IReadOnlyDictionary<IWorkflowNodeViewModel, object?> entries) => _entries = entries;

    /// <summary>Gets the output of the specified source node; throws <see cref="KeyNotFoundException"/> if the source is unregistered (use TryGetValue to read safely).</summary>
    public object? this[IWorkflowNodeViewModel key] => _entries[key];

    public IEnumerable<IWorkflowNodeViewModel> Keys => _entries.Keys;
    public IEnumerable<object?> Values => _entries.Values;
    public int Count => _entries.Count;
    public bool ContainsKey(IWorkflowNodeViewModel key) => _entries.ContainsKey(key);
    public bool TryGetValue(IWorkflowNodeViewModel key, out object? value) => _entries.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<IWorkflowNodeViewModel, object?>> GetEnumerator() => _entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
