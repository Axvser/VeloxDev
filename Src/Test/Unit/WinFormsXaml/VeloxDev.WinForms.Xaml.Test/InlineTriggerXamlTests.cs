using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Inline XAML triggers: the trigger tree compiles into <see cref="Trigger"/> objects, and the runtime path enters
/// and exits with setters and transition actions.
/// </summary>
[STATestClass]
public sealed class InlineTriggerXamlTests
{
    private const string Source = """
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public static class WorkflowPanel
        {
            public static readonly DependencyProperty ColumnProperty =
                DependencyProperty.RegisterAttached("Column", typeof(int), typeof(WorkflowPanel), new PropertyMetadata(0));

            public static int GetColumn(System.Windows.Forms.Control control) => (int)ColumnProperty.GetValue(control)!;
            public static void SetColumn(System.Windows.Forms.Control control, int value) => ColumnProperty.SetValue(control, value);
        }

        public partial class ProbeCard : VeloxDev.WinForms.Xaml.Controls.Control
        {
            public static readonly DependencyProperty IsHotProperty =
                DependencyProperty.Register("IsHot", typeof(bool), typeof(ProbeCard));

            public static readonly DependencyProperty BorderWidthProperty =
                DependencyProperty.Register("BorderWidth", typeof(int), typeof(ProbeCard));

            public static readonly DependencyProperty GlowProperty =
                DependencyProperty.Register("Glow", typeof(double), typeof(ProbeCard));

            public bool IsHot
            {
                get => (bool)GetValue(IsHotProperty)!;
                set => SetValue(IsHotProperty, value);
            }

            public int BorderWidth
            {
                get => (int)GetValue(BorderWidthProperty)!;
                set => SetValue(BorderWidthProperty, value);
            }

            public double Glow
            {
                get => (double)GetValue(GlowProperty)!;
                set => SetValue(GlowProperty, value);
            }
        }

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

          <local:ProbeCard x:Name="card">
            <local:ProbeCard.Triggers>
              <v:Trigger Property="IsHot" Value="True">
                <v:Trigger.Setters>
                  <v:Setter Property="BorderWidth" Value="4" />
                  <v:Setter Property="WorkflowPanel.Column" Value="3" />
                </v:Trigger.Setters>
                <v:Trigger.EnterAction>
                  <v:TransitionAction Property="Glow" To="1" Duration="0:0:0.3" />
                </v:Trigger.EnterAction>
                <v:Trigger.ExitAction>
                  <v:TransitionAction Property="Glow" To="0" Duration="0:0:0.3" />
                </v:Trigger.ExitAction>
              </v:Trigger>
            </local:ProbeCard.Triggers>
          </local:ProbeCard>

        </local:ProbeView>
        """;

