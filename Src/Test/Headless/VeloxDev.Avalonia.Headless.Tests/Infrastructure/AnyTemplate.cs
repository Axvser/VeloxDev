using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace VeloxDev.Avalonia.Headless.Tests.Infrastructure;

// 所有项都返回同一个极小视图。这里验的是「每个可见项一个池化视图」，不是模板内容。
internal sealed class AnyTemplate : IDataTemplate
{
    public Control Build(object? param) => new Border { Width = 12d, Height = 12d };

    public bool Match(object? data) => true;
}
