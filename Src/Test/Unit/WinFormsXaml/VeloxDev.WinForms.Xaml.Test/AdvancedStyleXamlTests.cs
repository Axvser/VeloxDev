using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Generator coverage for implicit styles, DataTrigger/MultiTrigger, ControlTemplate, native styles and the
/// unprefixed built-in XAML types.
/// </summary>
[TestClass]
public sealed class AdvancedStyleXamlTests
{
    private const string Source = """
        using System;
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public class NotAControl
        {
        }

        public partial class CardChrome : VeloxDev.WinForms.Xaml.Controls.Control
        {
            public static readonly DependencyProperty LabelProperty =
                DependencyProperty.Register("Label", typeof(string), typeof(CardChrome));

            public string? Label
            {
                get => (string?)GetValue(LabelProperty);
                set => SetValue(LabelProperty, value);
            }
        }

        public partial class Card : VeloxDev.WinForms.Xaml.Controls.Control
        {
            public static readonly DependencyProperty IsHotProperty =
                DependencyProperty.Register("IsHot", typeof(bool), typeof(Card));

            public static readonly DependencyProperty IsPinnedProperty =
                DependencyProperty.Register("IsPinned", typeof(bool), typeof(Card));

            public static readonly DependencyProperty StyleValueProperty =
                DependencyProperty.Register("StyleValue", typeof(int), typeof(Card));

            public static readonly DependencyProperty NoteProperty =
                DependencyProperty.Register("Note", typeof(string), typeof(Card));

            public bool IsHot
            {
                get => (bool)GetValue(IsHotProperty)!;
                set => SetValue(IsHotProperty, value);
            }

            public bool IsPinned
            {
                get => (bool)GetValue(IsPinnedProperty)!;
                set => SetValue(IsPinnedProperty, value);
            }

            public int StyleValue
            {
                get => (int)GetValue(StyleValueProperty)!;
                set => SetValue(StyleValueProperty, value);
            }

            public string? Note
            {
                get => (string?)GetValue(NoteProperty);
                set => SetValue(NoteProperty, value);
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
            xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
            x:Class="Probe.ProbeView">
        """;

    private const string CloseRoot = "</local:ProbeView>";

    private const string ValidResources = """
        <local:ProbeView.Resources>
          <v:ResourceDictionary>
            <v:ControlTemplate x:Key="CardTemplate">
              <local:CardChrome Label="{Binding Title}" />
            </v:ControlTemplate>
            <v:Style x:Key="CardStyle" TargetType="local:Card">
              <v:Style.Setters>
                <v:Setter Property="StyleValue" Value="2" />
              </v:Style.Setters>
              <v:Style.Triggers>
                <v:Trigger Property="IsHot" Value="True">
                  <v:Trigger.Setters>
                    <v:Setter Property="StyleValue" Value="4" />
                  </v:Trigger.Setters>
                  <v:Trigger.EnterAction>
                    <v:TransitionAction Property="StyleValue" To="6" Duration="0:0:0.1" />
                  </v:Trigger.EnterAction>
                </v:Trigger>
                <v:DataTrigger Binding="{Binding IsHot}" Value="True">
                  <v:DataTrigger.Setters>
                    <v:Setter Property="Note" Value="data" />
                  </v:DataTrigger.Setters>
                </v:DataTrigger>
                <v:MultiTrigger>
                  <v:MultiTrigger.Conditions>
                    <v:Condition Property="IsHot" Value="True" />
                    <v:Condition Property="IsPinned" Value="True" />
                  </v:MultiTrigger.Conditions>
                  <v:MultiTrigger.Setters>
                    <v:Setter Property="Note" Value="multi" />
                  </v:MultiTrigger.Setters>
                  <v:MultiTrigger.EnterAction>
                    <v:TransitionAction Property="StyleValue" To="7" Duration="0:0:0.1" />
                  </v:MultiTrigger.EnterAction>
                </v:MultiTrigger>
              </v:Style.Triggers>
            </v:Style>
            <v:Style x:Key="TemplatedStyle" TargetType="local:Card" Template="{StaticResource CardTemplate}">
              <v:Style.Setters>
                <v:Setter Property="StyleValue" Value="3" />
              </v:Style.Setters>
            </v:Style>
            <v:Style TargetType="local:Card">
              <v:Style.Setters>
                <v:Setter Property="StyleValue" Value="5" />
              </v:Style.Setters>
            </v:Style>
            <v:Style x:Key="NativeStyle" TargetType="Button">
              <v:Style.Setters>
                <v:Setter Property="Text" Value="native" />
              </v:Style.Setters>
            </v:Style>
          </v:ResourceDictionary>
        </local:ProbeView.Resources>
        """;

