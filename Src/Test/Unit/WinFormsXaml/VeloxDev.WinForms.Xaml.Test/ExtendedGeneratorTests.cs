using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The extended markup subset on a visual root: nested markup extensions, name/relative sources, template bindings,
/// visual style triggers, attached properties, shapes and an <c>ItemsControl.ItemTemplate</c>.
/// </summary>
[TestClass]
public sealed class ExtendedGeneratorTests
{
    private const string Source = """
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public static class ProbeStatics
        {
            public static string Value => "static";
        }

        public partial class ProbeCard : VeloxDev.WinForms.Xaml.Visuals.Controls.Border
        {
            public static readonly DependencyProperty TitleProperty =
                DependencyProperty.Register(nameof(Title), typeof(string), typeof(ProbeCard), new PropertyMetadata(null));
            public static readonly DependencyProperty CaptionProperty =
                DependencyProperty.Register(nameof(Caption), typeof(string), typeof(ProbeCard), new PropertyMetadata(null));
            public static readonly DependencyProperty IsHotProperty =
                DependencyProperty.Register(nameof(IsHot), typeof(bool), typeof(ProbeCard));
            public static readonly DependencyProperty GlowProperty =
                DependencyProperty.Register(nameof(Glow), typeof(double), typeof(ProbeCard));

            public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
            public string? Caption { get => (string?)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
            public bool IsHot { get => (bool)GetValue(IsHotProperty)!; set => SetValue(IsHotProperty, value); }
            public double Glow { get => (double)GetValue(GlowProperty)!; set => SetValue(GlowProperty, value); }
        }

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
        {
        }

        public sealed class ProbeConverter : IValueConverter
        {
            public static ProbeConverter Instance { get; } = new();

            public object? Convert(object? value, System.Type targetType) => value;
            public object? ConvertBack(object? value, System.Type sourceType) => value;
        }
        """;

    private const string Xaml = """
        <local:ProbeView
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:local="clr-namespace:Probe"
            x:Class="Probe.ProbeView">

          <local:ProbeView.Resources>
            <ControlTemplate x:Key="CardTemplate" TargetType="local:ProbeCard">
              <Border>
                <TextBlock Text="{TemplateBinding Title}" />
              </Border>
            </ControlTemplate>
            <Style x:Key="HotStyle" TargetType="local:ProbeCard">
              <Style.Triggers>
                <Trigger Property="IsHot" Value="True">
                  <Trigger.Setters>
                    <Setter Property="Glow" Value="1" />
                  </Trigger.Setters>
                </Trigger>
              </Style.Triggers>
            </Style>
          </local:ProbeView.Resources>

          <local:ProbeCard x:Name="card"
                           Title="{Binding Caption, Converter={x:Static local:ProbeConverter.Instance}, StringFormat=N2, FallbackValue=fallback, TargetNullValue=none, UpdateSourceTrigger=LostFocus}"
                           IsHot="True"
                           Style="{StaticResource HotStyle}"
                           Grid.Row="5" />

          <TextBlock Text="{Binding Caption, RelativeSource={RelativeSource Self}}" />
          <TextBlock Text="{Binding Caption, RelativeSource={RelativeSource FindAncestor, AncestorType={x:Type local:ProbeCard}}}" />
          <TextBlock Text="{x:Static local:ProbeStatics.Value}" />

          <Rectangle x:Name="shape" Width="40" Height="20" />

          <ItemsControl x:Name="list">
            <ItemsControl.ItemTemplate>
              <DataTemplate>
                <TextBlock Text="row" />
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>

          <ContentControl x:Name="host" Template="{StaticResource CardTemplate}" Content="Hello" />

        </local:ProbeView>
        """;

    [TestMethod]
    public void AnEscapedStringFormat_DropsTheEscapePrefix()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <TextBlock Text="{Binding Amount, StringFormat='{}{0:N2}'}" />
            </local:ProbeView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "EscapedStringFormatProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        StringAssert.Contains(run.GeneratedText(), "StringFormat = \"{0:N2}\"");
    }

    [TestMethod]
    public void ExtendedMarkup_CompilesAndEmitsTheNewBindingAndTriggerShape()
    {
        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "ExtendedGeneratorProbe",
            new InMemoryAdditionalText("ProbeView.xaml", Xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "StringFormat = \"N2\"");
        StringAssert.Contains(generated, "FallbackValue = \"fallback\"");
        StringAssert.Contains(generated, "TargetNullValue = \"none\"");
        StringAssert.Contains(generated, "UpdateSourceTrigger = global::VeloxDev.WinForms.Xaml.UpdateSourceTrigger.LostFocus");
        StringAssert.Contains(generated, "Converter = (global::VeloxDev.WinForms.Xaml.IValueConverter)");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.TemplateBinding(\"Title\")");
        StringAssert.Contains(generated, "global::VeloxDev.WinForms.Xaml.RelativeSource.Self");
        StringAssert.Contains(generated, "AncestorType = typeof(global::Probe.ProbeCard)");
        StringAssert.Contains(generated, "global::Probe.ProbeStatics.Value");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Visuals.VisualTrigger(");
        StringAssert.Contains(generated, ".Setters.Add(new global::VeloxDev.WinForms.Xaml.Visuals.VisualSetter(");
        StringAssert.Contains(generated, "global::VeloxDev.WinForms.Xaml.Visuals.Controls.Grid.RowProperty");
        StringAssert.Contains(generated, "list.ItemTemplate = new global::VeloxDev.WinForms.Xaml.Visuals.DataTemplate(");
        StringAssert.Contains(generated, "host.Template = global::VeloxDev.WinForms.Xaml.Visuals.VisualResourceLookup");
    }
}
