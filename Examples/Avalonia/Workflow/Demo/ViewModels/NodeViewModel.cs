using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[Workflow.Context.Node]
public partial class NodeViewModel
{
    public NodeViewModel()
    {
        // 必须执行此项
        InitializeWorkflow();
    }

    private partial Task OnExecute(object? parameter, CancellationToken ct)
    {
        throw new System.NotImplementedException();
    }
}