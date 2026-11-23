using System.Globalization;
using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// WPF-shaped content selection on the visual backend: a <see cref="DataTemplateSelector"/> picks the template before a
/// static <see cref="DataTemplate"/>, content presenters resolve their own selector before the templated parent's, and
/// <c>ContentStringFormat</c> shapes the text projection.
/// </summary>
[STATestClass]
public sealed class VisualContentSelectorTests
{
    [TestMethod]
    public void Selector_PicksTheTemplateForTheContentAndPassesTheControlAsContainer()
    {
        using var control = new VC.ContentControl { Content = 42 };
        var forNumbers = new DataTemplate(() => new VC.Border());
        var forText = new DataTemplate(() => new VC.StackPanel());
        var selector = new ProbeSelector((item, _) => item is int ? forNumbers : forText);

        control.ContentTemplateSelector = selector;

        Assert.AreEqual(42, selector.LastItem);
        Assert.AreSame(control, selector.LastContainer, "the ContentControl is the selector's container");
        Assert.IsInstanceOfType<VC.Border>(control.ContentVisual);
        Assert.AreEqual(42, control.ContentVisual!.DataContext);

        control.Content = "text";

        Assert.IsInstanceOfType<VC.StackPanel>(control.ContentVisual, "new content selects another template");
        Assert.AreEqual("text", control.ContentVisual!.DataContext);
    }

    [TestMethod]
    public void Selector_WinsOverContentTemplateAndFallsBackWhenItReturnsNull()
    {
        using var control = new VC.ContentControl { Content = "Hello" };
        control.ContentTemplate = new DataTemplate(() => new VC.Border());

        control.ContentTemplateSelector = new ProbeSelector((_, _) => new DataTemplate(() => new VC.StackPanel()));
        Assert.IsInstanceOfType<VC.StackPanel>(control.ContentVisual, "a selector result wins over ContentTemplate");

        control.ContentTemplateSelector = new ProbeSelector((_, _) => null);
        Assert.IsInstanceOfType<VC.Border>(control.ContentVisual, "a null selector result falls back to ContentTemplate");
    }

