using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// XAML event wiring for the custom input events: direct Control events and native-control bridge subscriptions,
/// plus missing/ambiguous/bad-signature handler diagnostics.
/// </summary>
[TestClass]
public sealed class InputEventXamlTests
{
    private const string Source = """
        using System;
        using System.Drawing;
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public partial class ProbeCard : VeloxDev.WinForms.Xaml.Controls.Control
        {
        }

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
        {
            private void OnPointerPressed(object? sender, PointerPressedEventArgs e) { }
            private void OnPointerMoved(object? sender, PointerEventArgs e) { }
            private void OnKeyDown(object? sender, KeyEventArgs e) { }
            private void OnKeyInputDown(object? sender, KeyInputEventArgs e) { }
            private void OnNativePressed(object? sender, PointerPressedEventArgs e) { }
            private void OnNativeKeyDown(object? sender, KeyEventArgs e) { }
            private void OnNativeMouseDown(object? sender, MouseEventArgs e) { }
            private void OnClick(object? sender, EventArgs e) { }

            private void OnAmbiguous(object? sender, PointerPressedEventArgs e) { }
            private void OnAmbiguous(object? sender, PointerEventArgs e) { }

            private void OnBad(object? sender) { }
        }
        """;

    private const string OpenRoot = """
        <local:ProbeView
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:local="clr-namespace:Probe"
            xmlns:native="clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms"
            x:Class="Probe.ProbeView">
        """;

    private const string CloseRoot = "</local:ProbeView>";

    [TestMethod]
    public void InputEvents_WireNativeEventsAndExplicitPointerBridges()
    {
        const string xaml = """
            <local:ProbeView.Resources />
            <local:ProbeCard x:Name="card"
                             PointerPressed="OnPointerPressed"
                             PointerMoved="OnPointerMoved"
                             KeyDown="OnKeyDown"
                             KeyInputDown="OnKeyInputDown" />
            <native:Button x:Name="button"
                           PointerPressed="OnNativePressed"
                    KeyDown="OnNativeKeyDown"
                    KeyInputDown="OnKeyInputDown"
                    MouseDown="OnNativeMouseDown"
                    Click="OnClick" />
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "InputEventXamlProbe",
            new InMemoryAdditionalText("ProbeView.xaml", Wrap(xaml)));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(
            output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(output.GetDiagnostics()));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "card.PointerPressed += this.OnPointerPressed;");
        StringAssert.Contains(generated, "card.PointerMoved += this.OnPointerMoved;");
        StringAssert.Contains(generated, "card.KeyDown += this.OnKeyDown;");
        StringAssert.Contains(generated, "card.KeyInputDown += this.OnKeyInputDown;");
        StringAssert.Contains(
            generated,
            "global::VeloxDev.WinForms.Xaml.XamlInput.AddPointerPressed(button, this.OnNativePressed);");
        StringAssert.Contains(generated, "button.KeyDown += this.OnNativeKeyDown;");
        StringAssert.Contains(
            generated,
            "global::VeloxDev.WinForms.Xaml.XamlInput.AddKeyInputDown(button, this.OnKeyInputDown);");
        StringAssert.Contains(generated, "button.MouseDown += this.OnNativeMouseDown;");
        StringAssert.Contains(generated, "button.Click += this.OnClick;");

        Assert.IsFalse(
            generated.Contains("XamlInput.AddKeyDown", StringComparison.Ordinal),
            "native KeyDown must not be routed through the custom keyboard bridge");
    }

    [TestMethod]
    public void AMissingHandler_IsXaml008()
    {
        var diagnostic = AssertSingle(
            CardAttributes("PointerPressed=\"DoesNotExist\""),
            "VELOX_XAML008");

        StringAssert.Contains(diagnostic.GetMessage(), "DoesNotExist");
    }

    [TestMethod]
    public void AnAmbiguousHandler_IsXaml016()
    {
        var diagnostic = AssertSingle(
            CardAttributes("PointerPressed=\"OnAmbiguous\""),
            "VELOX_XAML016");

        StringAssert.Contains(diagnostic.GetMessage(), "OnAmbiguous");
    }

    [TestMethod]
    public void ABadControlHandlerSignature_IsXaml017()
    {
        var diagnostic = AssertSingle(
            CardAttributes("PointerPressed=\"OnBad\""),
            "VELOX_XAML017");

        StringAssert.Contains(diagnostic.GetMessage(), "PointerPressed");
    }

    [TestMethod]
    public void ABadNativeBridgeHandlerSignature_IsXaml017()
    {
        var diagnostic = AssertSingle(
            ButtonAttributes("PointerPressed=\"OnBad\""),
            "VELOX_XAML017");

        StringAssert.Contains(diagnostic.GetMessage(), "PointerPressed");
    }

    [TestMethod]
    public void ANativeKeyDownHandlerWithTheCustomArgs_IsXaml017()
    {
        var diagnostic = AssertSingle(
            ButtonAttributes("KeyDown=\"OnKeyInputDown\""),
            "VELOX_XAML017");

        StringAssert.Contains(diagnostic.GetMessage(), "KeyDown");
    }

    [TestMethod]
    public void AMissingNativeBridgeHandler_IsXaml008()
    {
        var diagnostic = AssertSingle(
            ButtonAttributes("PointerPressed=\"DoesNotExist\""),
            "VELOX_XAML008");

        StringAssert.Contains(diagnostic.GetMessage(), "DoesNotExist");
    }

    private static string CardAttributes(string attributes)
        => Wrap($"<local:ProbeCard x:Name=\"card\" {attributes} />");

    private static string ButtonAttributes(string attributes)
        => Wrap($"<native:Button x:Name=\"button\" {attributes} />");

    private static string Wrap(string body) => OpenRoot + "\n" + body + "\n" + CloseRoot;

    private static Diagnostic AssertSingle(string xaml, string id)
    {
        var result = GeneratorProbe.Run(
            new XamlFileGenerator(),
            Source,
            "InputEventDiagnosticsProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        var matches = result.Diagnostics
            .Where(diagnostic => diagnostic.Id == id)
            .ToArray();

        Assert.HasCount(1, matches, GeneratorProbe.Describe(result.Diagnostics));
        return matches[0];
    }
}
