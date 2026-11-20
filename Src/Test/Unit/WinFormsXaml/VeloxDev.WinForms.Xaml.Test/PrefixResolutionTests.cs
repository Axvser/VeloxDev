using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Element type resolution: <c>v:</c> maps to the adapter namespace, <c>local:</c> to the template namespace, and an
/// unprefixed name prefers the adapter, requires an explicit prefix when local also defines it, and otherwise falls
/// back to the native WinForms table.
/// </summary>
[TestClass]
public sealed class PrefixResolutionTests
{
    private const string Source = """
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public partial class ProbeCard : VeloxDev.WinForms.Xaml.Controls.Control
        {
        }

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
        {
        }
        """;

    [TestMethod]
    public void UnprefixedControl_PrefersTheAdapterOverTheNativeType()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <Control x:Name="xamlHost" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixAdapterFirst", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Controls.Control xamlHost = null!;");
    }

    [TestMethod]
    public void UnprefixedNativeName_ResolvesToTheWinFormsType()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <Button x:Name="nativeButton" Text="OK" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixNativeFallback", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::System.Windows.Forms.Button nativeButton = null!;");
    }

    [TestMethod]
    public void ExplicitVPrefix_ResolvesAnAdapterControl()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <v:Panel x:Name="panel" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixExplicitV", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Controls.Panel panel = null!;");
    }

    [TestMethod]
    public void UnprefixedLocalName_ResolvesWhenTheAdapterDoesNotDefineIt()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <ProbeCard x:Name="localCard" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixLocalWinner", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::Probe.ProbeCard localCard = null!;");
    }

    [TestMethod]
    public void UnprefixedRoot_ResolvesTheLocalClass()
    {
        const string xaml = """
            <ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="Probe.ProbeView">
              <ProbeCard x:Name="localCard" />
            </ProbeView>
            """;

        var (run, output) = Run("PrefixUnprefixedRoot", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(run.GeneratedText(), "private void InitializeComponent()");
        StringAssert.Contains(run.GeneratedText(), "private global::Probe.ProbeCard localCard = null!;");
    }

    [TestMethod]
    public void UnprefixedNameDefinedByBothVAndLocal_IsAmbiguous()
    {
        const string source = """
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            // A local type with the same simple name as an adapter base control.
            public class Control : System.Windows.Forms.Control
            {
            }

            public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
            {
            }
            """;

        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <Control />
              <local:Control x:Name="localControl" />
            </local:ProbeView>
            """;

        var result = GeneratorProbe.Run(
            new XamlFileGenerator(),
            source,
            "PrefixAmbiguous",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        var diagnostic = AssertSingle(result, "VELOX_XAML018");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "ambiguous");
        StringAssert.Contains(diagnostic.GetMessage(), "v:Control");

        // An explicit prefix picks one side even while the unprefixed name is ambiguous.
        StringAssert.Contains(
            result.GeneratedText(),
            "private global::Probe.Control localControl = null!;");
    }

    private static (Microsoft.CodeAnalysis.GeneratorDriverRunResult Run, Compilation Output) Run(
        string assemblyName,
        string source,
        string xaml)
        => GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            assemblyName,
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

    private static Diagnostic[] Errors(Compilation compilation)
        => compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

    private static Diagnostic AssertSingle(GeneratorDriverRunResult result, string id)
    {
        var matches = result.Diagnostics.Where(diagnostic => diagnostic.Id == id).ToArray();
        Assert.HasCount(1, matches, GeneratorProbe.Describe(result.Diagnostics));
        return matches[0];
    }
}
