using VeloxDev.Core.WorkflowSystem;
using System.Linq;

namespace AvaloniaApplication1.ViewModels;

[Workflow.Context.Node]
public partial class MainViewModel : ViewModelBase
{
    public string Greeting => "Welcome to Avalonia!";
}