    [TestMethod]
    public void UnprefixedBuiltIns_CompileWithImplicitDataMultiTemplateAndNativeStyles()
    {
        var xaml = Wrap(ValidResources + """
            <local:Card x:Name="card" Style="{StaticResource CardStyle}" />
            <Button x:Name="button" Style="{StaticResource NativeStyle}" />
            """);

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "AdvancedStyleProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(
            output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(output.GetDiagnostics()));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.DataTrigger { Binding = new global::VeloxDev.WinForms.Xaml.Binding(\"IsHot\") }");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.MultiTrigger()");
        StringAssert.Contains(
            generated,
            "Conditions.Add(new global::VeloxDev.WinForms.Xaml.Condition(global::Probe.Card.IsHotProperty, true));");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.ControlTemplate(this.CreateControlTemplate_CardTemplate)");
        StringAssert.Contains(
            generated,
            "this.Resources[typeof(global::Probe.Card)] =");
        StringAssert.Contains(
            generated,
            ".Template = global::VeloxDev.WinForms.Xaml.XamlRuntime.GetResource<global::VeloxDev.WinForms.Xaml.ControlTemplate>(this, \"CardTemplate\");");
        StringAssert.Contains(
            generated,
            "global::VeloxDev.WinForms.Xaml.XamlRuntime.ApplyStyleResource(button, this, \"NativeStyle\");");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Setter(\"Text\", \"native\")");
        StringAssert.Contains(
            generated,
            "new global::VeloxDev.WinForms.Xaml.TransitionAction { Property = global::Probe.Card.StyleValueProperty, To = 6, Effect = new global::VeloxDev.TransitionSystem.TransitionEffect { Duration = global::System.TimeSpan.Parse(\"0:0:0.1\", global::System.Globalization.CultureInfo.InvariantCulture) }, };");
        StringAssert.Contains(
            generated,
            "new global::VeloxDev.WinForms.Xaml.TransitionAction { Property = global::Probe.Card.StyleValueProperty, To = 7, Effect = new global::VeloxDev.TransitionSystem.TransitionEffect { Duration = global::System.TimeSpan.Parse(\"0:0:0.1\", global::System.Globalization.CultureInfo.InvariantCulture) }, };");
    }

    [TestMethod]
    public void ADataTriggerWithoutBindingOrPath_IsXaml004()
    {
        var diagnostic = AssertSingle(
            CardTriggers("<v:DataTrigger Value=\"True\" />"),
            "VELOX_XAML004");

        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Binding");
    }

    [TestMethod]
    public void ADataTriggerBindingThatIsNotMarkup_IsXaml004()
    {
        var diagnostic = AssertSingle(
            CardTriggers("<v:DataTrigger Binding=\"IsHot\" Value=\"True\" />"),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "Binding");
    }

    [TestMethod]
    public void AMultiTriggerWithoutConditions_IsXaml004()
    {
        var diagnostic = AssertSingle(
            CardTriggers("""
                <v:MultiTrigger>
                  <v:MultiTrigger.Setters>
                    <v:Setter Property="Note" Value="x" />
                  </v:MultiTrigger.Setters>
                </v:MultiTrigger>
                """),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "Condition");
    }

    [TestMethod]
    public void AConditionWithoutAProperty_IsXaml004()
    {
        var diagnostic = AssertSingle(
            CardTriggers("""
                <v:MultiTrigger>
                  <v:MultiTrigger.Conditions>
                    <v:Condition Value="True" />
                  </v:MultiTrigger.Conditions>
                  <v:MultiTrigger.Setters>
                    <v:Setter Property="Note" Value="x" />
                  </v:MultiTrigger.Setters>
                </v:MultiTrigger>
                """),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "Property");
    }

    [TestMethod]
    public void AControlTemplateWithoutARoot_IsXaml004()
    {
        var diagnostic = AssertSingle(
            WithResources("<v:ControlTemplate x:Key=\"Bad\" />"),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "root");
    }

    [TestMethod]
    public void AControlTemplateWithMultipleRoots_IsXaml004()
    {
        var diagnostic = AssertSingle(
            WithResources("""
                <v:ControlTemplate x:Key="Bad">
                  <local:CardChrome />
                  <local:CardChrome />
                </v:ControlTemplate>
                """),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "root");
    }