    [TestMethod]
    public void ContentPresenter_PrefersItsOwnSelectorOverTheTemplatedParents()
    {
        using var control = new VC.ContentControl { Content = "Hello" };
        var parentSelector = new ProbeSelector((_, _) => new DataTemplate(() => new VC.Border()));
        var ownSelector = new ProbeSelector((_, _) => new DataTemplate(() => new VC.StackPanel()));
        control.ContentTemplateSelector = parentSelector;

        VC.ContentPresenter? presenter = null;
        control.Template = new VisualControlTemplate(_ =>
        {
            var host = new VC.Border();
            presenter = new VC.ContentPresenter { ContentTemplateSelector = ownSelector };
            host.Add(presenter);
            return host;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };

        Assert.IsNotNull(presenter);
        Assert.IsInstanceOfType<VC.StackPanel>(presenter!.PresentedContent);
        Assert.AreSame(control, ownSelector.LastContainer, "the owning ContentControl is passed as the container");
        Assert.AreEqual("Hello", presenter!.PresentedContent!.DataContext);
    }

    [TestMethod]
    public void ContentPresenter_UsesTheTemplatedParentsSelector()
    {
        using var control = new VC.ContentControl { Content = "Hello" };
        var parentSelector = new ProbeSelector((_, _) => new DataTemplate(() => new VC.Border()));
        control.ContentTemplateSelector = parentSelector;

        VC.ContentPresenter? presenter = null;
        control.Template = new VisualControlTemplate(_ =>
        {
            var host = new VC.Border();
            presenter = new VC.ContentPresenter();
            host.Add(presenter);
            return host;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };

        Assert.IsNotNull(presenter);
        Assert.IsInstanceOfType<VC.Border>(presenter!.PresentedContent);
        Assert.AreSame(control, parentSelector.LastContainer);
    }

    [TestMethod]
    public void ChangingTheSelectorRebuildsTheVisual()
    {
        using var control = new VC.ContentControl { Content = "text" };
        control.ContentTemplateSelector = new ProbeSelector((_, _) => new DataTemplate(() => new VC.Border()));
        var first = control.ContentVisual;

        control.ContentTemplateSelector = new ProbeSelector((_, _) => new DataTemplate(() => new VC.StackPanel()));

        var second = control.ContentVisual;
        Assert.AreNotSame(first, second);
        Assert.IsInstanceOfType<VC.StackPanel>(second);
    }

    [TestMethod]
    public void ChangingThePresentersSelectorRebuildsItsVisual()
    {
        using var control = new VC.ContentControl { Content = "Hello" };
        VC.ContentPresenter? presenter = null;
        control.Template = new VisualControlTemplate(_ =>
        {
            var host = new VC.Border();
            presenter = new VC.ContentPresenter
            {
                ContentTemplateSelector = new ProbeSelector((_, _) => new DataTemplate(() => new VC.Border())),
            };
            host.Add(presenter);
            return host;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };

        var first = presenter!.PresentedContent;
        presenter!.ContentTemplateSelector = new ProbeSelector((_, _) => new DataTemplate(() => new VC.StackPanel()));

        var second = presenter!.PresentedContent;
        Assert.AreNotSame(first, second);
        Assert.IsInstanceOfType<VC.StackPanel>(second);
    }

    [TestMethod]
    public void ContentPresenter_DoesNotInstantiateATemplateBeforeContentIsPushed()
    {
        var created = 0;
        using var presenter = new VC.ContentPresenter
        {
            ContentTemplate = new DataTemplate(() =>
            {
                created++;
                return new VC.Border();
            }),
        };

        Assert.AreEqual(0, created, "no content has been pushed yet");
        Assert.IsNull(presenter.PresentedContent);

        presenter.Content = "Hello";

        Assert.AreEqual(1, created);
        Assert.AreEqual("Hello", presenter.PresentedContent!.DataContext);
    }

    [TestMethod]
    public void ContentStringFormat_FormatsTheTextProjection()
    {
        using var control = new VC.ContentControl { Content = 1234.5, ContentStringFormat = "N1" };

        var text = control.ContentVisual as VC.TextBlock;
        Assert.IsNotNull(text);
        Assert.AreEqual(
            string.Format(CultureInfo.CurrentCulture, "N1", 1234.5),
            text!.Text);
    }

    [TestMethod]
    public void ContentWithoutAFormat_UsesToString()
    {
        using var control = new VC.ContentControl { Content = 42 };

        var text = control.ContentVisual as VC.TextBlock;
        Assert.IsNotNull(text);
        Assert.AreEqual("42", text!.Text);
    }

    [TestMethod]
    public void ContentStringFormat_IsIgnoredWhenATemplateSuppliesTheVisual()
    {
        using var control = new VC.ContentControl
        {
            Content = "Hello",
            ContentStringFormat = "X{0}",
            ContentTemplate = new DataTemplate(() => new VC.Border()),
        };

        Assert.IsInstanceOfType<VC.Border>(control.ContentVisual);
    }

    [TestMethod]
    public void ContentPresenter_AppliesTheControlStringFormatToItsTextFallback()
    {
        using var control = new VC.ContentControl { Content = "Hello", ContentStringFormat = "[{0}]" };
        var presenter = AttachPresenter(control);

        var text = presenter.PresentedContent as VC.TextBlock;
        Assert.IsNotNull(text);
        Assert.AreEqual("[Hello]", text!.Text);
    }

    [TestMethod]
    public void ContentPresenterOwnStringFormatOverridesTheControls()
    {
        using var control = new VC.ContentControl { Content = "Hello", ContentStringFormat = "[{0}]" };
        var presenter = AttachPresenter(control, new VC.ContentPresenter { ContentStringFormat = "({0})" });

        var text = presenter.PresentedContent as VC.TextBlock;
        Assert.IsNotNull(text);
        Assert.AreEqual("(Hello)", text!.Text);
    }

    [TestMethod]
    public void VisualContent_BypassesTheSelectorAndTheFormat()
    {
        using var body = new VC.TextBlock { Text = "Body" };
        using var control = new VC.ContentControl { Content = body, ContentStringFormat = "[{0}]" };
        var selector = new ProbeSelector((_, _) => new DataTemplate(() => new VC.Border()));

        control.ContentTemplateSelector = selector;

        Assert.AreSame(body, control.ContentVisual, "a visual content is shown as-is");
        Assert.AreEqual(0, selector.Calls, "a visual content does not consult the selector");
        Assert.AreEqual("Body", body.Text, "the format is not applied to a visual content");
    }

    [TestMethod]
    public void VisualContent_BypassesThePresentersSelectorAndFormat()
    {
        using var body = new VC.TextBlock { Text = "Body" };
        using var control = new VC.ContentControl { Content = body };
        var selector = new ProbeSelector((_, _) => new DataTemplate(() => new VC.Border()));
        var presenter = AttachPresenter(
            control,
            new VC.ContentPresenter { ContentTemplateSelector = selector, ContentStringFormat = "[{0}]" });

        Assert.AreSame(body, presenter.PresentedContent);
        Assert.AreEqual(0, selector.Calls);
        Assert.AreEqual("Body", body.Text);
    }

    [TestMethod]
    public void VisualXaml_CompilesAStaticResourceTemplateSelector()
    {
        const string source = """
            namespace Probe;

            public sealed class ProbeSelector : VeloxDev.WinForms.Xaml.Visuals.DataTemplateSelector
            {
                public override VeloxDev.WinForms.Xaml.Visuals.DataTemplate SelectTemplate(
                    object item,
                    VeloxDev.WinForms.Xaml.Visuals.Controls.ContentControl container)
                    => null;
            }

            public partial class ProbeVisualView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
            }
            """;

        const string xaml = """
            <v:StackPanel
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml.Visuals.Controls;assembly=VeloxDev.WinForms"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeVisualView">
              <v:StackPanel.Resources>
                <local:ProbeSelector x:Key="Picker" />
              </v:StackPanel.Resources>
              <v:ContentControl x:Name="host" Content="Hello" ContentTemplateSelector="{StaticResource Picker}" />
            </v:StackPanel>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualContentSelectorXamlProbe",
            new InMemoryAdditionalText("ProbeVisualView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        StringAssert.Contains(run.GeneratedText(), "ProbeSelector");
        StringAssert.Contains(run.GeneratedText(), "DataTemplateSelector");

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));
    }

    // 把 presenter 放进控件模板，返回该 presenter，省去每个用例重复模板装配。
    private static VC.ContentPresenter AttachPresenter(VC.ContentControl control, VC.ContentPresenter? presenter = null)
    {
        control.Template = new VisualControlTemplate(_ =>
        {
            var host = new VC.Border();
            presenter ??= new VC.ContentPresenter();
            var target = presenter;
            host.Add(target);
            return host;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };

        return presenter!;
    }

    private sealed class ProbeSelector(Func<object?, VC.ContentControl?, DataTemplate?> choose) : DataTemplateSelector
    {
        private int _calls;

        public object? LastItem { get; private set; }

        public VC.ContentControl? LastContainer { get; private set; }

        public int Calls => _calls;

        public override DataTemplate? SelectTemplate(object? item, VC.ContentControl? container)
        {
            _calls++;
            LastItem = item;
            LastContainer = container;
            return choose(item, container);
        }
    }
}
