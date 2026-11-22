using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The XAML path for the expanded control library: panels, attached coordinates, grid definitions, text content
/// and the shape namespace all resolve through unprefixed VeloxDev names.
/// </summary>
[TestClass]
public sealed class ControlLibraryXamlTests
{
    private const string Source = """
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
        {
        }
        """;

    private const string Xaml = """
        <local:ProbeView
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:local="clr-namespace:Probe"
            x:Class="Probe.ProbeView">

          <StackPanel x:Name="stack" Spacing="4">
            <Button x:Name="ok" Content="OK" />
            <TextBlock x:Name="caption">hello</TextBlock>
          </StackPanel>

          <Grid x:Name="layout">
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto" />
              <RowDefinition Height="2*" />
            </Grid.RowDefinitions>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="120" />
              <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <Border x:Name="cell" Grid.Row="1" Grid.Column="1" />
          </Grid>

          <Canvas x:Name="canvas">
            <Border x:Name="positioned" Canvas.Left="10" Canvas.Top="20" />
          </Canvas>

          <DockPanel x:Name="dock">
            <Border x:Name="docked" DockPanel.Dock="Top" Height="20" />
          </DockPanel>

          <Path x:Name="path" Data="M0,0 L10,10 Z" />
        </local:ProbeView>
        """;

    [TestMethod]
    public void ControlLibrary_CompilesThroughUnprefixedNames()
    {
        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "ControlLibraryProbe",
            new InMemoryAdditionalText("ProbeView.xaml", Xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(
            output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(output.GetDiagnostics()));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Controls.StackPanel()");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Controls.Grid()");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Controls.Shapes.Path()");
        StringAssert.Contains(generated, "ok.Content = \"OK\";");
        StringAssert.Contains(generated, "caption.Text = \"hello\";");
        StringAssert.Contains(generated, "RowDefinitions.Add(");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Controls.RowDefinition()");
        StringAssert.Contains(generated, "global::VeloxDev.WinForms.Xaml.Controls.GridLength.Parse(\"Auto\")");
        StringAssert.Contains(generated, "SetAttachedValue(cell, global::VeloxDev.WinForms.Xaml.Controls.Grid.RowProperty");
        StringAssert.Contains(generated, "SetAttachedValue(positioned, global::VeloxDev.WinForms.Xaml.Controls.Canvas.LeftProperty");
        StringAssert.Contains(generated, "SetAttachedValue(docked, global::VeloxDev.WinForms.Xaml.Controls.DockPanel.DockProperty");
    }

    [TestMethod]
    public void UnprefixedShape_ResolvesToTheShapesNamespace()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <Rectangle x:Name="shape" />
            </local:ProbeView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "UnprefixedShapeProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(
            output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(output.GetDiagnostics()));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Controls.Shapes.Rectangle shape = null!;");
    }
}
