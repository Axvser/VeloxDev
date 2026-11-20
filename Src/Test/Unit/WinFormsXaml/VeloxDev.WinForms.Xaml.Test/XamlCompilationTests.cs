using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Drives <see cref="XamlFileGenerator"/> over an in-memory XAML file: the subset covers native controls, a derived
/// control, names, properties, events, bindings, static resources and a data template.
/// </summary>
[TestClass]
public sealed class XamlCompilationTests
{
    private const string Source = """
        using System;
        using System.Drawing;
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public partial class ProbeCard : VeloxDev.WinForms.Xaml.Controls.Control
        {
            public static readonly DependencyProperty TitleProperty =
                DependencyProperty.Register(nameof(Title), typeof(string), typeof(ProbeCard));

            public string Title
            {
                get => (string)GetValue(TitleProperty)!;
                set => SetValue(TitleProperty, value);
            }
        }

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
        {
            public int Clicks { get; private set; }


            private void OnProbeClick(object? sender, EventArgs e) => Clicks++;
        }
        """;

    private const string Xaml = """
        <local:ProbeView
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:local="clr-namespace:Probe"
            xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
            x:Class="Probe.ProbeView"
            Size="300, 200">

          <local:ProbeView.Resources>
            <v:ResourceDictionary>
              <x:Color x:Key="Accent">#FF102030</x:Color>
              <v:DataTemplate x:Key="CardTemplate">
                <local:ProbeCard Title="{Binding Title}" />
              </v:DataTemplate>
            </v:ResourceDictionary>
          </local:ProbeView.Resources>

          <Label x:Name="captionLabel" Text="{Binding Caption}" Size="200, 24" />

          <local:ProbeCard x:Name="card"
                           Title="{Binding Title}"
                           BackColor="{StaticResource Accent}"
                           Size="200, 80" />

          <Button x:Name="probeButton" Text="Probe" Click="OnProbeClick" />

        </local:ProbeView>
        """;

    [TestMethod]
    public void AFullXamlFile_EmitsACompilableInitializeComponent()
    {
        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "XamlCompilationProbe",
            new InMemoryAdditionalText("ProbeView.xaml", Xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "private void InitializeComponent()");
        StringAssert.Contains(generated, "this.SuspendLayout();");
        StringAssert.Contains(generated, "this.ResumeLayout(false);");

        // x:Name 变成字段，且字段与局部变量同名。
        StringAssert.Contains(generated, "private global::System.Windows.Forms.Label captionLabel = null!;");
        StringAssert.Contains(generated, "private global::Probe.ProbeCard card = null!;");
        StringAssert.Contains(generated, "private global::System.Windows.Forms.Button probeButton = null!;");
        StringAssert.Contains(generated, "this.captionLabel = captionLabel;");
        StringAssert.Contains(generated, "this.Controls.Add(probeButton);");

        // 原生属性、DP 绑定、原生绑定、事件与静态资源。
        StringAssert.Contains(generated, "captionLabel.Size = global::VeloxDev.WinForms.Xaml.XamlRuntime.ParseSize(\"200, 24\");");
        StringAssert.Contains(generated, "card.SetBinding(global::Probe.ProbeCard.TitleProperty, new global::VeloxDev.WinForms.Xaml.Binding(\"Title\"));");
        StringAssert.Contains(generated, "this.SetNativeBinding(captionLabel, \"Text\", new global::VeloxDev.WinForms.Xaml.Binding(\"Caption\"));");
        StringAssert.Contains(generated, "probeButton.Click += this.OnProbeClick;");
        StringAssert.Contains(
            generated,
            "card.BackColor = global::VeloxDev.WinForms.Xaml.XamlRuntime.GetResource<global::System.Drawing.Color>(this, \"Accent\");");

        // 资源与 DataTemplate 工厂。
        StringAssert.Contains(generated, "this.Resources[\"Accent\"] = global::VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor(\"#FF102030\");");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.DataTemplate(this.CreateTemplate_CardTemplate)");
        StringAssert.Contains(generated, "private global::System.Windows.Forms.Control CreateTemplate_CardTemplate()");
        StringAssert.Contains(generated, "return __xaml1;");
    }

