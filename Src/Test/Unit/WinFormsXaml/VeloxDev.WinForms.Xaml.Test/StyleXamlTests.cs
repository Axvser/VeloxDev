using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The generated style resource path: Style/TargetType/Setters/Triggers/BasedOn compile into <see cref="Style"/>
/// objects, and malformed styles produce stable XAML diagnostics.
/// </summary>
[TestClass]
public sealed class StyleXamlTests
{
    private const string Source = """
        using System;
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public partial class Plain : System.Windows.Forms.Control
        {
        }

        public class NotAControl
        {
        }

        public partial class ProbeCard : VeloxDev.WinForms.Xaml.Controls.Control
        {
            public static readonly DependencyProperty BorderWidthProperty =
                DependencyProperty.Register("BorderWidth", typeof(int), typeof(ProbeCard));

            public static readonly DependencyProperty GlowProperty =
                DependencyProperty.Register("Glow", typeof(double), typeof(ProbeCard));

            public static readonly DependencyProperty IsHotProperty =
                DependencyProperty.Register("IsHot", typeof(bool), typeof(ProbeCard));

            public static readonly DependencyProperty StyleValueProperty =
                DependencyProperty.Register("StyleValue", typeof(int), typeof(ProbeCard));

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

            public bool IsHot
            {
                get => (bool)GetValue(IsHotProperty)!;
                set => SetValue(IsHotProperty, value);
            }

            public int StyleValue
            {
                get => (int)GetValue(StyleValueProperty)!;
                set => SetValue(StyleValueProperty, value);
            }
        }

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
        {
        }
        """;

    private const string OpenRoot = """
        <local:ProbeView
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:local="clr-namespace:Probe"
            x:Class="Probe.ProbeView">
        """;

    private const string CloseRoot = "</local:ProbeView>";

