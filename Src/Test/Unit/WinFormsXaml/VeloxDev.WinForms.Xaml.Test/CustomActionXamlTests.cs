using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Custom <see cref="TriggerAction"/> types declared in XAML: generated construction/property assignment for control
/// and style triggers, and the diagnostics for unusable action types or properties.
/// </summary>
[TestClass]
public sealed class CustomActionXamlTests
{
    private const string Source = """
        using System;
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public class NotAnAction
        {
        }

        public sealed class CustomAction : TriggerAction
        {
            public string? Tag { get; set; }
            public int Amount { get; set; }
            public int ReadOnly { get; private set; }
            public static int StaticValue { get; set; }

            public override void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control) { }
        }

        public sealed class NoCtorAction : TriggerAction
        {
            public NoCtorAction(int value) { }

            public override void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control) { }
        }

        public partial class ProbeCard : VeloxDev.WinForms.Xaml.Controls.Control
        {
            public static readonly DependencyProperty IsHotProperty =
                DependencyProperty.Register("IsHot", typeof(bool), typeof(ProbeCard));

            public bool IsHot
            {
                get => (bool)GetValue(IsHotProperty)!;
                set => SetValue(IsHotProperty, value);
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

    private const string ValidXaml = """
        <local:ProbeView.Resources>
          <ResourceDictionary>
            <x:String x:Key="ActionTag">from-resource</x:String>
            <Style x:Key="CardStyle" TargetType="local:ProbeCard">
              <Style.Triggers>
                <Trigger Property="IsHot" Value="True">
                  <Trigger.EnterAction>
                    <local:CustomAction Tag="style" Amount="5" />
                  </Trigger.EnterAction>
                </Trigger>
              </Style.Triggers>
            </Style>
          </ResourceDictionary>
        </local:ProbeView.Resources>
        <local:ProbeCard x:Name="card" Style="{StaticResource CardStyle}">
          <local:ProbeCard.Triggers>
            <Trigger Property="IsHot" Value="True">
              <Trigger.EnterAction>
                <local:CustomAction Tag="{StaticResource ActionTag}" Amount="3" />
              </Trigger.EnterAction>
              <Trigger.ExitAction>
                <local:CustomAction Tag="exit" Amount="4" />
              </Trigger.ExitAction>
            </Trigger>
          </local:ProbeCard.Triggers>
        </local:ProbeCard>
        """;

    [TestMethod]
    public void CustomActions_CompileForControlAndStyleTriggers()
    {
        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "CustomActionProbe",
            new InMemoryAdditionalText("ProbeView.xaml", Wrap(ValidXaml)));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(
            output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(output.GetDiagnostics()));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "new global::Probe.CustomAction();");
        StringAssert.Contains(generated, ".Amount = 3;");
        StringAssert.Contains(generated, ".Tag = \"exit\";");
        StringAssert.Contains(generated, ".Amount = 4;");
        StringAssert.Contains(generated, ".Tag = \"style\";");
        StringAssert.Contains(generated, ".Amount = 5;");
        StringAssert.Contains(
            generated,
            ".Tag = global::VeloxDev.WinForms.Xaml.XamlRuntime.GetResource<global::System.String?>(this, \"ActionTag\");");
        StringAssert.Contains(generated, ".EnterAction = __xaml");
        StringAssert.Contains(generated, ".ExitAction = __xaml");
    }

    [TestMethod]
    public void AnUnknownActionType_IsXaml001()
    {
        var diagnostic = AssertSingle(CardTriggers("<local:MissingAction />"), "VELOX_XAML001");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "MissingAction");
    }

    [TestMethod]
    public void AnActionTypeThatIsNotATriggerAction_IsXaml004()
    {
        var diagnostic = AssertSingle(CardTriggers("<local:NotAnAction />"), "VELOX_XAML004");
        StringAssert.Contains(diagnostic.GetMessage(), "TriggerAction");
    }

    [TestMethod]
    public void AnActionWithoutAPublicParameterlessConstructor_IsXaml004()
    {
        var diagnostic = AssertSingle(CardTriggers("<local:NoCtorAction />"), "VELOX_XAML004");
        StringAssert.Contains(diagnostic.GetMessage(), "parameterless");
    }

    [TestMethod]
    public void AnUnknownActionProperty_IsXaml002()
    {
        var diagnostic = AssertSingle(CardTriggers("<local:CustomAction Bogus=\"1\" />"), "VELOX_XAML002");
        StringAssert.Contains(diagnostic.GetMessage(), "Bogus");
    }

    [TestMethod]
    public void AReadOnlyActionProperty_IsXaml004()
    {
        var diagnostic = AssertSingle(CardTriggers("<local:CustomAction ReadOnly=\"1\" />"), "VELOX_XAML004");
        StringAssert.Contains(diagnostic.GetMessage(), "ReadOnly");
    }

    [TestMethod]
    public void AStaticActionProperty_IsXaml004()
    {
        var diagnostic = AssertSingle(CardTriggers("<local:CustomAction StaticValue=\"1\" />"), "VELOX_XAML004");
        StringAssert.Contains(diagnostic.GetMessage(), "StaticValue");
    }

    [TestMethod]
    public void ABadActionPropertyValue_IsXaml003()
    {
        var diagnostic = AssertSingle(CardTriggers("<local:CustomAction Amount=\"abc\" />"), "VELOX_XAML003");
        StringAssert.Contains(diagnostic.GetMessage(), "abc");
    }

    [TestMethod]
    public void NestedActionContent_IsXaml004()
    {
        var diagnostic = AssertSingle(
            CardTriggers("<local:CustomAction><local:NotAnAction /></local:CustomAction>"),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "Nested");
    }

    [TestMethod]
    public void AnUnsupportedActionPropertyMarkup_IsXaml004()
    {
        var diagnostic = AssertSingle(
            CardTriggers("<local:CustomAction Tag=\"{Binding Tag}\" />"),
            "VELOX_XAML004");

        StringAssert.Contains(diagnostic.GetMessage(), "Binding");
    }

    private static string CardTriggers(string action)
        => Wrap(
            "<local:ProbeCard x:Name=\"card\"><local:ProbeCard.Triggers>"
            + "<Trigger Property=\"IsHot\" Value=\"True\"><Trigger.EnterAction>"
            + action
            + "</Trigger.EnterAction></Trigger></local:ProbeCard.Triggers></local:ProbeCard>");

    private static string Wrap(string body) => OpenRoot + "\n" + body + "\n" + CloseRoot;

    private static Diagnostic AssertSingle(string xaml, string id)
    {
        var result = GeneratorProbe.Run(
            new XamlFileGenerator(),
            Source,
            "CustomActionDiagnosticsProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        var matches = result.Diagnostics
            .Where(diagnostic => diagnostic.Id == id)
            .ToArray();

        Assert.HasCount(1, matches, GeneratorProbe.Describe(result.Diagnostics));
        return matches[0];
    }
}
