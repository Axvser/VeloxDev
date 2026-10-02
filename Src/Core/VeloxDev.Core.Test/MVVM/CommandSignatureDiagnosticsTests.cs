using Microsoft.CodeAnalysis;
using VeloxDev.Generators;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The rejection side of the signature matrix.
/// <para>
/// Everything else in this folder proves a supported shape <em>works</em>; nothing could prove an unsupported one
/// is <em>refused</em>, because a refused shape used to surface as CS1503 in a generated file — invisible to any
/// assertion. <c>VELOX_MVVM_CMD001</c> gives it a name, and this drives the generator directly so the diagnostic
/// can be asserted without going through the compiler.
/// </para>
/// </summary>
[TestClass]
public class CommandSignatureDiagnosticsTests
{
    private const string Id = "VELOX_MVVM_CMD001";

    [TestMethod]
    public void AVoidBodyTakingAToken_IsRefusedWithItsOwnDiagnostic()
    {
        var (diagnostics, generated) = Run("private void M(CancellationToken ct) { _ = ct; }");

        Assert.HasCount(1, diagnostics, $"exactly one refusal:\n{Describe(diagnostics)}");
        Assert.AreEqual(Id, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), "'M'");
        StringAssert.Contains(diagnostics[0].GetMessage(), "void");
        Assert.IsEmpty(generated, "a refused method must not reach the generated file either");
    }

    [TestMethod]
    public void AGenericMethodWhoseTypeParametersMissItsParameter_IsRefusedWithItsOwnDiagnostic()
    {
        var (diagnostics, generated) = Run(
            "private Task<int> M<T>(object? p) { _ = p; return Task.FromResult(1); }");

        Assert.HasCount(1, diagnostics, Describe(diagnostics));
        Assert.AreEqual(Id, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), "generic");
        Assert.IsEmpty(generated, "a refused method must not reach the generated file either");

        // 泛型**类**是另一回事：它走 partial 声明，不该被这条诊断波及（见 GenericOuterNestedCommandViewModel）。
        var (classDiagnostics, _) = Run(
            "private Task M(object? p) { _ = p; return Task.CompletedTask; }", genericClass: true);
        Assert.IsEmpty(classDiagnostics, "a generic class must not be refused - only a generic method is");
    }

    [TestMethod]
    public void AGenericMethodWhoseTypeParameterIsItsParameter_IsAccepted()
    {
        // 拒绝条件放宽了：T 出现在参数类型里，访问器就能把它作为自己的类型参数带出去。
        var (diagnostics, generated) = Run(
            "private Task M<T>(T value) where T : class { _ = value; return Task.CompletedTask; }");

        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        StringAssert.Contains(generated, "GetMCommand<T>() where T : class");
        StringAssert.Contains(generated, "IVeloxCommand<T, global::System.Object?>");
    }

    [TestMethod]
    public void AConcreteParameter_EmitsATypedPropertyAndItsMatchingConstructor()
    {
        var (diagnostics, generated) = Run("private Task M(string value) { _ = value; return Task.CompletedTask; }");

        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        StringAssert.Contains(generated, "IVeloxCommand<global::System.String, global::System.Object?>");
        StringAssert.Contains(generated, "CreateTypedTaskOnlyWithParameter<global::System.String>");
    }

    [TestMethod]
    public void SeveralLeadingParameters_ProduceTheArityFamilyCommand()
    {
        // 两个前导形参不再是拒绝：arity 族的元数就是「形参个数 + 结果」。
        var (diagnostics, generated) = Run(
            "private Task M(string a, string b) { _ = a; _ = b; return Task.CompletedTask; }");

        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        StringAssert.Contains(
            generated,
            "IVeloxCommand<global::System.String, global::System.String, global::System.Object?>");
    }

    [TestMethod]
    public void MoreLeadingParametersThanTheDelegateAllows_IsRefused()
    {
        // 上限由委托决定：体是 Func<T1..Tn, CancellationToken, Task<TResult>>，Func 最多 17 个类型实参。
        var many = string.Join(", ", System.Linq.Enumerable.Range(1, 16).Select(static i => $"int p{i}"));
        var (diagnostics, _) = Run($"private Task M({many}) => Task.CompletedTask;");

        Assert.HasCount(1, diagnostics, Describe(diagnostics));
        Assert.AreEqual(Id, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), "carries at most");
    }

    [TestMethod]
    public void EverySupportedShape_IsAcceptedSilently()
    {
        // 与 CommandSignatureViewModel 覆盖的形态同源：这里断言的是「不被拒绝」。
        const string body = """
            private void A0() { }
            private void A1(object? p) { _ = p; }
            private Task B0() => Task.CompletedTask;
            private Task B1(object? p) { _ = p; return Task.CompletedTask; }
            private Task B2(CancellationToken ct) { _ = ct; return Task.CompletedTask; }
            private Task B3(object? p, CancellationToken ct) { _ = p; _ = ct; return Task.CompletedTask; }
            private ValueTask C0() => default;
            private ValueTask C1(object? p) { _ = p; return default; }
            private ValueTask C2(CancellationToken ct) { _ = ct; return default; }
            private ValueTask C3(object? p, CancellationToken ct) { _ = p; _ = ct; return default; }
            private Task D1(string s) { _ = s; return Task.CompletedTask; }
            private Task D2(string s, CancellationToken ct) { _ = s; _ = ct; return Task.CompletedTask; }
            private void E1(string s) { _ = s; }
            """;

        var (diagnostics, generated) = Run(body);

        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        Assert.IsNotEmpty(generated, "and all of them must still produce a file");
    }

    [TestMethod]
    public void EveryTypedAndGenericShape_IsAcceptedSilently()
    {
        // 与 EverySupportedShape_IsAcceptedSilently 互补：那一份钉非强类型形态，这一份钉强类型与泛型形态。
        const string body = """
            private Task<string> T1() => Task.FromResult("x");
            private Task<string> T2(object? p) { _ = p; return Task.FromResult("x"); }
            private Task<string> T3(CancellationToken ct) { _ = ct; return Task.FromResult("x"); }
            private Task<string> T4(object? p, CancellationToken ct) { _ = p; _ = ct; return Task.FromResult("x"); }
            private Task<string> T5(string s) { _ = s; return Task.FromResult("x"); }
            private Task<string> T6(string s, CancellationToken ct) { _ = s; _ = ct; return Task.FromResult("x"); }
            private ValueTask<string> T7() => new("x");
            private ValueTask<string> T8(object? p) { _ = p; return new("x"); }
            private ValueTask<string> T9(CancellationToken ct) { _ = ct; return new("x"); }
            private ValueTask<string> T10(object? p, CancellationToken ct) { _ = p; _ = ct; return new("x"); }
            private ValueTask<int> T11(int n) => new(n);
            private ValueTask<int> T12(int n, CancellationToken ct) { _ = ct; return new(n); }
            private Task G1<T>(T v) { _ = v; return Task.CompletedTask; }
            private Task G2<T>(T v, CancellationToken ct) { _ = v; _ = ct; return Task.CompletedTask; }
            private ValueTask G3<T>(T v) { _ = v; return default; }
            private ValueTask G4<T>(T v, CancellationToken ct) { _ = v; _ = ct; return default; }
            private void G5<T>(T v) { _ = v; }
            private Task G6<T>(T[] v) { _ = v; return Task.CompletedTask; }
            private Task G7<T>((T, T) v) { _ = v; return Task.CompletedTask; }
            private Task G8<T, U>((T, U) v) { _ = v; return Task.CompletedTask; }
            private Task G9<T>(T v) where T : class { _ = v; return Task.CompletedTask; }
            private Task G10<T>(T v) where T : struct { _ = v; return Task.CompletedTask; }
            """;

        var (diagnostics, generated) = Run(body);

        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        Assert.IsNotEmpty(generated, "and all of them must still produce a file");

        // 类自己的类型参数（情形 1）：参数类型里出现的是类的 T，属性类型写得出 IVeloxCommand<T, object?>。
        var (classDiagnostics, classGenerated) = Run(
            "private Task H1(T v) { _ = v; return Task.CompletedTask; }", genericClass: true);
        Assert.IsEmpty(classDiagnostics, Describe(classDiagnostics));
        StringAssert.Contains(classGenerated, "IVeloxCommand<T, global::System.Object?>");
    }

    [TestMethod]
    public void ACommandNameAlreadyTakenOnTheType_IsRefused()
    {
        // 以前这会静默产出 CS0102 —— 生成文件里冒出第二个 RunCommand。
        var (diagnostics, generated) = GeneratorProbe.Run(new Command(), """
            using System;
            using System.Threading.Tasks;
            using VeloxDev.MVVM;

            namespace Probe;

            public partial class Vm
            {
                public IVeloxCommand RunCommand => throw new NotSupportedException();
                [VeloxCommand] private Task Run() => Task.CompletedTask;
            }
            """, "NameClashProbe");

        Assert.HasCount(1, diagnostics, GeneratorProbe.Describe(diagnostics));
        Assert.AreEqual(Id, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), "already exists");
        Assert.IsEmpty(generated, "nothing can be generated for a name that is taken");
    }

    [TestMethod]
    public void AGenericAccessorBesideAnInterfaceCommandProperty_IsRefused()
    {
        // 接口要的是属性，泛型访问器只能给方法 —— 没有可退让的余地。
        var (diagnostics, _) = GeneratorProbe.Run(new Command(), """
            using System.Threading.Tasks;
            using VeloxDev.MVVM;

            namespace Probe;

            public interface IHasRun
            {
                IVeloxCommand RunCommand { get; }
            }

            public partial class Vm : IHasRun
            {
                [VeloxCommand] private Task Run<T>(T value) { _ = value; return Task.CompletedTask; }
            }
            """, "GenericAccessorContractProbe");

        Assert.HasCount(1, diagnostics, GeneratorProbe.Describe(diagnostics));
        Assert.AreEqual(Id, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), "only a property can satisfy");
    }

    [DataRow("IVeloxCommand<string, object?>", "IVeloxCommand<global::System.String, global::System.Object?> RunCommand")]
    [TestMethod]
    public void AnInterfaceCommandProperty_MakesThePropertyKeepTheDeclaredType(
        string declared, string expectedProperty)
    {
        // 接口声明的类型必须被原样采用：退成别的形状是 CS0738，退成不可转换的形状是 CS0266。
        // 两条继承链撑得起这两行 —— 2-arity 派生自 1-arity，1-arity 派生自 IVeloxCommand。
        var (diagnostics, generated) = GeneratorProbe.Run(new Command(), $$"""
            using System.Threading.Tasks;
            using VeloxDev.MVVM;

            namespace Probe;

            public interface IHasRun
            {
                {{declared}} RunCommand { get; }
            }

            public partial class Vm : IHasRun
            {
                [VeloxCommand] private Task Run(string value) { _ = value; return Task.CompletedTask; }
            }
            """, "TypedContractProbe");

        Assert.IsEmpty(diagnostics, GeneratorProbe.Describe(diagnostics));
        Assert.IsTrue(generated.Contains(expectedProperty), generated);
    }

    [TestMethod]
    public void AnInterfaceCommandPropertyWithADifferentResultType_IsRefused()
    {
        // 声明的是 object?，命令体返回 int —— 没有声明逆变，这个上转不存在，生成出来就是 CS0266。
        var (diagnostics, _) = GeneratorProbe.Run(new Command(), """
            using System.Threading.Tasks;
            using VeloxDev.MVVM;

            namespace Probe;

            public interface IHasRun
            {
                IVeloxCommand<string, object?> RunCommand { get; }
            }

            public partial class Vm : IHasRun
            {
                [VeloxCommand] private Task<int> Run(string value) { _ = value; return Task.FromResult(1); }
            }
            """, "TypedContractMismatchProbe");

        Assert.HasCount(1, diagnostics, GeneratorProbe.Describe(diagnostics));
        Assert.AreEqual(Id, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), "cannot be assigned to it");
    }

    [TestMethod]
    public void ASupportedMethodBesideARefusedOne_IsStillGenerated()
    {
        var (diagnostics, generated) = Run("""
            private Task Good(object? p) { _ = p; return Task.CompletedTask; }
            private void Bad(CancellationToken ct) { _ = ct; }
            """);

        Assert.HasCount(1, diagnostics, Describe(diagnostics));
        StringAssert.Contains(generated, "GoodCommand", "one bad method must not take the good one down with it");
        Assert.DoesNotContain("BadCommand", generated);
    }

    // 一行一个成员，每个都带上特性 —— 只标注第一个会让后面的成员悄悄变成「没有被标注」，
    // 测试于是断言了个寂寞。
    private static (IReadOnlyList<Diagnostic> Diagnostics, string Generated) Run(
        string members, bool genericClass = false)
    {
        var attributed = string.Join(
            Environment.NewLine,
            members.Split('\n').Select(static line =>
                line.Trim().Length == 0 ? line : "[VeloxCommand] " + line.Trim()));

        var source = $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using VeloxDev.MVVM;

            namespace Probe;

            public partial class Vm{{(genericClass ? "<T>" : string.Empty)}}
            {
            {{attributed}}
            }
            """;

        var (diagnostics, generated) = GeneratorProbe.Run(new Command(), source, "CommandSignatureProbe");
        return (diagnostics.Where(d => d.Id == Id).ToArray(), generated);
    }

    private static string Describe(IReadOnlyList<Diagnostic> diagnostics) => GeneratorProbe.Describe(diagnostics);
}
