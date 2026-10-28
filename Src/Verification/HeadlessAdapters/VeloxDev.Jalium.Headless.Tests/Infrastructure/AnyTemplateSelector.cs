using Jalium.UI;
using Jalium.UI.Controls;

namespace VeloxDev.Jalium.Headless.Tests.Infrastructure;

// 所有项都返回同一个极小模板。这里验的是「每个可见项一个池化视图」，不是模板内容。
internal sealed class AnyTemplateSelector : DataTemplateSelector
{
    private readonly DataTemplate _template;

    public AnyTemplateSelector()
    {
        // 不给 DataType：Jalium 的 TemplateKey 拒绝 object，而选择器本来就对所有项返回同一个模板。
        _template = new DataTemplate();
        _template.SetVisualTree(() => new Border { Width = 12d, Height = 12d });
    }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) => _template;
}
