using Demo.ViewModels;

namespace Demo.Views.Workflow
{
    public class CustomTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? Controller { get; set; }
        public DataTemplate? Simulator { get; set; }

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        {
            if (item is ControllerViewModel)
            {
                return Controller ?? throw new InvalidOperationException("Controller template is not set");
            }
            else if (item is NodeViewModel)
            {
                return Simulator ?? throw new InvalidOperationException("Simulator template is not set");
            }

            throw new InvalidOperationException($"No template found for type: {item?.GetType().Name}");
        }
    }
}