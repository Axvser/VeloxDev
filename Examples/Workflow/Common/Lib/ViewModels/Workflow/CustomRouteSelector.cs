using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

/// <summary>
/// A JSON-serializable <see cref="ISlotProvider"/> that drives a <c>SlotEnumerator</c>
/// with an arbitrary list of named routes instead of an enum.
/// </summary>
[AgentContext(AgentLanguages.Chinese,
    "自定义路由选择器（实现 ISlotProvider）。使用前先调用 GetTypeSchema('Demo.ViewModels.CustomRouteSelector') 了解其属性结构，" +
    "再根据 schema 构造 JSON 传给 SetEnumSlotCollection。")]
[AgentContext(AgentLanguages.English,
    "Custom route selector (implements ISlotProvider). " +
    "Call GetTypeSchema('Demo.ViewModels.CustomRouteSelector') first to inspect the property structure, " +
    "then construct the JSON and pass it to SetEnumSlotCollection.")]
public class CustomRouteSelector : ISlotProvider
{
    public List<RouteEntry> Routes { get; set; } = [];

    public IEnumerable<SlotDefinition> GetSlots()
        => Routes.Select(r => new SlotDefinition(r.Key, r.Label));
}
