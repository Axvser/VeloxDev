using Demo.ViewModels;
using System.Windows;
using System.Windows.Controls;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow
{
    public class CustomTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? Controller { get; set; }
        public DataTemplate? Simulator { get; set; }
        public DataTemplate? EnumSelector { get; set; }
        public DataTemplate? Timer { get; set; }
        public DataTemplate? Python { get; set; }
        public DataTemplate? Link { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item switch
            {
                ControllerViewModel => Controller ?? throw new ArgumentNullException($"Failed to find the [ {Controller} ] template"),
                EnumSelectorNodeViewModel => EnumSelector ?? throw new ArgumentNullException($"Failed to find the [ {EnumSelector} ] template"),
                TimerNodeViewModel => Timer ?? throw new ArgumentNullException($"Failed to find the [ {Timer} ] template"),
                PythonScriptNodeViewModel => Python ?? throw new ArgumentNullException($"Failed to find the [ {Python} ] template"),
                IWorkflowLinkViewModel => Link ?? throw new ArgumentNullException($"Failed to find the [ {Link} ] template"),
                _ => Simulator ?? throw new ArgumentNullException($"Failed to find the [ {Simulator} ] template")
            };
        }
    }
}
