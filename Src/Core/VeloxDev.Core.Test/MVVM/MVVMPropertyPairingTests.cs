using Microsoft.CodeAnalysis;
using VeloxDev.Generators;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Where a <c>[VeloxProperty]</c> declaration finds its storage.
/// <para>
/// Two declaration forms exist — a field and a <c>partial</c> property — and the property form has to work out
/// whether it may reuse an existing field or must declare one of its own. Reusing the wrong one is not a compile
/// error inside the generated file; it is a duplicate member (CS0102) or an unexpected second storage slot, so
/// each branch is pinned here.
/// </para>
/// </summary>
[TestClass]
public class MVVMPropertyPairingTests
{
    private const string Prop1 = "VELOX_MVVM_PROP001";
    private const string Prop2 = "VELOX_MVVM_PROP002";

    // ── 字段路：不变量 ──

    [TestMethod]
    public void AFieldOnlyDeclaration_StillGeneratesFromTheField()
    {
        var (diagnostics, generated) = Run("[VeloxProperty] private string _id = \"x\";");

        Assert.IsEmpty(diagnostics, GeneratorProbe.Describe(diagnostics));
        StringAssert.Contains(generated, "get => this._id;");
        Assert.DoesNotContain("private System.String _id =", generated, "the user's own field must not be redeclared");
    }

    [TestMethod]
    public void AFieldWithoutAnUnderscore_IsAcceptedSilently()
    {
        // 只写字段这一路不要求下划线 —— 名字只要推得出合法属性名就不算错。
        var (diagnostics, generated) = Run("[VeloxProperty] private string id = \"x\";");

        Assert.IsEmpty(diagnostics, GeneratorProbe.Describe(diagnostics));
        StringAssert.Contains(generated, "get => this.id;");
        StringAssert.Contains(generated, "public System.String Id");
    }

    [DataRow("_")]
    [DataRow("_1x")]
    [TestMethod]
    public void AFieldNameThatYieldsNoProperty_IsReportedAndSkipped(string fieldName)
    {
        // 旧代码在这里会越界崩掉（"_"）或写出一句编不过的产物（"1x"）。
        var (diagnostics, generated) = Run($"[VeloxProperty] private string {fieldName} = \"x\";");

        Assert.HasCount(1, diagnostics, GeneratorProbe.Describe(diagnostics));
        Assert.AreEqual(Prop2, diagnostics[0].Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostics[0].Severity);
        Assert.DoesNotContain("_1x", generated);
    }

    // ── 属性路：backing 字段的取舍 ──

    [TestMethod]
    public void APropertyWithoutAField_DeclaresOne()
    {
        var (diagnostics, generated) = Run("[VeloxProperty] public partial string Name { get; set; }");

        Assert.IsEmpty(diagnostics, GeneratorProbe.Describe(diagnostics));
        StringAssert.Contains(generated, "private System.String _name = default(System.String);");
        StringAssert.Contains(generated, "public partial System.String Name");
    }

    [TestMethod]
    public void APropertyReusesAnExistingField_EvenWhenItIsNotMarked()
    {
        var (diagnostics, generated) = Run("""
            [VeloxProperty] public partial string Name { get; set; }
            private string _name = "seed";
            """);

        Assert.IsEmpty(diagnostics, GeneratorProbe.Describe(diagnostics));
        Assert.DoesNotContain("private System.String _name =", generated, "the existing field must be reused");
        StringAssert.Contains(generated, "get => _name;");
    }

    [TestMethod]
    public void AFieldAndAPropertyForTheSameMember_ProduceExactlyOneOfEach()
    {
        // 本轮的核心形态：默认值放字段、访问形态放属性，两半同时声明。
        var (diagnostics, generated) = Run("""
            [VeloxProperty] private string _id = "default";
            [VeloxProperty] public partial string Id { get; set; }
            """);

        Assert.IsEmpty(diagnostics, GeneratorProbe.Describe(diagnostics));
        Assert.AreEqual(1, CountOf(generated, "partial System.String Id"), "exactly one property");
        Assert.AreEqual(0, CountOf(generated, "private System.String _id ="), "and no second backing field");
        StringAssert.Contains(generated, "get => _id;");
    }

