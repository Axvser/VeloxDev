using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.ContextTree]
    public partial class FactoryViewModel
    {
        [VeloxCommand]
        public Task CreateNode()
    }
}