    [TestMethod]
    public void InlineTriggers_EmitTriggerSettersAndTransitionActions()
    {
        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "InlineTriggerProbe",
            new InMemoryAdditionalText("ProbeView.xaml", Xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Trigger(global::Probe.ProbeCard.IsHotProperty)");
        StringAssert.Contains(generated, ".Value = true;");
        StringAssert.Contains(
            generated,
            ".Setters.Add(new global::VeloxDev.WinForms.Xaml.Setter(global::Probe.ProbeCard.BorderWidthProperty, 4));");
        StringAssert.Contains(
            generated,
            ".Setters.Add(new global::VeloxDev.WinForms.Xaml.Setter(global::Probe.WorkflowPanel.ColumnProperty, 3));");
        StringAssert.Contains(
            generated,
            "new global::VeloxDev.WinForms.Xaml.TransitionAction { Property = global::Probe.ProbeCard.GlowProperty, To = 1D, Effect = new global::VeloxDev.TransitionSystem.TransitionEffect { Duration = global::System.TimeSpan.Parse(\"0:0:0.3\", global::System.Globalization.CultureInfo.InvariantCulture) }, };");
        StringAssert.Contains(
            generated,
            "new global::VeloxDev.WinForms.Xaml.TransitionAction { Property = global::Probe.ProbeCard.GlowProperty, To = 0D, Effect = new global::VeloxDev.TransitionSystem.TransitionEffect { Duration = global::System.TimeSpan.Parse(\"0:0:0.3\", global::System.Globalization.CultureInfo.InvariantCulture) }, };");
        StringAssert.Contains(generated, "card.Triggers.Add(");
    }

    [TestMethod]
    public void InlineTrigger_WithoutValueToOrDuration_UsesDefaults()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
                x:Class="Probe.ProbeView">
              <local:ProbeCard x:Name="card">
                <local:ProbeCard.Triggers>
                  <v:Trigger Property="IsHot">
                    <v:Trigger.EnterAction>
                      <v:TransitionAction Property="Glow" />
                    </v:Trigger.EnterAction>
                  </v:Trigger>
                </local:ProbeCard.Triggers>
              </local:ProbeCard>
            </local:ProbeView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "InlineTriggerDefaultsProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(
            output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(output.GetDiagnostics()));

        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "new global::VeloxDev.WinForms.Xaml.TransitionAction { Property = global::Probe.ProbeCard.GlowProperty, To = null, };");
        Assert.IsFalse(generated.Contains(".Value =", StringComparison.Ordinal), "an omitted Value must not be emitted");
    }

    [TestMethod]
    public void InlineTransitionAction_MapsEaseNames()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
                x:Class="Probe.ProbeView">
              <local:ProbeCard x:Name="card">
                <local:ProbeCard.Triggers>
                  <v:Trigger Property="IsHot" Value="True">
                    <v:Trigger.EnterAction>
                      <v:TransitionAction Property="Glow" To="1" Duration="0:0:0.1" Ease="CubicOut" />
                    </v:Trigger.EnterAction>
                    <v:Trigger.ExitAction>
                      <v:TransitionAction Property="Glow" To="0" Duration="0:0:0.1" Ease="Sine.InOut" />
                    </v:Trigger.ExitAction>
                  </v:Trigger>
                </local:ProbeCard.Triggers>
              </local:ProbeCard>
            </local:ProbeView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "InlineTriggerEaseProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(
            output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(output.GetDiagnostics()));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "Ease = global::VeloxDev.TransitionSystem.Eases.Cubic.Out");
        StringAssert.Contains(generated, "Ease = global::VeloxDev.TransitionSystem.Eases.Sine.InOut");
    }

    [TestMethod]
    public void InlineKeyFrameAnimation_EmitsTheFrameChain()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
                x:Class="Probe.ProbeView">
              <local:ProbeCard x:Name="card">
                <local:ProbeCard.Triggers>
                  <v:Trigger Property="IsHot" Value="True">
                    <v:Trigger.EnterAction>
                      <v:KeyFrameAnimation Property="Glow" Duration="0:0:0.3" Ease="CubicOut">
                        <v:KeyFrame Offset="0" Value="0" />
                        <v:KeyFrame Offset="0.5" Value="0.5" />
                        <v:KeyFrame Offset="1" Value="1" />
                      </v:KeyFrameAnimation>
                    </v:Trigger.EnterAction>
                  </v:Trigger>
                </local:ProbeCard.Triggers>
              </local:ProbeCard>
            </local:ProbeView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "InlineKeyFrameProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(
            output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(output.GetDiagnostics()));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.KeyFrameAnimation { Property = global::Probe.ProbeCard.GlowProperty");
        StringAssert.Contains(generated, "Ease = global::VeloxDev.TransitionSystem.Eases.Cubic.Out");
        StringAssert.Contains(generated, "KeyFrames.Add(new global::VeloxDev.WinForms.Xaml.KeyFrame { Offset = 0.5D, Value = 0.5D });");
        StringAssert.Contains(generated, "KeyFrames.Add(new global::VeloxDev.WinForms.Xaml.KeyFrame { Offset = 1D, Value = 1D });");
    }

    [TestMethod]
    public void InlineTrigger_EntersAndExitsWithSetterAndTransition()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();
        XamlTestSurface.EnsureHandle(card);

        card.IsHot = true;

        Assert.AreEqual(4, card.BorderWidth);
        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => card.Glow >= 0.99),
            $"Glow never reached 1; last value {card.Glow}");

        card.IsHot = false;

        Assert.AreEqual(0, card.BorderWidth, "the setter value must be restored on exit");
        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => card.Glow <= 0.01),
            $"Glow never returned to 0; last value {card.Glow}");
    }
}