    [TestMethod]
    public void APairedDeclaration_KeepsTheAccessorsTheUserWrote()
    {
        var (diagnostics, generated) = Run("""
            [VeloxProperty] private string _id = "default";
            [VeloxProperty] public partial string Id { get; protected set; }
            """);

        Assert.IsEmpty(diagnostics, GeneratorProbe.Describe(diagnostics));
        StringAssert.Contains(generated, "protected set");
        StringAssert.Contains(generated, "partial void OnIdChanging(");
        StringAssert.Contains(generated, "partial void OnIdChanged(");
    }

    // ── 属性路：基类字段的可访问性 ──

    [DataRow("private string _name = \"b\";", true)]
    [DataRow("protected string _name = \"b\";", false)]
    [DataRow("internal string _name = \"b\";", false)]
    [DataRow("public string _name = \"b\";", false)]
    [TestMethod]
    public void ABaseClassField_IsReusedOnlyWhenItIsAccessible(string field, bool declaresItsOwn)
    {
        var (diagnostics, generated) = Run(
            "[VeloxProperty] public partial string Name { get; set; }",
            $"public class Base {{ {field} }}");

        Assert.IsEmpty(diagnostics, GeneratorProbe.Describe(diagnostics));
        if (declaresItsOwn)
        {
            StringAssert.Contains(generated, "private System.String _name = default(System.String);");
        }
        else
        {
            Assert.DoesNotContain("private System.String _name =", generated);
        }
    }

    // ── 冲突：报错并不生成 ──

    [TestMethod]
    public void APairedDeclarationWithMismatchedTypes_IsRefused()
    {
        var (diagnostics, generated) = Run("""
            [VeloxProperty] private int _id = 0;
            [VeloxProperty] public partial string Id { get; set; }
            """);

        Assert.HasCount(1, diagnostics, GeneratorProbe.Describe(diagnostics));
        Assert.AreEqual(Prop1, diagnostics[0].Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
        Assert.AreEqual(0, CountOf(generated, "System.String Id"), "nothing can be generated from a broken pair");
    }

    [DataRow("private readonly string _name = \"x\";")]
    [DataRow("private const string _name = \"x\";")]
    [DataRow("private static string _name = \"x\";")]
    [TestMethod]
    public void AFieldThatCannotBackAWritableProperty_IsRefused(string field)
    {
        var (diagnostics, _) = Run($$"""
            [VeloxProperty] public partial string Name { get; set; }
            {{field}}
            """);

        Assert.HasCount(1, diagnostics, GeneratorProbe.Describe(diagnostics));
        Assert.AreEqual(Prop1, diagnostics[0].Id);
    }

    [TestMethod]
    public void AReadonlyField_StillBacksAGetOnlyProperty()
    {
        // 只读属性只读这个字段，readonly 不构成障碍。
        var (diagnostics, generated) = Run("""
            [VeloxProperty] public partial string Name { get; }
            private readonly string _name = "x";
            """);

        Assert.IsEmpty(diagnostics, GeneratorProbe.Describe(diagnostics));
        StringAssert.Contains(generated, "get => _name;");
    }

    [TestMethod]
    public void TwoFieldsMappingToTheSameProperty_AreRefused()
    {
        var (diagnostics, generated) = Run("""
            [VeloxProperty] private string _id = "a";
            [VeloxProperty] private string id = "b";
            """);

        Assert.HasCount(1, diagnostics, GeneratorProbe.Describe(diagnostics));
        Assert.AreEqual(Prop1, diagnostics[0].Id);
        Assert.AreEqual(1, CountOf(generated, "public System.String Id"), "only one of them can own the name");
    }

    [TestMethod]
    public void TheGeneratorNeverCopiesAttributesIntoItsOutput()
    {
        // 特性留在用户写的那一半上；生成器只补代码，不搬运注解。
        var (_, generated) = Run("""
            [VeloxProperty] private string _id = "default";
            [VeloxProperty] public partial string Id { get; set; }
            """);

        Assert.DoesNotContain("VeloxProperty", generated);
        Assert.DoesNotContain("ObservableProperty", generated);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, string Generated) Run(
        string members, string? baseClass = null)
    {
        var source = $$"""
            using VeloxDev.MVVM;

            namespace Probe;

            {{baseClass}}

            public partial class Vm{{(baseClass is null ? string.Empty : " : Base")}}
            {
            {{members}}
            }
            """;

        // 全限定：测试自己的命名空间段也叫 MVVM，裸写会被解析成命名空间。
        var (diagnostics, generated) = GeneratorProbe.Run(new VeloxDev.Generators.MVVM(), source, "MvvmPropertyProbe");
        return (diagnostics, generated);
    }

    private static int CountOf(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
