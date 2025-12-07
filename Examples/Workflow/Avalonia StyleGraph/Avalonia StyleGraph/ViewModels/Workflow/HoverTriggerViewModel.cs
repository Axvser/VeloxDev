using Avalonia_StyleGraph.ViewModels.Workflow.Helper;
using System.Linq;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph.ViewModels.Workflow;

[WorkflowBuilder.ViewModel.Node
    <TriggerHelper>
    (workSemaphore: 1)]
public partial class HoverTriggerViewModel
{
    public HoverTriggerViewModel() => InitializeWorkflow();

    // …… 自由扩展您的节点视图模型

    [VeloxProperty] private bool pointerHoverd = false;
    [VeloxProperty] private SlotChannel channel = SlotChannel.OneSource | SlotChannel.OneTarget;

    partial void OnPointerHoverdChanged(bool oldValue, bool newValue)
    {
        var styler = Slots
            .SelectMany(s => s.Targets)                     // 扁平化所有 Targets
            .OfType<IWorkflowSlotViewModel>()               // 只取 IWorkflowSlotViewModel
            .Select(t => t.Parent)                          // 获取 Parent
            .OfType<HoverStyleViewModel>()                  // 只取 HoverStyleViewModel
            .FirstOrDefault();                              // 找第一个

        styler?.Update();
    }
}