using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>The extended markup subset: nested markup extensions, name/relative sources, template bindings and grouped actions.</summary>
[TestClass]
public sealed class ExtendedGeneratorTests
{
    private const string Source = """
        using System.Drawing;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public static class ProbeStatics
        {
            public static string Value => "static";
        }

        public partial class ProbeCard : VeloxDev.WinForms.Xaml.Controls.Control
        {
            public static readonly DependencyProperty TitleProperty =
                DependencyProperty.Register(nameof(Title), typeof(string), typeof(ProbeCard));
            public static readonly DependencyProperty CaptionProperty =
                DependencyProperty.Register(nameof(Caption), typeof(string), typeof(ProbeCard));
            public static readonly DependencyProperty IsHotProperty =
                DependencyProperty.Register(nameof(IsHot), typeof(bool), typeof(ProbeCard));
            public static readonly DependencyProperty GlowProperty =
                DependencyProperty.Register(nameof(Glow), typeof(double), typeof(ProbeCard));
            public static readonly DependencyProperty BorderWidthProperty =
                DependencyProperty.Register(nameof(BorderWidth), typeof(int), typeof(ProbeCard));

            public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
            public string? Caption { get => (string?)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
            public bool IsHot { get => (bool)GetValue(IsHotProperty)!; set => SetValue(IsHotProperty, value); }
            public double Glow { get => (double)GetValue(GlowProperty)!; set => SetValue(GlowProperty, value); }
            public int BorderWidth { get => (int)GetValue(BorderWidthProperty)!; set => SetValue(BorderWidthProperty, value); }
        }

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
        {
        }

        public sealed class ProbeConverter : IValueConverter
        {
            public object? Convert(object? value, System.Type targetType) => value;
            public object? ConvertBack(object? value, System.Type sourceType) => value;
        }
        """;

    private const string Xaml = """
        <local:ProbeView
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:local="clr-namespace:Probe"
            xmlns:native="clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms"
            x:Class="Probe.ProbeView">

          <local:ProbeView.Resources>
            <ResourceDictionary>
              <local:ProbeConverter x:Key="Converter" />
              <ControlTemplate x:Key="CardTemplate" TargetType="local:ProbeCard">
                <Border>
                  <TextBlock Text="{TemplateBinding Title}" />
                </Border>
              </ControlTemplate>
              <Style x:Key="BoundStyle" TargetType="local:ProbeCard">
                <Style.Setters>
                  <Setter Property="Title"
                            Value="{Binding Caption, Converter={StaticResource Converter}, StringFormat=N2, FallbackValue=fallback, TargetNullValue=none, UpdateSourceTrigger=LostFocus}" />
                </Style.Setters>
              </Style>
            </ResourceDictionary>
          </local:ProbeView.Resources>

          <local:ProbeCard x:Name="card"
                           Title="{Binding ElementName=card, Path=Title}"
                           Panel.ZIndex="5">
            <local:ProbeCard.Triggers>
              <Trigger Property="IsHot" Value="True">
                <Trigger.EnterAction>
                  <TransitionAction Property="Glow" To="1" Duration="0:0:0.1" LoopTime="2" IsAutoReverse="True" />
                  <TransitionAction Property="BorderWidth" To="4" Duration="0:0:0.1" />
                </Trigger.EnterAction>
              </Trigger>
            </local:ProbeCard.Triggers>
          </local:ProbeCard>

          <TextBlock Text="{Binding Caption, RelativeSource={RelativeSource Self}}" />
          <TextBlock Text="{Binding Caption, RelativeSource={RelativeSource FindAncestor, AncestorType={x:Type local:ProbeCard}}}" />
          <TextBlock Text="{x:Static local:ProbeStatics.Value}" />

          <native:Button x:Name="nativeButton" Text="Native">
            <native:Button.ContextMenu>
              <ContextMenu>
                <MenuItem Header="Native" />
              </ContextMenu>
            </native:Button.ContextMenu>
          </native:Button>

          <Button x:Name="commandButton" Content="Go">
            <Button.InputBindings>
              <KeyBinding Key="F5" Command="{Binding Save}" />
            </Button.InputBindings>
          </Button>

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
    public void ExtendedMarkup_CompilesAndEmitsTheNewBindingAndActionShape()
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
        StringAssert.Contains(generated, "ElementName = \"card\"");
        StringAssert.Contains(generated, "StringFormat = \"N2\"");
        StringAssert.Contains(generated, "FallbackValue = \"fallback\"");
        StringAssert.Contains(generated, "TargetNullValue = \"none\"");
        StringAssert.Contains(generated, "UpdateSourceTrigger = global::VeloxDev.WinForms.Xaml.UpdateSourceTrigger.LostFocus");
        StringAssert.Contains(generated, "Converter = (global::VeloxDev.WinForms.Xaml.IValueConverter)");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.TemplateBinding(\"Title\")");
        StringAssert.Contains(generated, "global::VeloxDev.WinForms.Xaml.RelativeSource.Self");
        StringAssert.Contains(generated, "AncestorType = typeof(global::Probe.ProbeCard)");
        StringAssert.Contains(generated, "global::Probe.ProbeStatics.Value");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.TriggerActionGroup()");
        StringAssert.Contains(generated, "LoopTime = 2");
        StringAssert.Contains(generated, "IsAutoReverse = true");
        StringAssert.Contains(generated, "global::VeloxDev.WinForms.Xaml.Controls.Panel.ZIndexProperty");
        StringAssert.Contains(generated, "nativeButton.ContextMenuStrip =");
        StringAssert.Contains(generated, "global::VeloxDev.WinForms.Xaml.XamlRuntime.SetObjectBinding(commandButton, __xaml");
    }
}
