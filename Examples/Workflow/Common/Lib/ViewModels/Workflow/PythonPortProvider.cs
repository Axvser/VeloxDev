using VeloxDev.AI;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

/// <summary>
/// A JSON-serializable <see cref="ISlotProvider"/> that drives a <c>SlotEnumerator</c>
/// with an arbitrary list of named ports — the Python node's dynamic input/output slots.
/// The Agent rebuilds the ports by passing this provider's JSON to <c>SetEnumSlotCollection</c>;
/// the whole slot set is atomically replaced and existing links are rewired by position.
/// </summary>
/// <remarks>
/// <see cref="ArchivableAttribute"/> is what makes the Agent's route work at all. Neither this type nor
/// <see cref="PythonPort"/> is a workflow component or a <c>[VeloxProperty]</c> holder, so without it the
/// archive generator compiles no reader for them and <c>SetEnumSlotCollection</c> — which reads the
/// provider back through the generated serializer — would fail with "no registered JSON reader".
/// A <c>[SlotSelectors]</c> provider in a consuming project needs the same annotation.
/// </remarks>
[Archivable(typeof(PythonPort))]
public class PythonPortProvider : ISlotProvider
{
    public List<PythonPort> Ports { get; set; } = [];

    /// <inheritdoc/>
    public IEnumerable<SlotDefinition> GetSlots()
        => Ports.Select(p => new SlotDefinition(p.Name, p.Name));
}

/// <summary>One dynamic port definition. The name is both the routing key and the field name under which the upstream value arrives in the script's input.json.</summary>
public sealed class PythonPort
{
    public PythonPort() { }

    public PythonPort(string name) => Name = name;

    public string Name { get; set; } = "";
}
