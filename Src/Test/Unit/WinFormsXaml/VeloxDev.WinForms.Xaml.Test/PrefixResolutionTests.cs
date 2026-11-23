using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Element type resolution is prefix-strict and visual-only: an unprefixed name resolves to the visual backend's
/// controls, shapes, helpers and shared primitives; <c>local:</c> means the template namespace; an explicit
/// <c>clr-namespace:</c> wins as written; and a legacy <c>v:</c> prefix still means the visual adapter.
/// </summary>
[TestClass]
public sealed class PrefixResolutionTests
{
    private const string Source = """
        namespace Probe;

        public partial class ProbeCard : VeloxDev.WinForms.Xaml.Visuals.Controls.Border
        {
        }

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
        {
        }
        """;

    private const string VisualCanvasSource = """
        namespace Probe;

        public partial class ProbeVisualView : VeloxDev.WinForms.Xaml.Visuals.Controls.Canvas
        {
        }
        """;

    private const string VisualStackPanelSource = """
        namespace Probe;

        public partial class ProbeVisualView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
        {
        }
        """;

    [TestMethod]
    public void UnprefixedVisualRootAndControl_ResolveToVisualTypes()
    {
        const string xaml = """
            <StackPanel
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="Probe.ProbeView">
              <Border x:Name="frame" />
            </StackPanel>
            """;

        var (run, output) = Run("PrefixUnprefixedVisual", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.Border frame = null!;");
    }

    [TestMethod]
    public void UnprefixedShape_ResolvesThroughTheVisualShapesNamespace()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <Rectangle x:Name="shape" Width="40" Height="20" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixUnprefixedShape", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.Shapes.Rectangle shape = null!;");
    }

    [TestMethod]
    public void UnknownUnprefixedType_IsXaml019()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <MissingThing />
            </local:ProbeView>
            """;

        var (run, _) = Run("PrefixUnknownUnprefixed", Source, xaml);

        var diagnostic = AssertSingle(run, "VELOX_XAML019");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "local:MissingThing");
    }

    [TestMethod]
    public void ExplicitLocalPrefix_ResolvesTheTemplateType()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <local:ProbeCard x:Name="localCard" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixExplicitLocal", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::Probe.ProbeCard localCard = null!;");
    }

    [TestMethod]
    public void LegacyVPrefix_StillResolvesAVisualControl()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <v:StackPanel x:Name="stack" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixLegacyV", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel stack = null!;");
    }

    [TestMethod]
    public void UnprefixedStyleTargetType_IsAVisualControl()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <local:ProbeView.Resources>
                <Style x:Key="ButtonStyle" TargetType="Button">
                  <Style.Setters>
                    <Setter Property="Width" Value="80" />
                  </Style.Setters>
                </Style>
              </local:ProbeView.Resources>
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixVisualStyleTarget", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "TargetType = typeof(global::VeloxDev.WinForms.Xaml.Visuals.Controls.Button);");
    }

    [TestMethod]
    public void NativePrefix_IsRejectedOnAVisualRoot()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                xmlns:native="clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms"
                x:Class="Probe.ProbeView">
              <native:Label Text="OK" />
            </local:ProbeView>
            """;

        var run = GeneratorProbe.Run(
            new XamlFileGenerator(),
            Source,
            "PrefixNative",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        var diagnostic = AssertSingle(run, "VELOX_XAML004");
        StringAssert.Contains(diagnostic.GetMessage(), "native WinForms type");
    }

    [TestMethod]
    public void VisualRoot_UnprefixedNamesResolveToVisualTypes()
    {
        const string xaml = """
            <Canvas
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="Probe.ProbeVisualView">
              <UserControl x:Name="content" />
              <Canvas x:Name="nested" />
              <ItemsControl x:Name="list">
                <ItemsControl.ItemTemplate>
                  <DataTemplate>
                    <TextBlock Text="row" />
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
            </Canvas>
            """;

        var (run, output) = Run("VisualPrefixUnprefixed", VisualCanvasSource, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl content = null!;");
        StringAssert.Contains(
            generated,
            "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.Canvas nested = null!;");
        StringAssert.Contains(
            generated,
            "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.ItemsControl list = null!;");
        StringAssert.Contains(
            generated,
            "list.ItemTemplate = new global::VeloxDev.WinForms.Xaml.Visuals.DataTemplate(this.CreateVisualItemTemplate_0);");
        StringAssert.Contains(
            generated,
            "private global::VeloxDev.WinForms.Xaml.Visuals.VisualElement CreateVisualItemTemplate_0()");
    }

    [TestMethod]
    public void VisualRoot_SharedTrackDefinitionsFallBackToTheSharedPrimitives()
    {
        const string xaml = """
            <Canvas
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="Probe.ProbeVisualView">
              <Grid x:Name="layout">
                <Grid.RowDefinitions>
                  <RowDefinition Height="Auto" />
                  <RowDefinition Height="2*" />
                </Grid.RowDefinitions>
              </Grid>
            </Canvas>
            """;

        var (run, output) = Run("VisualPrefixSharedTracks", VisualCanvasSource, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.Grid layout = null!;");
        StringAssert.Contains(
            generated,
            "new global::VeloxDev.WinForms.Xaml.Controls.RowDefinition()");
    }

    [TestMethod]
    public void VisualRoot_ExplicitClrNamespaceStillWins()
    {
        const string xaml = """
            <visuals:StackPanel
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:visuals="clr-namespace:VeloxDev.WinForms.Xaml.Visuals.Controls;assembly=VeloxDev.WinForms"
                xmlns:helpers="clr-namespace:VeloxDev.WinForms.Xaml.Visuals;assembly=VeloxDev.WinForms"
                x:Class="Probe.ProbeVisualView">
              <visuals:Border x:Name="frame" />
              <visuals:ItemsControl x:Name="list">
                <visuals:ItemsControl.ItemTemplate>
                  <helpers:DataTemplate>
                    <visuals:TextBlock Text="row" />
                  </helpers:DataTemplate>
                </visuals:ItemsControl.ItemTemplate>
              </visuals:ItemsControl>
            </visuals:StackPanel>
            """;

        var (run, output) = Run("VisualPrefixExplicit", VisualStackPanelSource, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.Border frame = null!;");
        StringAssert.Contains(
            generated,
            "list.ItemTemplate = new global::VeloxDev.WinForms.Xaml.Visuals.DataTemplate(this.CreateVisualItemTemplate_0);");
    }

    private static (Microsoft.CodeAnalysis.GeneratorDriverRunResult Run, Compilation Output) Run(
        string assemblyName,
        string source,
        string xaml)
        => GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            assemblyName,
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

    private static Diagnostic[] Errors(Compilation compilation)
        => compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

    private static Diagnostic AssertSingle(GeneratorDriverRunResult result, string id)
    {
        var matches = result.Diagnostics.Where(diagnostic => diagnostic.Id == id).ToArray();
        Assert.HasCount(1, matches, GeneratorProbe.Describe(result.Diagnostics));
        return matches[0];
    }
}
