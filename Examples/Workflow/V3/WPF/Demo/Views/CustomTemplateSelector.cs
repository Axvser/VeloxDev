using Demo.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Demo.Views
{
    public class CustomTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? Controller { get; set; }
        public DataTemplate? Simulator { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item switch
            {
                ControllerViewModel => Controller ?? throw new ArgumentNullException($"Failed to find the [ {Controller} ] template"),
                NodeViewModel => Simulator ?? throw new ArgumentNullException($"Failed to find the [ {Simulator} ] template"),
                _ => throw new InvalidOperationException("Unknown Data Type")
            };
        }
    }
}
