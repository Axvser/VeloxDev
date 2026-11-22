using System.Drawing;
using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Resources, visual styles and visual control templates in the HWND-free backend: lookup order, implicit and
/// explicit application, template content/presenter wiring, template bindings and named template parts.
/// </summary>
[STATestClass]
public sealed class VisualResourceTemplateTests
{
    [TestMethod]
    public void Resources_ResolveNearestFirstAndConvert()
    {
        using var surface = new VisualSurface();
        var root = new VC.StackPanel();
        var middle = new VC.StackPanel();
        var text = new VC.TextBlock();
        root.Add(middle);
        middle.Add(text);
        surface.Root = root;

        surface.Resources["Accent"] = "#FF112233";
        root.Resources["Accent"] = "#FF445566";
        middle.Resources["Accent"] = "#FF778899";

        Assert.AreEqual(
            Color.FromArgb(0xFF, 0x77, 0x88, 0x99),
            VisualResourceLookup.Get<Color>(text, "Accent"),
            "the nearest ancestor dictionary wins");

        middle.Resources.Remove("Accent");
        Assert.AreEqual(
            Color.FromArgb(0xFF, 0x44, 0x55, 0x66),
            VisualResourceLookup.Get<Color>(text, "Accent"),
            "removing the nearest key falls back to the outer dictionary");

        root.Resources.Remove("Accent");
        Assert.AreEqual(
            Color.FromArgb(0xFF, 0x11, 0x22, 0x33),
            VisualResourceLookup.Get<Color>(text, "Accent"),
            "the surface dictionary is the last fallback");
    }

    [TestMethod]
    public void MissingResource_ThrowsInsteadOfReturningADefault()
    {
        var text = new VC.TextBlock();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => VisualResourceLookup.Get<Color>(text, "Missing"));
    }

    [TestMethod]
    public void TemplateBinding_TracksTheContentAfterTheTemplatedParentIsAssigned()
    {
        using var control = new VC.ContentControl { Content = "Hello" };
        var template = new VisualControlTemplate(scope =>
        {
            var border = new VC.Border();
            var text = new VC.TextBlock();
            VisualBindingOperations.Bind(text, VC.TextBlock.TextProperty, new TemplateBinding("Content"));
            border.Add(text);
            scope.Register("PART_Text", text);
            return border;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };

        control.Template = template;

        var part = control.GetTemplateChild<VC.TextBlock>("PART_Text");
        Assert.IsNotNull(part);
        Assert.AreEqual("Hello", part!.Text, "a template binding attaches before TemplatedParent exists, then re-resolves");

        control.Content = "World";
        Assert.AreEqual("World", part.Text);
    }

    [TestMethod]
    public void ContentPresenter_ReceivesTheContentVisual()
    {
        using var content = new VC.TextBlock { Text = "Body" };
        using var control = new VC.ContentControl { Content = content };
        var template = new VisualControlTemplate(_ =>
        {
            var border = new VC.Border();
            border.Add(new VC.ContentPresenter());
            return border;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };

        control.Template = template;

        var presenter = FindFirst<VC.ContentPresenter>(control.TemplateRoot!);
        Assert.IsNotNull(presenter);
        Assert.AreSame(content, presenter!.PresentedContent);
        Assert.AreSame(content, control.ContentVisual);
    }

    [TestMethod]
    public void StyleTemplate_IsAppliedRemovedAndYieldsToADirectTemplate()
    {
        var styleTemplate = new VisualControlTemplate(_ => new VC.Border())
        {
            TargetType = typeof(VC.ContentControl),
        };
        var style = new VisualStyle { TargetType = typeof(VC.ContentControl), Template = styleTemplate };
        using var control = new VC.ContentControl();

        control.Style = style;
        Assert.IsNotNull(control.TemplateRoot, "the style template becomes the control's content");

        var direct = new VisualControlTemplate(_ => new VC.StackPanel())
        {
            TargetType = typeof(VC.ContentControl),
        };
        control.Template = direct;
        Assert.IsInstanceOfType<VC.StackPanel>(control.TemplateRoot, "an explicitly assigned template wins");

        control.Template = null;
        Assert.IsInstanceOfType<VC.Border>(control.TemplateRoot, "clearing the direct template reveals the style template");

        control.Style = null;
        Assert.IsNull(control.TemplateRoot, "removing the style removes the template");
    }

    [TestMethod]
    public void TemplateTrigger_WritesTheNamedPartAtTheTemplateLayer()
    {
        using var control = new VC.ContentControl();
        var template = new VisualControlTemplate(scope =>
        {
            var border = new VC.Border();
            scope.Register("PART_Frame", border);
            return border;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };
        var trigger = new VisualTrigger(VisualElement.IsEnabledProperty, false) { TargetName = "PART_Frame" };
        trigger.Setters.Add(new VisualSetter(VisualElement.WidthProperty, 90d));
        template.Triggers.Add(trigger);

        control.Template = template;
        var frame = control.GetTemplateChild<VC.Border>("PART_Frame");
        Assert.IsNotNull(frame);
        Assert.IsTrue(double.IsNaN(frame!.Width), "the trigger is inactive while the control is enabled");

        control.IsEnabled = false;
        Assert.AreEqual(90d, frame.Width, "the template trigger targets the named part");

        control.IsEnabled = true;
        Assert.IsTrue(double.IsNaN(frame.Width), "leaving the trigger clears only the template layer");
    }

    [TestMethod]
    public void VisualXaml_CompilesStyleResourcesAndTheStyleAttribute()
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
                <Style x:Key="Fancy" TargetType="v:Button">
                  <Style.Setters>
                    <Setter Property="Width" Value="80" />
                    <Setter Property="Text" Value="OK" />
                  </Style.Setters>
                </Style>
              </v:StackPanel.Resources>
              <v:Button x:Name="action" Style="{StaticResource Fancy}" />
            </v:StackPanel>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualStyleXamlProbe",
            new InMemoryAdditionalText("ProbeVisualView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Visuals.VisualStyle()");
        StringAssert.Contains(generated, ".Resources[\"Fancy\"] =");
        StringAssert.Contains(generated, "action.Style = global::VeloxDev.WinForms.Xaml.Visuals.VisualResourceLookup");

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));
    }

    [TestMethod]
    public void VisualXaml_CompilesControlTemplateResourcesAndNamedParts()
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
                <ControlTemplate x:Key="Card" TargetType="v:ContentControl">
                  <v:Border x:Name="PART_Frame" Background="#FF252526">
                    <v:ContentPresenter />
                  </v:Border>
                </ControlTemplate>
              </v:StackPanel.Resources>
              <v:ContentControl x:Name="host" Template="{StaticResource Card}" Content="Hello" />
            </v:StackPanel>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualControlTemplateXamlProbe",
            new InMemoryAdditionalText("ProbeVisualView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "CreateVisualControlTemplate_Card");
        StringAssert.Contains(generated, "__xamlScope.Register(\"PART_Frame\"");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Visuals.VisualControlTemplate(");
        StringAssert.Contains(generated, "host.Template = global::VeloxDev.WinForms.Xaml.Visuals.VisualResourceLookup");

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
