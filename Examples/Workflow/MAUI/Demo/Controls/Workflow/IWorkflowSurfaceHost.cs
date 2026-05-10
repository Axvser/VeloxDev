using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

internal interface IWorkflowSurfaceHost
{
    void BeginConnection(IWorkflowSlotViewModel slot);
    void UpdateConnectionPointer(Anchor anchor);
    void CompleteConnection(Anchor anchor, IWorkflowSlotViewModel sourceSlot);
}
