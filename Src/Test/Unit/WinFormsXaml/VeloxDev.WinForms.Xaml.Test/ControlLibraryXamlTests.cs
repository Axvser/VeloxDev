using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The XAML path for the expanded control library: panels, attached coordinates, grid definitions, text content
/// and the shape namespace all resolve through an explicit <c>v:</c> prefix.
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
            xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
            x:Class="Probe.ProbeView">

          <v:StackPanel x:Name="stack" Spacing="4">
            <v:Button x:Name="ok" Content="OK" />
            <v:TextBlock x:Name="caption">hello</v:TextBlock>
          </v:StackPanel>

          <v:Grid x:Name="layout">
            <v:Grid.RowDefinitions>
              <v:RowDefinition Height="Auto" />
              <v:RowDefinition Height="2*" />
            </v:Grid.RowDefinitions>
            <v:Grid.ColumnDefinitions>
              <v:ColumnDefinition Width="120" />
              <v:ColumnDefinition Width="*" />
            </v:Grid.ColumnDefinitions>
            <v:Border x:Name="cell" v:Grid.Row="1" v:Grid.Column="1" />
          </v:Grid>

          <v:Canvas x:Name="canvas">
            <v:Border x:Name="positioned" v:Canvas.Left="10" v:Canvas.Top="20" />
          </v:Canvas>

          <v:DockPanel x:Name="dock">
            <v:Border x:Name="docked" v:DockPanel.Dock="Top" Height="20" />
          </v:DockPanel>

          <v:Path x:Name="path" Data="M0,0 L10,10 Z" />
        </local:ProbeView>
        """;

    [TestMethod]
    public void ControlLibrary_CompilesThroughTheExplicitPrefix()
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
    public void UnprefixedShape_IsRejectedEvenThoughTheShapeExists()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <Rectangle />
            </local:ProbeView>
            """;

        var (run, _) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "UnprefixedShapeProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        var diagnostic = run.Diagnostics.Single(d => d.Id == "VELOX_XAML019");
        StringAssert.Contains(diagnostic.GetMessage(), "v:Rectangle");
    }
}
