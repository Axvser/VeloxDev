namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Implemented by ContentView hosts that own a workflow surface, allowing
/// WorkflowSurfaceBehavior to resolve the tree view-model without depending
/// on BindingContext (which propagates to child views and can break ViewPool).
/// </summary>
public interface IWorkflowSurfaceHost
{
    IWorkflowTreeViewModel? WorkflowTree { get; }
}
