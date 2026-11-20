using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Drives <see cref="DependencyPropertyGenerator"/> directly, so the emitted members and the refusal diagnostics can
/// be asserted without a compiler round trip.
/// </summary>
[TestClass]
public sealed class SourceGeneratorTests
{
    [TestMethod]
    public void XamlProperty_EmitsAFieldAndAClrWrapper()
    {
        var result = GeneratorProbe.Run(new DependencyPropertyGenerator(), """
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public partial class Card : VeloxDev.WinForms.Xaml.Controls.Control
            {
                [XamlProperty]
                public partial string Title { get; set; }
            }
            """, "XamlPropertyProbe");

        Assert.IsEmpty(result.Diagnostics, GeneratorProbe.Describe(result.Diagnostics));

        var generated = result.GeneratedText();
        StringAssert.Contains(generated, "public static readonly global::VeloxDev.WinForms.Xaml.DependencyProperty TitleProperty =");
        StringAssert.Contains(generated, "global::VeloxDev.WinForms.Xaml.DependencyProperty.Register(");
        StringAssert.Contains(generated, "\"Title\"");
        StringAssert.Contains(generated, "typeof(string)");
        StringAssert.Contains(generated, "typeof(global::Probe.Card)");
        StringAssert.Contains(generated, "public partial global::System.String Title");
        StringAssert.Contains(generated, "get => (global::System.String)GetValue(TitleProperty)!;");
        StringAssert.Contains(generated, "set => SetValue(TitleProperty, value);");
    }

    [TestMethod]
    public void XamlProperty_ExplicitNameAndDefaultValue_ReachTheField()
    {
        var result = GeneratorProbe.Run(new DependencyPropertyGenerator(), """
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public partial class Card : VeloxDev.WinForms.Xaml.Controls.Control
            {
                [XamlProperty("Caption", DefaultValue = "hello")]
                public partial string Text { get; set; }
            }
            """, "XamlPropertyDefaultProbe");

        Assert.IsEmpty(result.Diagnostics, GeneratorProbe.Describe(result.Diagnostics));

        var generated = result.GeneratedText();
        StringAssert.Contains(generated, "DependencyProperty CaptionProperty =");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.PropertyMetadata(\"hello\")");
        StringAssert.Contains(generated, "public partial global::System.String Text");
    }

    [TestMethod]
    public void XamlAttachedProperty_EmitsRegisterAttachedAndBothAccessors()
    {
        var result = GeneratorProbe.Run(new DependencyPropertyGenerator(), """
            using System.Windows.Forms;
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public static partial class Attachments
            {
                [XamlAttachedProperty]
                public static partial string? GetNote(System.Windows.Forms.Control control);

                [XamlAttachedProperty]
                public static partial void SetNote(System.Windows.Forms.Control control, string? value);
            }
            """, "XamlAttachedProbe");

        Assert.IsEmpty(result.Diagnostics, GeneratorProbe.Describe(result.Diagnostics));

        var generated = result.GeneratedText();
        StringAssert.Contains(generated, "public static readonly global::VeloxDev.WinForms.Xaml.DependencyProperty NoteProperty =");
        StringAssert.Contains(generated, "global::VeloxDev.WinForms.Xaml.DependencyProperty.RegisterAttached(");
        StringAssert.Contains(generated, "\"Note\"");
        StringAssert.Contains(generated, "typeof(string)");
        StringAssert.Contains(generated, "typeof(global::Probe.Attachments)");
        StringAssert.Contains(
            generated,
            "public static partial global::System.String? GetNote(global::System.Windows.Forms.Control control)");
        StringAssert.Contains(generated, "=> (global::System.String?)NoteProperty.GetValue(control)!;");
        StringAssert.Contains(
            generated,
            "public static partial void SetNote(global::System.Windows.Forms.Control control, global::System.String? value)");
        StringAssert.Contains(generated, "=> NoteProperty.SetValue(control, value);");
    }

    [TestMethod]
    public void ANonPartialProperty_IsRefusedWithXamlp001()
    {
        var result = GeneratorProbe.Run(new DependencyPropertyGenerator(), """
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public partial class Card : VeloxDev.WinForms.Xaml.Controls.Control
            {
                [XamlProperty]
                public string Title { get; set; } = string.Empty;
            }
            """, "XamlPropertyNotPartialProbe");

        var diagnostic = AssertSingle(result, "VELOX_XAMLP001");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.IsEmpty(result.GeneratedText());
    }

    [TestMethod]
    public void APropertyOnANonControl_IsRefusedWithXamlp002()
    {
        var result = GeneratorProbe.Run(new DependencyPropertyGenerator(), """
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public partial class Plain
            {
                [XamlProperty]
                public partial string Title { get; set; }
            }
            """, "XamlPropertyOwnerProbe");

        var diagnostic = AssertSingle(result, "VELOX_XAMLP002");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [TestMethod]
    public void ANonPartialAttachedAccessor_IsRefusedWithXamlp003()
    {
        var result = GeneratorProbe.Run(new DependencyPropertyGenerator(), """
            using System.Windows.Forms;
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public static partial class Attachments
            {
                [XamlAttachedProperty]
                public static string GetNote(System.Windows.Forms.Control control) => string.Empty;
            }
            """, "XamlAttachedInvalidProbe");

        var diagnostic = AssertSingle(result, "VELOX_XAMLP003");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [TestMethod]
    public void ABackingFieldAlreadyInTheSource_IsRefusedWithXamlp004()
    {
        var result = GeneratorProbe.Run(new DependencyPropertyGenerator(), """
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public partial class Card : VeloxDev.WinForms.Xaml.Controls.Control
            {
                public static readonly DependencyProperty TitleProperty = null!;

                [XamlProperty]
                public partial string Title { get; set; }
            }
            """, "XamlPropertyConflictProbe");

        var diagnostic = AssertSingle(result, "VELOX_XAMLP004");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.GetMessage(), "TitleProperty");
    }

    private static Diagnostic AssertSingle(GeneratorDriverRunResult result, string id)
    {
        var matches = result.Diagnostics.Where(diagnostic => diagnostic.Id == id).ToArray();
        Assert.HasCount(1, matches, GeneratorProbe.Describe(result.Diagnostics));
        return matches[0];
    }
}
