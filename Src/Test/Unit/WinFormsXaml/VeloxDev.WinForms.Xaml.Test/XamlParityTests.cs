using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Full-parity phase 1: attached property elements, lexical static/dynamic resources, per-instance template name
/// scopes, direct style setters and target names, ItemsPanel/ItemsPresenter, and the removal of the native profile.
/// </summary>
[STATestClass]
public sealed class XamlParityTests
{
    private const string VisualSource = """
        namespace Probe;

        public partial class ParityView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
        {
        }
        """;

    private const string OpenRoot = """
        <local:ParityView
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:local="clr-namespace:Probe"
            x:Class="Probe.ParityView">
        """;

    private const string CloseRoot = "</local:ParityView>";

    [TestMethod]
    public void AttachedPropertyElement_CompilesToSetValue()
    {
        const string xaml = OpenRoot + """

              <Border x:Name="card">
                <Grid.Row>1</Grid.Row>
              </Border>

            """ + CloseRoot;

        var (run, output) = Run(xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));

        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "card.SetValue(global::VeloxDev.WinForms.Xaml.Visuals.Controls.Grid.RowProperty, 1);");
    }

    [TestMethod]
    public void StaticResource_IsAnchoredOnTheTargetElement()
    {
        const string xaml = OpenRoot + """

              <local:ParityView.Resources>
                <x:Color x:Key="Accent">#FF102030</x:Color>
              </local:ParityView.Resources>

              <Border x:Name="card" Background="{StaticResource Accent}" />

            """ + CloseRoot;

        var (run, output) = Run(xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "VisualResourceLookup.ApplyStatic<");
        StringAssert.Contains(generated, "card, \"Accent\"");
        StringAssert.Contains(generated, "card.Background = __v");
    }

    [TestMethod]
    public void DynamicResource_EmitsABindingThatClearsOnRemoval()
    {
        const string xaml = OpenRoot + """

              <Border x:Name="card" Background="{DynamicResource Accent}" />

            """ + CloseRoot;

        var (run, output) = Run(xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "VisualResourceLookup.BindDynamic<");
        StringAssert.Contains(generated, "card.Background = __v");
        StringAssert.Contains(generated, "card.ClearValue(");
    }

    [TestMethod]
    public void DataTrigger_IsReportedInsteadOfSkipped()
    {
        const string xaml = OpenRoot + """

              <local:ParityView.Resources>
                <Style TargetType="Button">
                  <Style.Triggers>
                    <DataTrigger Binding="{Binding Flag}" Value="True">
                      <Setter Property="Width" Value="5" />
                    </DataTrigger>
                  </Style.Triggers>
                </Style>
              </local:ParityView.Resources>

            """ + CloseRoot;

        var result = Run(xaml).Run;
        var diagnostic = result.Diagnostics.Single(candidate => candidate.Id == "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "DataTrigger");
    }

    [TestMethod]
    public void DirectStyleSetters_Compile()
    {
        const string xaml = OpenRoot + """

              <local:ParityView.Resources>
                <Style TargetType="Button">
                  <Setter Property="Width" Value="80" />
                  <Setter Property="Height" Value="24" />
                </Style>
              </local:ParityView.Resources>

            """ + CloseRoot;

        var (run, output) = Run(xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, ".Setters.Add(new global::VeloxDev.WinForms.Xaml.Visuals.VisualSetter(");
        StringAssert.Contains(generated, "Value = 80D");
        StringAssert.Contains(generated, "Value = 24D");
    }

    [TestMethod]
    public void ItemsPanel_CompilesIntoAnItemsPanelTemplate()
    {
        const string xaml = OpenRoot + """

              <ItemsControl x:Name="list">
                <ItemsControl.ItemsPanel>
                  <ItemsPanelTemplate>
                    <WrapPanel />
                  </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
              </ItemsControl>

            """ + CloseRoot;

        var (run, output) = Run(xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "list.ItemsPanel = new global::VeloxDev.WinForms.Xaml.Visuals.Controls.ItemsPanelTemplate(");
        StringAssert.Contains(generated, "CreateVisualItemsPanel_");
    }

    [TestMethod]
    public void RemovedNativeProfile_LeavesNoLegacyCallsInGeneratedCode()
    {
        const string xaml = OpenRoot + """

              <TextBlock x:Name="caption" Text="Hi" />

            """ + CloseRoot;

        var (run, output) = Run(xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));

        var generated = run.GeneratedText();
        foreach (var legacy in new[]
                 {
                     "SetNativeBinding", "SetAttachedBinding", "ApplyStyleResource", "XamlInput",
                     "RegisterName", "Controls.Add", "Xaml.Controls.Control",
                 })
        {
            Assert.IsFalse(generated.Contains(legacy, StringComparison.Ordinal), $"generated code still mentions {legacy}");
        }
    }

    [TestMethod]
    public void StaticResource_ResolvesThroughAncestorsAfterParenting()
    {
        var root = new VC.StackPanel();
        root.Resources["Accent"] = "#FF112233";
        var middle = new VC.StackPanel();
        var child = new VC.Border();
        middle.Add(child);

        VisualResourceLookup.ApplyStatic<Brush?>(child, "Accent", value => child.Background = value);
        Assert.IsNull(child.Background, "the child is not parented into the resource chain yet");

        root.Add(middle);
        Assert.IsNotNull(child.Background, "parenting completes the chain and resolves the ancestor resource");
    }

    [TestMethod]
    public void DynamicResource_TracksAddReplaceAndRemove()
    {
        var root = new VC.StackPanel();
        var child = new VC.Border();
        root.Add(child);

        using var subscription = VisualResourceLookup.BindDynamic<double>(
            child,
            "Gap",
            value => child.Width = value,
            () => child.ClearValue(VisualElement.WidthProperty));

        Assert.IsTrue(double.IsNaN(child.Width), "a key that was never present does not write a value");

        root.Resources["Gap"] = 40d;
        Assert.AreEqual(40d, child.Width);

        root.Resources["Gap"] = 60d;
        Assert.AreEqual(60d, child.Width, "replacing the key updates the target");

        root.Resources.Remove("Gap");
        Assert.IsTrue(double.IsNaN(child.Width), "removing the key clears the value back to the default");
    }

    [TestMethod]
    public void DataTemplate_InstancesHaveTheirOwnNameScope()
    {
        var index = 0;
        var template = new DataTemplate(scope =>
        {
            var instance = ++index;
            var root = new VC.StackPanel();
            var part = new VC.TextBlock { Text = "part" + instance };
            scope.Register("PROBE", part);

            var binding = new VC.TextBlock();
            binding.SetBinding(
                VC.TextBlock.TextProperty,
                new Binding(nameof(VC.TextBlock.Text)) { ElementName = "PROBE" });

            root.Add(part);
            root.Add(binding);
            return root;
        });

        var first = template.Create();
        first.DataContext = new object();
        var second = template.Create();
        second.DataContext = new object();

        Assert.AreEqual("part1", LastTextChild(first), "the first instance resolves its own part");
        Assert.AreEqual("part2", LastTextChild(second), "the second instance must not see the first instance's part");
    }

    [TestMethod]
    public void StyleSetter_TargetNameWritesTheTemplatePart()
    {
        using var control = new VC.ContentControl();
        var template = new VisualControlTemplate(scope =>
        {
            var frame = new VC.Border();
            scope.Register("PART_Frame", frame);
            return frame;
        })
        {
            TargetType = typeof(VC.ContentControl),
        };
        control.Template = template;

        var style = new VisualStyle { TargetType = typeof(VC.ContentControl) };
        style.Setters.Add(new VisualSetter(VisualElement.WidthProperty)
        {
            TargetName = "PART_Frame",
            Value = 42d,
        });

        control.Style = style;
        var frame = control.GetTemplateChild<VC.Border>("PART_Frame");
        Assert.IsNotNull(frame);
        Assert.AreEqual(42d, frame!.Width, "the setter writes the named template part");
        Assert.IsTrue(double.IsNaN(control.Width), "the styled element itself is untouched");

        control.Style = null;
        Assert.IsTrue(double.IsNaN(frame.Width), "removing the style clears the part's style layer");
    }

    [TestMethod]
    public void ItemsControl_RealizesIntoTheItemsPanel()
    {
        using var control = new VC.ItemsControl();
        var panel = new VC.WrapPanel();
        control.ItemsPanel = new VC.ItemsPanelTemplate(() => panel);
        control.ItemsSource = new object[] { "a", "b" };

        Assert.IsTrue(control.Children.Contains(panel), "the ItemsPanel becomes a child host");
        Assert.AreEqual(2, panel.Children.Count, "realized items live in the panel, not directly on the control");
        Assert.AreEqual(2, control.Items.Count);

        control.ItemsPanel = null;
        Assert.IsFalse(control.Children.Contains(panel), "removing ItemsPanel removes its host");
        Assert.AreEqual(0, panel.Children.Count);
        Assert.AreEqual(2, control.Children.Count, "items fall back to the control's own vertical stack");
    }

    [TestMethod]
    public void ItemsPresenter_DeclaredInContentHostsTheItems()
    {
        using var control = new VC.ItemsControl();
        var presenter = new VC.ItemsPresenter();
        control.Add(presenter);
        control.ItemsSource = new object[] { "x", "y" };

        Assert.AreEqual(2, presenter.Children.Count);
        Assert.AreEqual(2, control.Items.Count);
    }

    [TestMethod]
    public void ItemsControl_ItemTemplateSelectorWinsOverItemTemplate()
    {
        using var control = new VC.ItemsControl();
        var selected = new DataTemplate(() => new VC.Border());
        control.ItemTemplate = new DataTemplate(() => new VC.TextBlock());
        control.ItemTemplateSelector = new AlwaysSelector(selected);
        control.ItemsSource = new object[] { "a" };

        Assert.IsInstanceOfType<VC.Border>(control.Items[0]);
    }

    private sealed class AlwaysSelector(DataTemplate template) : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object? item, VC.ContentControl? container) => template;
    }

    [TestMethod]
    public void XamlProperty_ForwardsLayoutMetadata()
    {
        var result = GeneratorProbe.Run(new DependencyPropertyGenerator(), """
            #nullable enable
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public partial class ParityCard : VeloxDev.WinForms.Xaml.Visuals.Controls.Border
            {
                [XamlProperty(AffectsMeasure = true, AffectsArrange = true, AffectsRender = false, Inherits = true)]
                public partial string? Title { get; set; }
            }
            """, "XamlPropertyMetadataProbe");

        Assert.IsEmpty(result.Diagnostics, GeneratorProbe.Describe(result.Diagnostics));

        var generated = result.GeneratedText();
        StringAssert.Contains(generated, "AffectsMeasure = true");
        StringAssert.Contains(generated, "AffectsArrange = true");
        StringAssert.Contains(generated, "AffectsRender = false");
        StringAssert.Contains(generated, "Inherits = true");
    }

    private static string LastTextChild(VisualElement root)
        => root.Children.OfType<VC.TextBlock>().Last().Text;

    private static (Microsoft.CodeAnalysis.GeneratorDriverRunResult Run, Compilation Output) Run(string xaml)
        => GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            VisualSource,
            "XamlParityProbe",
            new InMemoryAdditionalText("ParityView.xaml", xaml));

    private static Diagnostic[] Errors(Compilation output)
        => output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
}
