using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// WPF-shaped <c>ContentControl.ContentTemplate</c> behaviour on the visual backend: non-visual content is templated
/// with the content as its data context, content changes rebuild the visual, and content presenters inherit the
/// templated parent's template.
/// </summary>
[STATestClass]
public sealed class VisualContentTemplateTests
{
    [TestMethod]
    public void ContentTemplate_CreatesTheVisualWithContentAsDataContext()
    {
        using var control = new VC.ContentControl { Content = "first" };
        var created = 0;
        control.ContentTemplate = new DataTemplate(() =>
        {
            created++;
            return new VC.Border();
        });

        var visual = control.ContentVisual;
        Assert.IsNotNull(visual);
        Assert.IsInstanceOfType<VC.Border>(visual);
        Assert.AreEqual("first", visual!.DataContext, "the content is the template's data context");
        Assert.IsTrue(control.Children.Contains(visual), "the created visual is parented by the control");
        Assert.AreEqual(1, created);
    }

    [TestMethod]
    public void ContentTemplate_RebuildsWhenContentChanges()
    {
        using var control = new VC.ContentControl { Content = "first" };
        control.ContentTemplate = new DataTemplate(() => new VC.Border());
        var first = control.ContentVisual;

        control.Content = "second";

        var second = control.ContentVisual;
        Assert.AreNotSame(first, second, "new content instantiates a new template visual");
        Assert.AreEqual("second", second!.DataContext);
    }

    [TestMethod]
    public void ContentTemplate_RebuildsWhenTheTemplateChanges()
    {
        using var control = new VC.ContentControl { Content = "text" };
        control.ContentTemplate = new DataTemplate(() => new VC.Border());
        var first = control.ContentVisual;

        control.ContentTemplate = new DataTemplate(() => new VC.StackPanel());

        var second = control.ContentVisual;
        Assert.AreNotSame(first, second);
        Assert.IsInstanceOfType<VC.StackPanel>(second);
        Assert.AreEqual("text", second!.DataContext, "the new template also receives the content");
    }

    [TestMethod]
    public void VisualContent_IgnoresContentTemplate()
    {
        using var content = new VC.TextBlock { Text = "Body" };
        using var control = new VC.ContentControl { Content = content };
        var created = 0;

        control.ContentTemplate = new DataTemplate(() =>
        {
            created++;
            return new VC.Border();
        });

        Assert.AreSame(content, control.ContentVisual, "a visual content is shown as-is");
        Assert.IsTrue(control.Children.Contains(content));
        Assert.AreEqual(0, created, "the template is not instantiated for visual content");
    }

    [TestMethod]
    public void ContentPresenter_UsesTheTemplatedParentsContentTemplate()
    {
        using var control = new VC.ContentControl { Content = "Hello" };
        control.ContentTemplate = new DataTemplate(() => new VC.Border());

        control.Template = new VisualControlTemplate(_ =>
        {
            var host = new VC.Border();
            host.Add(new VC.ContentPresenter());
            return host;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };

        var presenter = FindFirst<VC.ContentPresenter>(control.TemplateRoot!);
        Assert.IsNotNull(presenter);
        var presented = presenter!.PresentedContent;
        Assert.IsNotNull(presented);
        Assert.IsInstanceOfType<VC.Border>(presented, "the parent's ContentTemplate creates the visual");
        Assert.AreEqual("Hello", presented!.DataContext);
    }

    [TestMethod]
    public void ContentPresenter_PrefersItsOwnContentTemplate()
    {
        using var control = new VC.ContentControl { Content = "Hello" };
        control.ContentTemplate = new DataTemplate(() => new VC.Border());

        VC.ContentPresenter? presenter = null;
        control.Template = new VisualControlTemplate(_ =>
        {
            var host = new VC.Border();
            presenter = new VC.ContentPresenter { ContentTemplate = new DataTemplate(() => new VC.StackPanel()) };
            host.Add(presenter);
            return host;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };

        Assert.IsNotNull(presenter);
        Assert.IsInstanceOfType<VC.StackPanel>(presenter!.PresentedContent, "the presenter's own template wins");
        Assert.AreEqual("Hello", presenter.PresentedContent!.DataContext);
    }

    [TestMethod]
    public void ContentPresenter_RebuildsWhenTheParentsTemplateChanges()
    {
        using var control = new VC.ContentControl { Content = "Hello" };
        control.ContentTemplate = new DataTemplate(() => new VC.Border());

        control.Template = new VisualControlTemplate(_ =>
        {
            var host = new VC.Border();
            host.Add(new VC.ContentPresenter());
            return host;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };

        var presenter = FindFirst<VC.ContentPresenter>(control.TemplateRoot!);
        var first = presenter!.PresentedContent;

        control.ContentTemplate = new DataTemplate(() => new VC.StackPanel());

        var second = presenter.PresentedContent;
        Assert.AreNotSame(first, second, "a new parent template rebuilds the presented visual");
        Assert.IsInstanceOfType<VC.StackPanel>(second);
        Assert.AreEqual("Hello", second!.DataContext);
    }

    [TestMethod]
    public void VisualXaml_CompilesAStaticResourceContentTemplate()
    {
        const string source = """
            namespace Probe;

            public partial class ProbeVisualView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
            }
            """;

        const string xaml = """
            <v:StackPanel
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml.Visuals.Controls;assembly=VeloxDev.WinForms"
                x:Class="Probe.ProbeVisualView">
              <v:StackPanel.Resources>
                <DataTemplate x:Key="Row">
                  <v:Border />
                </DataTemplate>
              </v:StackPanel.Resources>
              <v:ContentControl x:Name="host" Content="Hello" ContentTemplate="{StaticResource Row}" />
            </v:StackPanel>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualContentTemplateXamlProbe",
            new InMemoryAdditionalText("ProbeVisualView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        StringAssert.Contains(
            run.GeneratedText(),
            "host.ContentTemplate = global::VeloxDev.WinForms.Xaml.Visuals.VisualResourceLookup");

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));
    }

    private static T? FindFirst<T>(VisualElement root) where T : VisualElement
    {
        if (root is T match)
        {
            return match;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindFirst<T>(element) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
