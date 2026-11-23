using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Drives <see cref="XamlFileGenerator"/> over an in-memory XAML file against the visual backend: unprefixed controls,
/// a <c>local:</c> derived type, names, properties, events, bindings, attached properties, static resources and a
/// data template.
/// </summary>
[TestClass]
public sealed class XamlCompilationTests
{
    private const string Source = """
        using System;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public partial class ProbeCard : VeloxDev.WinForms.Xaml.Visuals.Controls.Border
        {
            public static readonly DependencyProperty TitleProperty =
                DependencyProperty.Register(nameof(Title), typeof(string), typeof(ProbeCard));

            public string? Title
            {
                get => (string?)GetValue(TitleProperty);
                set => SetValue(TitleProperty, value);
            }
        }

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
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
            x:Class="Probe.ProbeView"
            Orientation="Horizontal"
            Spacing="4"
            Padding="6">

          <local:ProbeView.Resources>
            <x:Color x:Key="Accent">#FF102030</x:Color>
            <DataTemplate x:Key="CardTemplate">
              <local:ProbeCard Title="{Binding Title}" />
            </DataTemplate>
          </local:ProbeView.Resources>

          <Border x:Name="frame" Grid.Row="1" Background="{StaticResource Accent}" Width="80" Height="40" />

          <TextBlock x:Name="caption" Text="{Binding Caption}" Width="200" />

          <local:ProbeCard x:Name="card" Title="{Binding Title}" />

          <Button x:Name="probeButton" Text="Probe" Click="OnProbeClick" />

        </local:ProbeView>
        """;

    [TestMethod]
    public void AFullVisualXamlFile_EmitsACompilableInitializeComponent()
    {
        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "VisualXamlCompilationProbe",
            new InMemoryAdditionalText("ProbeView.xaml", Xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "private void InitializeComponent()");

        // x:Name becomes a field; the children are added with Add, never Controls.Add.
        StringAssert.Contains(generated, "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.Border frame = null!;");
        StringAssert.Contains(generated, "private global::Probe.ProbeCard card = null!;");
        StringAssert.Contains(generated, "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.Button probeButton = null!;");
        StringAssert.Contains(generated, "this.Add(frame);");
        StringAssert.Contains(generated, "this.Add(probeButton);");
        Assert.IsFalse(generated.Contains(".Controls.Add", StringComparison.Ordinal), "visual children must not use Controls");

        // Attached property, events, static resources and the data template resource.
        StringAssert.Contains(
            generated,
            "frame.SetValue(global::VeloxDev.WinForms.Xaml.Visuals.Controls.Grid.RowProperty, 1);");
        StringAssert.Contains(generated, "probeButton.Click += this.OnProbeClick;");
        StringAssert.Contains(generated, "VisualBindingOperations.Bind(");
        StringAssert.Contains(generated, "this.Resources[\"Accent\"] =");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Visuals.DataTemplate(");
    }

    [TestMethod]
    public void ThicknessLiterals_CompileOnVisualPaddingAndBorderThickness()
    {
        const string source = """
            namespace Probe2;

            public partial class ThicknessView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
            }
            """;

        const string xaml = """
            <local:ThicknessView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe2"
                x:Class="Probe2.ThicknessView">

              <Border x:Name="card"
                        Padding="1,2,3,4"
                        BorderThickness="8"
                        CornerRadius="6" />

            </local:ThicknessView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualThicknessLiteralProbe",
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
        StringAssert.Contains(generated, "card.CornerRadius = 6F;");
    }

    [TestMethod]
    public void VisualScrollViewer_CompilesWithContent()
    {
        const string source = """
            namespace Probe3;

            public partial class ScrollHostView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
            }
            """;

        const string xaml = """
            <local:ScrollHostView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe3"
                x:Class="Probe3.ScrollHostView">

              <ScrollViewer x:Name="viewer" IsScrollInputEnabled="False">
                <Canvas x:Name="content" />
              </ScrollViewer>

            </local:ScrollHostView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualScrollViewerProbe",
            new InMemoryAdditionalText("ScrollHostView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "private global::VeloxDev.WinForms.Xaml.Visuals.Controls.ScrollViewer viewer = null!;");
        StringAssert.Contains(generated, "viewer.IsScrollInputEnabled = false;");
        StringAssert.Contains(generated, "viewer.Content = content;");
    }

    [TestMethod]
    public void TheGeneratorEmitsAPrivateNonPartialInitializeComponent()
    {
        const string source = """
            namespace Probe;

            public partial class BareView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
            }
            """;

        const string xaml = """
            <local:BareView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.BareView">
              <Button x:Name="okButton" Text="OK" />
            </local:BareView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualXamlBareProbe",
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

    [TestMethod]
    public void InlineContentTemplates_Compile()
    {
        const string source = """
            namespace Probe;

            public partial class ContentTemplateView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
            }
            """;

        const string xaml = """
            <local:ContentTemplateView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                x:Class="Probe.ContentTemplateView">

              <ContentControl x:Name="host">
                <ContentControl.ContentTemplate>
                  <DataTemplate>
                    <TextBlock Text="{Binding Name}" />
                  </DataTemplate>
                </ContentControl.ContentTemplate>
              </ContentControl>

              <ContentPresenter x:Name="presenter">
                <ContentPresenter.ContentTemplate>
                  <DataTemplate>
                    <Border />
                  </DataTemplate>
                </ContentPresenter.ContentTemplate>
              </ContentPresenter>

            </local:ContentTemplateView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualContentTemplateProbe",
            new InMemoryAdditionalText("ContentTemplateView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "host.ContentTemplate = new global::VeloxDev.WinForms.Xaml.Visuals.DataTemplate(");
        StringAssert.Contains(
            generated,
            "presenter.ContentTemplate = new global::VeloxDev.WinForms.Xaml.Visuals.DataTemplate(");
    }
}
