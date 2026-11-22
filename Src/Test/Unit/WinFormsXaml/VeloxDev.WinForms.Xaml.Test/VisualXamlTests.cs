using System.Drawing;
using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The visual-backend XAML profile: a <c>v:*</c> root derives from a <see cref="VisualElement"/>, children are added
/// with <c>Add</c>, properties stay literal CLR values and attached properties write the dependency property directly.
/// </summary>
[STATestClass]
public sealed class VisualXamlTests
{
    [TestMethod]
    public void VisualView_BuildsTheVisualTreeAndProperties()
    {
        using var view = new VisualView();

        Assert.HasCount(3, view.Children);
        var frame = Assert.IsInstanceOfType<VC.Border>(view.Children[0]);
        var title = Assert.IsInstanceOfType<VC.TextBlock>(view.Children[1]);
        var button = Assert.IsInstanceOfType<VC.Button>(view.Children[2]);

        Assert.AreSame(view.Frame, frame);
        Assert.AreSame(view.Title, title);
        Assert.AreSame(view.ActionButton, button);

        Assert.AreEqual(Orientation.Horizontal, view.Orientation);
        Assert.AreEqual(4, view.Spacing);
        Assert.AreEqual(6f, view.Padding.Left);

        Assert.AreEqual(80d, frame.Width);
        Assert.AreEqual(40d, frame.Height);
        Assert.AreEqual(2f, frame.BorderThickness.Left);
        Assert.AreEqual(6f, frame.CornerRadius);
        Assert.IsNotNull(frame.Background);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x10, 0x20, 0x30), frame.Background!.ApproximateColor);
        Assert.AreEqual(1, VC.Grid.GetRow(frame));

        Assert.AreEqual("Visual title", title.Text);
        Assert.IsTrue(title.Wrap);
        Assert.AreEqual(120d, title.Width);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x20, 0x30, 0x40), title.Foreground);

        Assert.AreEqual("Run", button.Text);
    }

    [TestMethod]
    public void VisualView_WiresTheGeneratedClickHandler()
    {
        using var view = new VisualView();
        using var surface = new VisualSurface { Size = new Size(320, 200) };
        surface.Root = view;
        Layout(surface, 320, 200);

        var bounds = view.ActionButton.AbsoluteBounds;
        var center = new PointF(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
        surface.ProcessPointerDown(center, MouseButtons.Left);
        surface.ProcessPointerUp(center, MouseButtons.Left);

        Assert.AreEqual(1, view.ActionClicks);
    }

    [TestMethod]
    public void VisualXaml_UsesAddAndLiteralProperties()
    {
        const string source = """
            namespace Probe;

            public partial class ProbeVisualView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
                private void OnProbeClick(object? sender, System.EventArgs e) { }
            }
            """;

        const string xaml = """
            <v:StackPanel
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml.Visuals.Controls;assembly=VeloxDev.WinForms"
                x:Class="Probe.ProbeVisualView"
                Orientation="Horizontal"
                Spacing="4">
              <v:Border x:Name="frame" v:Grid.Row="1" Background="#FF102030" BorderThickness="2" />
              <v:TextBlock x:Name="title" Text="Visual title" Wrap="True" Width="120" Foreground="#FF203040" />
              <v:Button x:Name="actionButton" Text="Run" Click="OnProbeClick" />
            </v:StackPanel>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualXamlProbe",
            new InMemoryAdditionalText("ProbeVisualView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "private void InitializeComponent()");
        StringAssert.Contains(generated, "this.Add(frame);");
        StringAssert.Contains(generated, "this.Add(title);");
        StringAssert.Contains(generated, "this.Add(actionButton);");
        Assert.IsFalse(generated.Contains(".Controls.Add", StringComparison.Ordinal), "visual children must not use Controls");
        Assert.IsFalse(generated.Contains("NameScope", StringComparison.Ordinal), "visual elements have no NameScope");

        StringAssert.Contains(generated, "frame.SetValue(global::VeloxDev.WinForms.Xaml.Visuals.Controls.Grid.RowProperty, 1);");
        StringAssert.Contains(generated, "title.Text = \"Visual title\";");
        StringAssert.Contains(generated, "title.Wrap = true;");
        StringAssert.Contains(generated, "actionButton.Click += this.OnProbeClick;");
    }

    [TestMethod]
    public void VisualXaml_MarkupExtensionsAreReported()
    {
        const string source = """
            namespace Probe;

            public partial class ProbeVisualView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
            }
            """;

        const string xaml = """
            <v:StackPanel
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml.Visuals.Controls;assembly=VeloxDev.WinForms"
                x:Class="Probe.ProbeVisualView">
              <v:TextBlock Text="{Binding Title}" />
              <v:Border Background="{StaticResource Accent}" />
            </v:StackPanel>
            """;

        var (run, _) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualXamlMarkupProbe",
            new InMemoryAdditionalText("ProbeVisualView.xaml", xaml));

        var diagnostics = run.Diagnostics
            .Where(static diagnostic => diagnostic.Id == "VELOX_XAML004")
            .ToArray();
        Assert.HasCount(2, diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        foreach (var diagnostic in diagnostics)
        {
            StringAssert.Contains(diagnostic.GetMessage(), "visual backend");
        }
    }

    [TestMethod]
    public void VisualXaml_ResourcesAreReported()
    {
        const string source = """
            namespace Probe;

            public partial class ProbeVisualView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
            }
            """;

        const string xaml = """
            <v:StackPanel
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml.Visuals.Controls;assembly=VeloxDev.WinForms"
                x:Class="Probe.ProbeVisualView">
              <v:StackPanel.Resources>
                <x:String x:Key="Accent">#FF102030</x:String>
              </v:StackPanel.Resources>
            </v:StackPanel>
            """;

        var (run, _) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualXamlResourceProbe",
            new InMemoryAdditionalText("ProbeVisualView.xaml", xaml));

        var diagnostic = AssertSingle(run, "VELOX_XAML010");
        StringAssert.Contains(diagnostic.GetMessage(), "cannot own resources");
    }

    private static Diagnostic AssertSingle(GeneratorDriverRunResult result, string id)
    {
        var matches = result.Diagnostics.Where(diagnostic => diagnostic.Id == id).ToArray();
        Assert.HasCount(1, matches, GeneratorProbe.Describe(result.Diagnostics));
        return matches[0];
    }

    private static void Layout(VisualSurface surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }
}
