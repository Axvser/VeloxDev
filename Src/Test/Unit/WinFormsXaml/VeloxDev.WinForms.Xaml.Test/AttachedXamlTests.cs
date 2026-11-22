using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Attached properties written in XAML: literal values, static resources and bindings on native controls.
/// </summary>
[STATestClass]
public sealed class AttachedXamlTests
{
    private const string Source = """
        using System.Windows.Forms;
        using VeloxDev.WinForms.Xaml;

        namespace Probe;

        public static class WorkflowPanel
        {
            public static readonly DependencyProperty ColumnProperty =
                DependencyProperty.RegisterAttached("Column", typeof(int), typeof(WorkflowPanel), new PropertyMetadata(0));

            public static readonly DependencyProperty LabelProperty =
                DependencyProperty.RegisterAttached("Label", typeof(string), typeof(WorkflowPanel));

            public static int GetColumn(System.Windows.Forms.Control control) => (int)ColumnProperty.GetValue(control)!;
            public static void SetColumn(System.Windows.Forms.Control control, int value) => ColumnProperty.SetValue(control, value);
            public static string? GetLabel(System.Windows.Forms.Control control) => (string?)LabelProperty.GetValue(control);
            public static void SetLabel(System.Windows.Forms.Control control, string? value) => LabelProperty.SetValue(control, value);
        }

        public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
        {
        }
        """;

    private const string Xaml = """
        <local:ProbeView
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:local="clr-namespace:Probe"
            xmlns:native="clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms"
            x:Class="Probe.ProbeView">
          <local:ProbeView.Resources>
            <ResourceDictionary>
              <x:String x:Key="SomeKey">resource label</x:String>
            </ResourceDictionary>
          </local:ProbeView.Resources>

          <native:Button x:Name="button"
                         local:WorkflowPanel.Column="2"
                         local:WorkflowPanel.Label="{StaticResource SomeKey}" />

          <native:Label x:Name="label"
                        local:WorkflowPanel.Column="{Binding Column}" />
        </local:ProbeView>
        """;

    [TestMethod]
    public void AttachedAttributes_EmitLiteralResourceAndBinding()
    {
        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            Source,
            "AttachedXamlProbe",
            new InMemoryAdditionalText("ProbeView.xaml", Xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "global::VeloxDev.WinForms.Xaml.XamlRuntime.SetAttachedValue(button, global::Probe.WorkflowPanel.ColumnProperty, 2);");
        StringAssert.Contains(
            generated,
            "global::VeloxDev.WinForms.Xaml.XamlRuntime.SetAttachedValue(button, global::Probe.WorkflowPanel.LabelProperty, global::VeloxDev.WinForms.Xaml.XamlRuntime.GetResource<object>(this, \"SomeKey\"));");
        StringAssert.Contains(
            generated,
            "this.SetAttachedBinding(label, global::Probe.WorkflowPanel.ColumnProperty, new global::VeloxDev.WinForms.Xaml.Binding(\"Column\"));");
    }

    [TestMethod]
    public void AttachedLiteralAndStaticResource_ReachANativeControl()
    {
        using var view = new GeneratedView();
        var button = view.Controls.OfType<Button>().Single();

        Assert.AreEqual(2, WorkflowPanel.GetColumn(button));
        Assert.AreEqual("caption from a static resource", WorkflowPanel.GetLabel(button));
        Assert.AreEqual("xaml note", GeneratedAttachments.GetNote(button));
    }

    [TestMethod]
    public void AttachedBinding_OneWayAndOneTime_FollowTheDataContext()
    {
        using var view = new GeneratedView();
        var label = view.Controls.OfType<Label>().Single();
        view.BindingContext = new BindingContext();
        var model = new GeneratedModel { Column = 5, Title = "one-time title" };

        view.DataContext = model;

        Assert.AreEqual(5, WorkflowPanel.GetColumn(label));
        Assert.AreEqual("one-time title", WorkflowPanel.GetLabel(label));

        model.Column = 9;
        model.Title = "changed title";

        Assert.AreEqual(9, WorkflowPanel.GetColumn(label));
        Assert.AreEqual(
            "one-time title",
            WorkflowPanel.GetLabel(label),
            "a OneTime attached binding must not follow later source changes");
    }

    [TestMethod]
    public void AttachedTwoWayBinding_DoesNotWriteBackFromANativeValue()
    {
        using var host = new VeloxDev.WinForms.Xaml.Controls.Control();
        using var button = new Button();
        var model = new GeneratedModel { Column = 3 };
        host.DataContext = model;

        host.SetAttachedBinding(
            button,
            WorkflowPanel.ColumnProperty,
            new Binding("Column") { Mode = BindingMode.TwoWay });

        Assert.AreEqual(3, WorkflowPanel.GetColumn(button));

        // 原生控件写入没有变更管线；TwoWay 在附加值上按 OneWay 处理。
        WorkflowPanel.SetColumn(button, 42);
        Assert.AreEqual(3, model.Column);

        model.Column = 8;
        Assert.AreEqual(8, WorkflowPanel.GetColumn(button));
    }
}
