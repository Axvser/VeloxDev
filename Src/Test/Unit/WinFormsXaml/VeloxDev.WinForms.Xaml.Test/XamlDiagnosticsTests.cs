using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The documented <c>VELOX_XAML*</c> diagnostics. Every case is driven through the generator, because the malformed
/// files never reach a compiled output.
/// </summary>
[TestClass]
public sealed class XamlDiagnosticsTests
{
    private const string Source = """
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
        {
        }
        """;

    private const string NonPartialSource = """
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
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
    public void AnUnknownElement_IsXaml001()
    {
        var diagnostic = AssertSingle(Wrap("<MissingControl />"), "VELOX_XAML001");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "MissingControl");
    }

    [TestMethod]
    public void AnUnknownMember_IsXaml002()
    {
        var diagnostic = AssertSingle(Wrap("<Button Bogus=\"1\" />"), "VELOX_XAML002");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Bogus");
    }

    [TestMethod]
    public void AMissingXClass_IsXaml005()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe">
              <Button Text="OK" />
            </local:ProbeView>
            """;

        var diagnostic = AssertSingle(xaml, "VELOX_XAML005");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [TestMethod]
    public void AnXClassThatDoesNotExist_IsXaml006()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.DoesNotExist">
              <Button Text="OK" />
            </local:ProbeView>
            """;

        var diagnostic = AssertSingle(xaml, "VELOX_XAML006");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "Probe.DoesNotExist");
    }

    [TestMethod]
    public void ADuplicateXName_IsXaml007()
    {
        var diagnostic = AssertSingle(
            Wrap("<Button x:Name=\"duplicate\" /><Label x:Name=\"duplicate\" />"),
            "VELOX_XAML007");
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "duplicate");
    }

    [TestMethod]
    public void AMissingEventHandler_IsXaml008()
    {
        var diagnostic = AssertSingle(Wrap("<Button Click=\"DoesNotExist\" />"), "VELOX_XAML008");
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "DoesNotExist");
    }

    [TestMethod]
    public void ANonPartialXClass_IsXaml009()
    {
        var result = Run(Wrap("<Button Text=\"OK\" />"), NonPartialSource);
        var diagnostic = AssertSingle(result, "VELOX_XAML009");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.IsEmpty(result.GeneratedText());
    }

    [TestMethod]
    public void MalformedXml_IsXaml012()
    {
        var result = Run("<local:ProbeView", Source);
        var diagnostic = AssertSingle(result, "VELOX_XAML012");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.IsEmpty(result.GeneratedText());
    }

    [TestMethod]
    public void AnExistingInitializeComponent_IsXaml015()
    {
        const string source = """
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
            {
                private void InitializeComponent() { }
            }
            """;

        var result = Run(Wrap("<Button Text=\"OK\" />"), source);
        var diagnostic = AssertSingle(result, "VELOX_XAML015");

        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "InitializeComponent");
        Assert.IsEmpty(result.GeneratedText());
    }

    [TestMethod]
    public void APartialInitializeComponentDeclaration_IsXaml015()
    {
        const string source = """
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
            {
                private partial void InitializeComponent();
            }
            """;

        var result = Run(Wrap("<Button Text=\"OK\" />"), source);
        AssertSingle(result, "VELOX_XAML015");
        Assert.IsEmpty(result.GeneratedText());
    }

    private static string Wrap(string body) => OpenRoot + "\n" + body + "\n" + CloseRoot;

    private static Diagnostic AssertSingle(string xaml, string id) => AssertSingle(Run(xaml, Source), id);

    private static Diagnostic AssertSingle(GeneratorDriverRunResult result, string id)
    {
        var matches = result.Diagnostics
            .Where(diagnostic => diagnostic.Id == id)
            .ToArray();

        Assert.HasCount(1, matches, GeneratorProbe.Describe(result.Diagnostics));
        return matches[0];
    }

    private static GeneratorDriverRunResult Run(string xaml, string source)
        => GeneratorProbe.Run(
            new XamlFileGenerator(),
            source,
            "XamlDiagnosticsProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));
}
