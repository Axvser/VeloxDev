
namespace VeloxDev.WorkflowSystem;

/// <summary>A workflow component that carries a runtime identity.</summary>
public interface IRuntimeIdentifiable
{
    /// <summary>The runtime identifier, assigned when the component is created and stable across collection mutations.</summary>
    string RuntimeId { get; }
}