    [TestMethod]
    public void AControlTemplateWithANonControlRoot_IsXaml004()
    {
        var diagnostic = AssertSingle(
            WithResources("""
                <v:ControlTemplate x:Key="Bad">
                  <local:NotAControl />
                </v:ControlTemplate>
                """),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "Control");
    }

    [TestMethod]
    public void ANativeStyleWithAnUnknownProperty_IsXaml002()
    {
        var diagnostic = AssertSingle(
            WithResources("""
                <v:Style x:Key="Bad" TargetType="Button">
                  <v:Style.Setters>
                    <v:Setter Property="Bogus" Value="1" />
                  </v:Style.Setters>
                </v:Style>
                """),
            "VELOX_XAML002");

        StringAssert.Contains(diagnostic.GetMessage(), "Bogus");
    }

    [TestMethod]
    public void ANativeStyleWithAnAttachedProperty_IsXaml004()
    {
        var diagnostic = AssertSingle(
            WithResources("""
                <v:Style x:Key="Bad" TargetType="Button">
                  <v:Style.Setters>
                    <v:Setter Property="Owner.Value" Value="1" />
                  </v:Style.Setters>
                </v:Style>
                """),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "CLR property");
    }

    [TestMethod]
    public void ANativeStyleWithTriggers_IsXaml004()
    {
        var diagnostic = AssertSingle(
            WithResources("""
                <v:Style x:Key="Bad" TargetType="Button">
                  <v:Style.Triggers>
                    <v:Trigger Property="Text" Value="x" />
                  </v:Style.Triggers>
                </v:Style>
                """),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "Triggers");
    }

    [TestMethod]
    public void ANativeStyleWithATemplate_IsXaml004()
    {
        var diagnostic = AssertSingle(
            WithResources("<v:Style x:Key=\"Bad\" TargetType=\"Button\" Template=\"{StaticResource Tpl}\" />"),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "ControlTemplate");
    }

    [TestMethod]
    public void AnImplicitNativeStyle_IsXaml004()
    {
        var diagnostic = AssertSingle(
            WithResources("""
                <v:Style TargetType="Button">
                  <v:Style.Setters>
                    <v:Setter Property="Text" Value="x" />
                  </v:Style.Setters>
                </v:Style>
                """),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "x:Key");
    }

    [TestMethod]
    public void AnImplicitStyleWithoutATargetType_IsXaml014()
    {
        var diagnostic = AssertSingle(
            WithResources("""
                <v:Style>
                  <v:Style.Setters>
                    <v:Setter Property="StyleValue" Value="1" />
                  </v:Style.Setters>
                </v:Style>
                """),
            "VELOX_XAML014");

        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "TargetType");
    }

    [TestMethod]
    public void AStyleTemplateChildThatIsNotAStaticResource_IsXaml004()
    {
        var diagnostic = AssertSingle(
            WithResources("""
                <v:Style x:Key="Bad" TargetType="local:Card">
                  <v:Style.Template>
                    <v:Setter Property="Note" Value="x" />
                  </v:Style.Template>
                </v:Style>
                """),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "StaticResource");
    }

    [TestMethod]
    public void AStyleTemplateAttributeThatIsNotAStaticResource_IsXaml004()
    {
        var diagnostic = AssertSingle(
            WithResources("<v:Style x:Key=\"Bad\" TargetType=\"local:Card\" Template=\"Tpl\" />"),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "Template");
    }

    private static string CardTriggers(string body)
        => Wrap("<local:Card x:Name=\"card\"><local:Card.Triggers>" + body + "</local:Card.Triggers></local:Card>");

    private static string WithResources(string resources)
        => Wrap("<local:ProbeView.Resources><v:ResourceDictionary>" + resources + "</v:ResourceDictionary></local:ProbeView.Resources>");

    private static string Wrap(string body) => OpenRoot + "\n" + body + "\n" + CloseRoot;

    private static Diagnostic AssertSingle(string xaml, string id)
    {
        var result = GeneratorProbe.Run(
            new XamlFileGenerator(),
            Source,
            "AdvancedStyleDiagnosticsProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        var matches = result.Diagnostics
            .Where(diagnostic => diagnostic.Id == id)
            .ToArray();

        Assert.HasCount(1, matches, GeneratorProbe.Describe(result.Diagnostics));
        return matches[0];
    }
}
