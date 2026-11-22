using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Element type resolution is prefix-strict: unprefixed names mean VeloxDev, <c>native:</c> means
/// <see cref="System.Windows.Forms"/>, and <c>local:</c> means the template namespace. A legacy <c>v:</c> keeps
/// meaning VeloxDev, but nothing in the repo needs it any more.
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

    private const string UserControlSource = """
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.UserControl
        {
        }
        """;

    [TestMethod]
    public void UnprefixedRootAndControl_ResolveToVeloxDev()
    {
        const string xaml = """
            <UserControl
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="Probe.ProbeView">
              <Border x:Name="frame" />
            </UserControl>
            """;

        var (run, output) = Run("PrefixUnprefixedVeloxDev", UserControlSource, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Controls.Border frame = null!;");
    }

    [TestMethod]
    public void UnprefixedShape_ResolvesThroughTheShapesNamespace()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <Rectangle x:Name="shape" Width="40" Height="20" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixUnprefixedShape", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Controls.Shapes.Rectangle shape = null!;");
    }

    [TestMethod]
    public void NativePrefix_ResolvesTheWinFormsType()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                xmlns:native="clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms"
                x:Class="Probe.ProbeView">
              <native:Label x:Name="nativeLabel" Text="OK" />
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixNative", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::System.Windows.Forms.Label nativeLabel = null!;");
    }

    [TestMethod]
    public void UnknownUnprefixedType_IsXaml019()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <MissingThing />
            </local:ProbeView>
            """;

        var (run, _) = Run("PrefixUnknownUnprefixed", Source, xaml);

        var diagnostic = AssertSingle(run, "VELOX_XAML019");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "native:MissingThing");
        StringAssert.Contains(diagnostic.GetMessage(), "local:MissingThing");
    }

    [TestMethod]
    public void LegacyVPrefix_StillResolvesAVeloxDevControl()
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

        var (run, output) = Run("PrefixLegacyV", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "private global::VeloxDev.WinForms.Xaml.Controls.StackPanel stack = null!;");
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
    public void UnprefixedStyleTargetType_IsAVeloxDevControl()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ProbeView">
              <local:ProbeView.Resources>
                <ResourceDictionary>
                  <Style x:Key="ButtonStyle" TargetType="Button">
                    <Style.Setters>
                      <Setter Property="Content" Value="ok" />
                    </Style.Setters>
                  </Style>
                </ResourceDictionary>
              </local:ProbeView.Resources>
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixVeloxDevStyleTarget", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "TargetType = typeof(global::VeloxDev.WinForms.Xaml.Controls.Button);");
    }

    [TestMethod]
    public void NativeStyleTargetType_IsAWinFormsControl()
    {
        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                xmlns:native="clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms"
                x:Class="Probe.ProbeView">
              <local:ProbeView.Resources>
                <ResourceDictionary>
                  <Style x:Key="NativeButtonStyle" TargetType="native:Button">
                    <Style.Setters>
                      <Setter Property="Text" Value="ok" />
                    </Style.Setters>
                  </Style>
                </ResourceDictionary>
              </local:ProbeView.Resources>
            </local:ProbeView>
            """;

        var (run, output) = Run("PrefixNativeStyleTarget", Source, xaml);

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(Errors(output), GeneratorProbe.Describe(Errors(output)));
        StringAssert.Contains(
            run.GeneratedText(),
            "TargetType = typeof(global::System.Windows.Forms.Button);");
        StringAssert.Contains(run.GeneratedText(), "new global::VeloxDev.WinForms.Xaml.Setter(\"Text\", \"ok\")");
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