    [TestMethod]
    public void ThicknessLiterals_CompileOnPaddingAndBorderThickness()
    {
        const string source = """
            namespace Probe2;

            public partial class ThicknessView : VeloxDev.WinForms.Xaml.Controls.Control
            {
            }
            """;

        const string xaml = """
            <local:ThicknessView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe2"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
                x:Class="Probe2.ThicknessView"
                Size="200, 120">

              <v:Border x:Name="card"
                        Padding="1,2,3,4"
                        BorderThickness="8"
                        CornerRadius="6"
                        Location="0, 0"
                        Size="100, 100" />

            </local:ThicknessView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "ThicknessLiteralProbe",
            new InMemoryAdditionalText("ThicknessView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "card.Padding = global::VeloxDev.WinForms.Xaml.XamlRuntime.ParseThickness(\"1,2,3,4\");");
        StringAssert.Contains(
            generated,
            "card.BorderThickness = global::VeloxDev.WinForms.Xaml.XamlRuntime.ParseThickness(\"8\");");
    }

    [TestMethod]
    public void VScrollViewer_CompilesWithScrollBarProperties()
    {
        const string source = """
            namespace Probe3;

            public partial class ScrollHostView : VeloxDev.WinForms.Xaml.Controls.Control
            {
            }
            """;

        const string xaml = """
            <local:ScrollHostView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe3"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
                x:Class="Probe3.ScrollHostView"
                Size="400, 300">

              <v:ScrollViewer x:Name="viewer"
                              HorizontalScrollBarVisibility="Visible"
                              VerticalScrollBarVisibility="Hidden"
                              ExtentWidth="2000"
                              ExtentHeight="1200"
                              ViewportWidth="400"
                              ViewportHeight="300"
                              ScrollOffset="10, 20">
                <v:Canvas x:Name="content" />
              </v:ScrollViewer>

            </local:ScrollHostView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "ScrollViewerProbe",
            new InMemoryAdditionalText("ScrollHostView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "private global::VeloxDev.WinForms.Xaml.Controls.ScrollViewer viewer = null!;");
        StringAssert.Contains(
            generated,
            "viewer.HorizontalScrollBarVisibility = global::VeloxDev.WinForms.Xaml.Controls.ScrollBarVisibility.Visible;");
        StringAssert.Contains(
            generated,
            "viewer.VerticalScrollBarVisibility = global::VeloxDev.WinForms.Xaml.Controls.ScrollBarVisibility.Hidden;");
        StringAssert.Contains(generated, "viewer.ExtentWidth = 2000D;");
        StringAssert.Contains(generated, "viewer.ExtentHeight = 1200D;");
        StringAssert.Contains(generated, "viewer.ViewportWidth = 400D;");
        StringAssert.Contains(generated, "viewer.ViewportHeight = 300D;");
        StringAssert.Contains(
            generated,
            "viewer.ScrollOffset = global::VeloxDev.WinForms.Xaml.XamlRuntime.ParsePoint(\"10, 20\");");
        StringAssert.Contains(generated, "viewer.Controls.Add(content);");
    }

    [TestMethod]
    public void TheGeneratorEmitsAPrivateNonPartialInitializeComponent()
    {
        const string source = """
            using System.Windows.Forms;
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public partial class BareView : VeloxDev.WinForms.Xaml.Controls.Control
            {
            }
            """;

        const string xaml = """
            <local:BareView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
                x:Class="Probe.BareView">
              <Button x:Name="okButton" Text="OK" />
            </local:BareView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "XamlBareProbe",
            new InMemoryAdditionalText("BareView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "private void InitializeComponent()");
        Assert.IsFalse(
            generated.Contains("partial void InitializeComponent", StringComparison.Ordinal),
            "InitializeComponent must be an ordinary private method, not a partial method");

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));
    }
}
