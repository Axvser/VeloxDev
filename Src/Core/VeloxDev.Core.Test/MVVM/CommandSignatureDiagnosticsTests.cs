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
    public void AGenericMethod_IsRefusedWithItsOwnDiagnostic()
    {
        var (diagnostics, generated) = Run(
            "private Task<int> M<T>(object? p) { _ = p; return Task.FromResult(1); }");

        Assert.HasCount(1, diagnostics, Describe(diagnostics));
        Assert.AreEqual(Id, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), "generic");

        // 泛型**类**是另一回事：它走 partial 声明，不该被这条诊断波及（见 GenericOuterNestedCommandViewModel）。
        var (classDiagnostics, _) = Run(
            "private Task M(object? p) { _ = p; return Task.CompletedTask; }", genericClass: true);
        Assert.IsEmpty(classDiagnostics, "a generic class must not be refused - only a generic method is");
    }

    [TestMethod]
    public void MoreThanOneLeadingParameter_IsRefusedWithItsOwnDiagnostic()
    {
        var (diagnostics, _) = Run(
            "private Task M(string a, string b) { _ = a; _ = b; return Task.CompletedTask; }");

        Assert.HasCount(1, diagnostics, Describe(diagnostics));
        Assert.AreEqual(Id, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), "more than one parameter");
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
