using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public sealed class CustomTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ControllerTemplate { get; set; }
    public DataTemplate? NodeTemplate { get; set; }
    public DataTemplate? EnumSelectorTemplate { get; set; }
    public DataTemplate? PythonTemplate { get; set; }
    public DataTemplate? TimerTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item switch
        {
            ControllerViewModel => ControllerTemplate ?? throw new InvalidOperationException("ControllerTemplate is not set."),
            EnumSelectorNodeViewModel => EnumSelectorTemplate ?? throw new InvalidOperationException("EnumSelectorTemplate is not set."),
            // New FULL-demo nodes without a dedicated view here → fall back to the generic node card.
            TimerNodeViewModel => TimerTemplate ?? throw new InvalidOperationException("TimerTemplate is not set."),
            PythonScriptNodeViewModel => PythonTemplate ?? throw new InvalidOperationException("PythonTemplate is not set."),
            // Any other workflow node type falls back to the generic node card.
            IWorkflowNodeViewModel => NodeTemplate ?? throw new InvalidOperationException("NodeTemplate is not set."),
            // 连线不进池：由共享链接层绘制。适配器的 ViewManager 会按选择器把连线筛掉，
            // 走到这里说明池被喂了未过滤的集合 —— 契约被破坏。
            IWorkflowLinkViewModel => throw new InvalidOperationException("LinkViewModels must not be pooled; the shared link layer renders links."),
            _ => throw new InvalidOperationException($"Unknown data type: {item?.GetType().Name}")
        };
}
