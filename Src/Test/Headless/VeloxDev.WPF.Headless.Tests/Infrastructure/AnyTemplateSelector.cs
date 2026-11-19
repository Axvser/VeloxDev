using System.Windows;
using System.Windows.Controls;

namespace VeloxDev.WPF.Headless.Tests.Infrastructure;

// 所有项都返回同一个极小模板。这里验的是「每个可见项一个池化视图」，不是模板内容，
// 所以模板刻意不带任何绑定。
internal sealed class AnyTemplateSelector : DataTemplateSelector
{
    private static readonly DataTemplate Template = BuildTemplate();

    public override DataTemplate SelectTemplate(object item, DependencyObject container) => Template;

    private static DataTemplate BuildTemplate()
    {
#pragma warning disable CS0618 // FrameworkElementFactory is obsolete but is still the code-only way to build a DataTemplate.
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(FrameworkElement.WidthProperty, 12d);
        factory.SetValue(FrameworkElement.HeightProperty, 12d);
        var template = new DataTemplate { VisualTree = factory };
        // FrameworkElementFactory can only instantiate a sealed template; an unsealed one throws at LoadContent.
        template.Seal();
        return template;
#pragma warning restore CS0618
    }
}
