using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Generator coverage for the styleable <c>v:ScrollBar</c> and the <c>ScrollViewer</c> bar-style properties.
/// </summary>
[TestClass]
public sealed class ScrollBarXamlTests
{
    private const string Source = """
        namespace ProbeScrollBar;

        public partial class ScrollBarView : VeloxDev.WinForms.Xaml.Controls.UserControl
        {
        }
        """;

    private const string Xaml = """
        <v:UserControl
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
            x:Class="ProbeScrollBar.ScrollBarView"
            Size="400, 300">

          <v:UserControl.Resources>
            <v:ResourceDictionary>
              <v:Style x:Key="BarStyle" TargetType="v:ScrollBar">
                <v:Style.Setters>
                  <v:Setter Property="TrackBrush" Value="#22FFFFFF" />
                  <v:Setter Property="ThumbBrush" Value="#66FFFFFF" />
                  <v:Setter Property="ThumbHoverBrush" Value="#AAFFFFFF" />
                  <v:Setter Property="ThumbBorderBrush" Value="#30FFFFFF" />
                  <v:Setter Property="ThumbBorderThickness" Value="1" />
                  <v:Setter Property="ThumbCornerRadius" Value="6" />
                  <v:Setter Property="BarThickness" Value="12" />
                  <v:Setter Property="ThumbMinLength" Value="24" />
                </v:Style.Setters>
              </v:Style>
            </v:ResourceDictionary>
          </v:UserControl.Resources>

          <v:ScrollBar x:Name="bar"
                       Orientation="Vertical"
                       Maximum="109"
                       LargeChange="10"
                       Value="25" />

          <v:ScrollViewer x:Name="viewer"
                          BarThickness="12"
                          HorizontalBarStyle="{StaticResource BarStyle}"
                          VerticalBarStyle="{StaticResource BarStyle}">
            <v:Canvas x:Name="content" />
          </v:ScrollViewer>

        </v:UserControl>
        """;

    [TestMethod]
    public void VScrollBar_CompilesStyleSettersAndScrollViewerBarProperties()
    {
        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "ScrollBarProbe",
            new InMemoryAdditionalText("ScrollBarView.xaml", Xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "private global::VeloxDev.WinForms.Xaml.Controls.ScrollBar bar = null!;");
        StringAssert.Contains(
            generated,
            "bar.Orientation = global::VeloxDev.WinForms.Xaml.Controls.ScrollBarOrientation.Vertical;");
        StringAssert.Contains(generated, "bar.Maximum = 109;");
        StringAssert.Contains(generated, "bar.LargeChange = 10;");
        StringAssert.Contains(generated, "bar.Value = 25;");
        StringAssert.Contains(
            generated,
            "global::VeloxDev.WinForms.Xaml.Controls.ScrollBar.TrackBrushProperty");
        StringAssert.Contains(
            generated,
            "global::VeloxDev.WinForms.Xaml.Controls.ScrollBar.ThumbBorderThicknessProperty");
        StringAssert.Contains(
            generated,
            "viewer.BarThickness = 12;");
        StringAssert.Contains(
            generated,
            "viewer.HorizontalBarStyle = global::VeloxDev.WinForms.Xaml.XamlRuntime.GetResource<global::VeloxDev.WinForms.Xaml.Style?>(this, \"BarStyle\");");
        StringAssert.Contains(
            generated,
            "viewer.VerticalBarStyle = global::VeloxDev.WinForms.Xaml.XamlRuntime.GetResource<global::VeloxDev.WinForms.Xaml.Style?>(this, \"BarStyle\");");
    }
}
