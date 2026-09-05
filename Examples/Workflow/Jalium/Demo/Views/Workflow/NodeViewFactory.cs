using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Maps a node view-model type to its full-demo card view (the per-node-type style of the
/// other FULL demos). Falls back to the generic <see cref="NodeView"/>.</summary>
internal static class NodeViewFactory
{
    public static NodeViewBase Create(IWorkflowNodeViewModel node) => node switch
    {
        ControllerViewModel => new ControllerView(),
        TimerNodeViewModel => new TimerNodeView(),
        EnumSelectorNodeViewModel => new EnumSelectorNodeView(),
        PythonScriptNodeViewModel => new PythonNodeView(),
        _ => new NodeView(),
    };
}
