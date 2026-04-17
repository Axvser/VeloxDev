using Demo.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Demo.Views.Workflow
{
    public class CustomTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? Controller { get; set; }
        public DataTemplate? Simulator { get; set; }
        public DataTemplate? BoolSelector { get; set; }
        public DataTemplate? EnumSelector { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item switch
            {
                ControllerViewModel => Controller ?? throw new ArgumentNullException($"Failed to find the [ {Controller} ] template"),
                BoolSelectorNodeViewModel => BoolSelector ?? throw new ArgumentNullException($"Failed to find the [ {BoolSelector} ] template"),
                EnumSelectorNodeViewModel => EnumSelector ?? throw new ArgumentNullException($"Failed to find the [ {EnumSelector} ] template"),
                NodeViewModel => Simulator ?? throw new ArgumentNullException($"Failed to find the [ {Simulator} ] template"),
                _ => throw new InvalidOperationException("Unknown Data Type")
            };
        }
    }
}
