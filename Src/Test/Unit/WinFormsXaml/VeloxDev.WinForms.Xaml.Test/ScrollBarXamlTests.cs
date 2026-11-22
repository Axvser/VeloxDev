using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Generator coverage for the styleable <c>ScrollBar</c> and the <c>ScrollViewer</c> bar-style properties.
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
        <UserControl
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            x:Class="ProbeScrollBar.ScrollBarView"
            Size="400, 300">

          <UserControl.Resources>
            <ResourceDictionary>
              <Style x:Key="BarStyle" TargetType="ScrollBar">
                <Style.Setters>
                  <Setter Property="TrackBrush" Value="#22FFFFFF" />
                  <Setter Property="ThumbBrush" Value="#66FFFFFF" />
                  <Setter Property="ThumbHoverBrush" Value="#AAFFFFFF" />
                  <Setter Property="ThumbBorderBrush" Value="#30FFFFFF" />
                  <Setter Property="ThumbBorderThickness" Value="1" />
                  <Setter Property="ThumbCornerRadius" Value="6" />
                  <Setter Property="BarThickness" Value="12" />
                  <Setter Property="ThumbMinLength" Value="24" />
                </Style.Setters>
              </Style>
            </ResourceDictionary>
          </UserControl.Resources>

          <ScrollBar x:Name="bar"
                       Orientation="Vertical"
                       Maximum="109"
                       LargeChange="10"
                       Value="25" />

          <ScrollViewer x:Name="viewer"
                          BarThickness="12"
                          HorizontalBarStyle="{StaticResource BarStyle}"
                          VerticalBarStyle="{StaticResource BarStyle}">
            <Canvas x:Name="content" />
          </ScrollViewer>

        </UserControl>
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
