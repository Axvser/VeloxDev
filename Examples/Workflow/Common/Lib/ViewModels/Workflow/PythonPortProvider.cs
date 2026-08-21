using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

/// <summary>
/// A JSON-serializable <see cref="ISlotProvider"/> that drives a <c>SlotEnumerator</c>
/// with an arbitrary list of named ports — the Python node's dynamic input/output slots.
/// The Agent rebuilds the ports by passing this provider's JSON to <c>SetEnumSlotCollection</c>;
/// the whole slot set is atomically replaced and existing links are rewired by position.
/// </summary>
[AgentContext(AgentLanguages.Chinese,
    "Python 节点的动态端口提供器（实现 ISlotProvider）。先用 GetTypeSchema('Demo.ViewModels.PythonPortProvider') 查看属性结构，" +
    "再构造 JSON 传给 SetEnumSlotCollection 重建输入/输出口。")]
[AgentContext(AgentLanguages.English,
    "Python node dynamic port provider (implements ISlotProvider). Call GetTypeSchema('Demo.ViewModels.PythonPortProvider') " +
    "first to inspect the property structure, then construct the JSON and pass it to SetEnumSlotCollection to rebuild the input/output ports.")]
public class PythonPortProvider : ISlotProvider
{
    public List<PythonPort> Ports { get; set; } = [];

    public IEnumerable<SlotDefinition> GetSlots()
        => Ports.Select(p => new SlotDefinition(p.Name, p.Name));
}

/// <summary>One dynamic port definition. The name is both the routing key and the field name under which the upstream value arrives in the script's input.json.</summary>
[AgentContext(AgentLanguages.Chinese,
    "单个动态端口定义，当前仅包含名称 Name。")]
[AgentContext(AgentLanguages.English,
    "One dynamic port definition; currently only carries the port Name.")]
public sealed class PythonPort
{
    public PythonPort() { }

    public PythonPort(string name) => Name = name;

    [AgentContext(AgentLanguages.Chinese, "端口名称（也是路由键，以及脚本 input.json 中该上游产物对应的字段名）")]
    [AgentContext(AgentLanguages.English, "Port name (also the routing key, and the field name under which this upstream's output arrives in the script's input.json).")]
    public string Name { get; set; } = "";
}
