using System.Windows;
using System.Windows.Controls;

namespace VeloxDev.WPF.WorkflowSystem.Views
{
    public class TemplateSelector : DataTemplateSelector
    {
        public DataTemplate StudentTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return base.SelectTemplate(item, container);
        }
    }
}
