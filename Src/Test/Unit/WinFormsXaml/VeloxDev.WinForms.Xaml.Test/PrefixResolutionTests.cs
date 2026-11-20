using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Element type resolution is prefix-strict: <c>v:</c> maps to the adapter, <c>local:</c> to the template namespace,
/// and an unprefixed name means native WinForms only. A VeloxDev type written without <c>v:</c> is an error, even when
/// the native table has a same-named type.
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
    public void UnprefixedControl_ResolvesToTheNativeType()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <Control x:Name="nativeHost" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixNativeControl", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::System.Windows.Forms.Control nativeHost = null!;");
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
    public void UnprefixedAdapterOnlyName_IsAnError()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <StackPanel x:Name="stack" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixAdapterWithoutV", Source, xaml);

        Assert.IsNotNull(output);
        var diagnostic = AssertSingle(run, "VELOX_XAML019");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "v:StackPanel");
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
              <v:StackPanel x:Name="stack" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixExplicitV", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Controls.StackPanel stack = null!;");
    }

    [TestMethod]
    public void ExplicitVShapePrefix_ResolvesTheShapeNamespace()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <v:Rectangle x:Name="shape" Width="40" Height="20" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixShape", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Controls.Shapes.Rectangle shape = null!;");
    }

    [TestMethod]
    public void ExplicitLocalPrefix_ResolvesTheTemplateType()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <local:ProbeCard x:Name="localCard" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixExplicitLocal", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::Probe.ProbeCard localCard = null!;");
    }

    [TestMethod]
    public void UnprefixedRoot_IsNoLongerImplicitLocal()
    {
        const string xaml = """
            <ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <local:ProbeCard x:Name="localCard" />
            </ProbeView>
            """;

        var (run, _) = Run("PrefixUnprefixedRoot", Source, xaml);

        var diagnostic = AssertSingle(run, "VELOX_XAML019");
        StringAssert.Contains(diagnostic.GetMessage(), "ProbeView");
    }

    [TestMethod]
    public void UnprefixedName_IsNotDisambiguatedAgainstLocal()
    {
        const string source = """
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            // A local type with the same simple name as a native WinForms type.
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
              <Control x:Name="nativeControl" />
              <local:Control x:Name="localControl" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixStrict", source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::System.Windows.Forms.Control nativeControl = null!;");
        StringAssert.Contains(
            run.GeneratedText(),
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