    [TestMethod]
    public void StyleResources_EmitTargetTypeSettersTriggersAndBasedOn()
    {
        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "StyleXamlProbe",
            new InMemoryAdditionalText("ProbeView.xaml", Wrap(ValidResources())));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Style()");
        StringAssert.Contains(generated, "TargetType = typeof(global::Probe.ProbeCard);");
        StringAssert.Contains(
            generated,
            ".Setters.Add(new global::VeloxDev.WinForms.Xaml.Setter(global::Probe.ProbeCard.BorderWidthProperty, 2));");
        StringAssert.Contains(
            generated,
            ".Setters.Add(new global::VeloxDev.WinForms.Xaml.Setter(global::Probe.ProbeCard.GlowProperty, 0.25D));");
        StringAssert.Contains(
            generated,
            ".Setters.Add(new global::VeloxDev.WinForms.Xaml.Setter(global::Probe.ProbeCard.StyleValueProperty, 3));");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Trigger(global::Probe.ProbeCard.IsHotProperty)");
        StringAssert.Contains(generated, ".Triggers.Add(__xaml");
        StringAssert.Contains(
            generated,
            "new global::VeloxDev.WinForms.Xaml.TransitionAction { Property = global::Probe.ProbeCard.GlowProperty, To = 1D, Effect = new global::VeloxDev.TransitionSystem.TransitionEffect { Duration = global::System.TimeSpan.Parse(\"0:0:0.1\", global::System.Globalization.CultureInfo.InvariantCulture) }, };");
        StringAssert.Contains(generated, "this.Resources[\"NodeStyle\"] =");
        StringAssert.Contains(generated, "this.Resources[\"ChildStyle\"] =");
        StringAssert.Contains(
            generated,
            ".BasedOn = global::VeloxDev.WinForms.Xaml.XamlRuntime.GetResource<global::VeloxDev.WinForms.Xaml.Style>(this, \"NodeStyle\");");
        StringAssert.Contains(
            generated,
            "card.ApplyStyleResource(this, \"ChildStyle\");");
    }

    [TestMethod]
    public void BasedOnCanReferenceAStyleDeclaredLaterInTheSameDictionary()
    {
        var xaml = Wrap("""
            <local:ProbeView.Resources>
              <ResourceDictionary>
                <Style x:Key="Child" TargetType="local:ProbeCard" BasedOn="{StaticResource Base}">
                  <Style.Setters>
                    <Setter Property="StyleValue" Value="3" />
                  </Style.Setters>
                </Style>
                <Style x:Key="Base" TargetType="local:ProbeCard">
                  <Style.Setters>
                    <Setter Property="BorderWidth" Value="2" />
                  </Style.Setters>
                </Style>
              </ResourceDictionary>
            </local:ProbeView.Resources>
            <local:ProbeCard x:Name="card" Style="{StaticResource Child}" />
            """);

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "StyleBasedOnOrderProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(
            output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(output.GetDiagnostics()));

        StringAssert.Contains(
            run.GeneratedText(),
            ".BasedOn = global::VeloxDev.WinForms.Xaml.XamlRuntime.GetResource<global::VeloxDev.WinForms.Xaml.Style>(this, \"Base\");");
    }

    [TestMethod]
    public void AMissingStyleTargetType_IsXaml014()
    {
        var diagnostic = AssertSingle(
            Wrap("""
                <local:ProbeView.Resources>
                  <ResourceDictionary>
                    <Style x:Key="Bad">
                      <Style.Setters>
                        <Setter Property="BorderWidth" Value="2" />
                      </Style.Setters>
                    </Style>
                  </ResourceDictionary>
                </local:ProbeView.Resources>
                """),
            "VELOX_XAML014");

        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "TargetType");
    }

    [TestMethod]
    public void AnUnresolvableStyleTargetType_IsXaml014()
    {
        var diagnostic = AssertSingle(StyleWithTargetType("local:Missing"), "VELOX_XAML014");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Missing");
    }

    [TestMethod]
    public void ANonControlStyleTargetType_IsXaml014()
    {
        var diagnostic = AssertSingle(StyleWithTargetType("local:NotAControl"), "VELOX_XAML014");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Control");
    }

    [TestMethod]
    public void AnUnknownStyleSetterProperty_IsXaml002()
    {
        var diagnostic = AssertSingle(
            StyleWithBody("""
                <Style.Setters>
                  <Setter Property="Bogus" Value="2" />
                </Style.Setters>
                """),
            "VELOX_XAML002");

        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Bogus");
    }

    [TestMethod]
    public void AnUnknownStyleTriggerProperty_IsXaml002()
    {
        var diagnostic = AssertSingle(
            StyleWithBody("""
                <Style.Triggers>
                  <Trigger Property="Bogus" Value="True" />
                </Style.Triggers>
                """),
            "VELOX_XAML002");

        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Bogus");
    }

    [TestMethod]
    public void AnUnsupportedStyleMember_IsXaml004()
    {
        var diagnostic = AssertSingle(
            StyleWithBody("<Style.Nonsense />"),
            "VELOX_XAML004");

        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Nonsense");
    }

    [TestMethod]
    public void AnUnsupportedBasedOn_IsXaml004()
    {
        var diagnostic = AssertSingle(
            Wrap("""
                <local:ProbeView.Resources>
                  <ResourceDictionary>
                    <Style x:Key="Bad" TargetType="local:ProbeCard" BasedOn="NodeStyle" />
                  </ResourceDictionary>
                </local:ProbeView.Resources>
                """),
            "VELOX_XAML004");

        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "BasedOn");
    }

    private static string ValidResources() => """
        <local:ProbeView.Resources>
          <ResourceDictionary>
            <Style x:Key="NodeStyle" TargetType="local:ProbeCard">
              <Style.Setters>
                <Setter Property="BorderWidth" Value="2" />
                <Setter Property="Glow" Value="0.25" />
              </Style.Setters>
              <Style.Triggers>
                <Trigger Property="IsHot" Value="True">
                  <Trigger.Setters>
                    <Setter Property="BorderWidth" Value="4" />
                  </Trigger.Setters>
                  <Trigger.EnterAction>
                    <TransitionAction Property="Glow" To="1" Duration="0:0:0.1" />
                  </Trigger.EnterAction>
                </Trigger>
              </Style.Triggers>
            </Style>
            <Style x:Key="ChildStyle" TargetType="{x:Type local:ProbeCard}" BasedOn="{StaticResource NodeStyle}">
              <Style.Setters>
                <Setter Property="StyleValue" Value="3" />
              </Style.Setters>
            </Style>
          </ResourceDictionary>
        </local:ProbeView.Resources>
        <local:ProbeCard x:Name="card" Style="{StaticResource ChildStyle}" />
        """;

    private static string StyleWithTargetType(string targetType) => Wrap($"""
        <local:ProbeView.Resources>
          <ResourceDictionary>
            <Style x:Key="Bad" TargetType="{targetType}">
              <Style.Setters>
                <Setter Property="BorderWidth" Value="2" />
              </Style.Setters>
            </Style>
          </ResourceDictionary>
        </local:ProbeView.Resources>
        """);

    private static string StyleWithBody(string body) => Wrap($"""
        <local:ProbeView.Resources>
          <ResourceDictionary>
            <Style x:Key="Bad" TargetType="local:ProbeCard">
              {body}
            </Style>
          </ResourceDictionary>
        </local:ProbeView.Resources>
        """);

    private static string Wrap(string body) => OpenRoot + "\n" + body + "\n" + CloseRoot;

    private static Diagnostic AssertSingle(string xaml, string id)
    {
        var result = GeneratorProbe.Run(
            new XamlFileGenerator(),
            Source,
            "StyleDiagnosticsProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        var matches = result.Diagnostics
            .Where(diagnostic => diagnostic.Id == id)
            .ToArray();

        Assert.HasCount(1, matches, GeneratorProbe.Describe(result.Diagnostics));
        return matches[0];
    }
}
