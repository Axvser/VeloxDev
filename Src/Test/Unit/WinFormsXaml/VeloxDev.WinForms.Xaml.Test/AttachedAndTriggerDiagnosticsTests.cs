using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The diagnostics for the two accepted XAML forms: attached attributes and inline trigger trees. Malformed files are
/// driven through the generator directly because they never reach a compiled output.
/// </summary>
[TestClass]
public sealed class AttachedAndTriggerDiagnosticsTests
{
    private const string Source = """
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public class NotAnAction
        {
        }

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

    private const string OpenRoot = """
        <local:ProbeView
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:local="clr-namespace:Probe"
            xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
            x:Class="Probe.ProbeView">
        """;

    private const string CloseRoot = "</local:ProbeView>";

    [TestMethod]
    public void AnUnknownAttachedOwner_IsXaml001()
    {
        var diagnostic = AssertSingle(Wrap("<Button local:MissingOwner.Column=\"1\" />"), "VELOX_XAML001");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "MissingOwner");
    }

    [TestMethod]
    public void AnUnknownAttachedProperty_IsXaml013()
    {
        var diagnostic = AssertSingle(Wrap("<Button local:WorkflowPanel.Missing=\"1\" />"), "VELOX_XAML013");
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "WorkflowPanel");
        StringAssert.Contains(diagnostic.GetMessage(), "Missing");
    }

    [TestMethod]
    public void AnUnprefixedAttachedAttribute_IsXaml004()
    {
        var diagnostic = AssertSingle(Wrap("<Button WorkflowPanel.Column=\"1\" />"), "VELOX_XAML004");
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "clr-namespace");
    }

    [TestMethod]
    public void ATriggerWithoutAProperty_IsXaml004()
    {
        var diagnostic = AssertSingle(
            Wrap(TriggerCard("<v:Trigger Value=\"True\" />")),
            "VELOX_XAML004");

        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Property");
    }

    [TestMethod]
    public void AnUnknownTriggerProperty_IsXaml002()
    {
        var diagnostic = AssertSingle(
            Wrap(TriggerCard("<v:Trigger Property=\"Bogus\" Value=\"True\" />")),
            "VELOX_XAML002");

        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Bogus");
    }

    [TestMethod]
    public void AnUnknownSetterProperty_IsXaml002()
    {
        var diagnostic = AssertSingle(
            Wrap(TriggerCard("""
                <v:Trigger Property="IsHot" Value="True">
                  <v:Trigger.Setters>
                    <v:Setter Property="Bogus" Value="1" />
                  </v:Trigger.Setters>
                </v:Trigger>
                """)),
            "VELOX_XAML002");

        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Bogus");
    }

    [TestMethod]
    public void AnUnsupportedTriggerAction_IsXaml004()
    {
        var diagnostic = AssertSingle(
            Wrap(TriggerCard("""
                <v:Trigger Property="IsHot" Value="True">
                  <v:Trigger.EnterAction>
                    <local:NotAnAction />
                  </v:Trigger.EnterAction>
                </v:Trigger>
                """)),
            "VELOX_XAML004");

        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "NotAnAction");
    }

    [TestMethod]
    public void AnInvalidTransitionDuration_IsXaml003()
    {
        var diagnostic = AssertSingle(
            Wrap(TriggerCard("""
                <v:Trigger Property="IsHot" Value="True">
                  <v:Trigger.EnterAction>
                    <v:TransitionAction Property="Glow" To="1" Duration="not-a-duration" />
                  </v:Trigger.EnterAction>
                </v:Trigger>
                """)),
            "VELOX_XAML003");

        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "not-a-duration");
    }

    [TestMethod]
    public void ATwoWayAttachedBinding_IsXaml004AndFallsBackToOneWay()
    {
        var result = Run(Wrap("<Button local:WorkflowPanel.Column=\"{Binding Column, Mode=TwoWay}\" />"));
        var diagnostic = AssertSingle(result, "VELOX_XAML004");

        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "TwoWay");
        StringAssert.Contains(result.GeneratedText(), "SetAttachedBinding(");
    }

    [TestMethod]
    public void AnUnknownEaseName_IsXaml003()
    {
        var diagnostic = AssertSingle(
            Wrap(TriggerCard("""
                <v:Trigger Property="IsHot" Value="True">
                  <v:Trigger.EnterAction>
                    <v:TransitionAction Property="Glow" To="1" Ease="Nope" />
                  </v:Trigger.EnterAction>
                </v:Trigger>
                """)),
            "VELOX_XAML003");

        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Nope");
    }

    private static string TriggerCard(string trigger)
        => "<local:ProbeCard x:Name=\"card\"><local:ProbeCard.Triggers>" + trigger + "</local:ProbeCard.Triggers></local:ProbeCard>";

    private static string Wrap(string body) => OpenRoot + "\n" + body + "\n" + CloseRoot;

    private static Diagnostic AssertSingle(string xaml, string id) => AssertSingle(Run(xaml), id);

    private static Diagnostic AssertSingle(GeneratorDriverRunResult result, string id)
    {
        var matches = result.Diagnostics
            .Where(diagnostic => diagnostic.Id == id)
            .ToArray();

        Assert.HasCount(1, matches, GeneratorProbe.Describe(result.Diagnostics));
        return matches[0];
    }

    private static GeneratorDriverRunResult Run(string xaml)
        => GeneratorProbe.Run(
            new XamlFileGenerator(),
            Source,
            "AttachedTriggerDiagnosticsProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));
}
