using VeloxDev.Core.Test.MVVM;
using VeloxDev.Generators;

namespace VeloxDev.Core.Test.AI;

/// <summary>
/// The generator itself: what it names things, and what it refuses to represent silently.
/// </summary>
/// <remarks>
/// Driven through <see cref="GeneratorProbe"/> rather than by building a fixture class, because the interesting
/// cases are the ones that would not compile — an ambiguous overload, a name another generator owns. A real
/// compilation could not hold them.
/// </remarks>
[TestClass]
public class AIContextTreeGeneratorTests
{
    private const string Fixture = """
        namespace Probe;

        public partial class ViewModel
        {
            [VeloxDev.MVVM.VeloxProperty]
            private int _delay = 0;

            [VeloxDev.AI.AgentContext(VeloxDev.AI.AgentLanguages.English, "Runs twice")]
            [VeloxDev.MVVM.VeloxCommand]
            private void RunAsync() { }

            public void Add(int a) { }
            public void Add(string a) { }
        }
        """;

    [TestMethod]
    public void AnAmbiguousOverloadIsReportedRatherThanDropped()
    {
        var (diagnostics, _) = GeneratorProbe.Run(new AIContextTree(), Fixture, "Probe");

        CollectionAssert.Contains(
            diagnostics.Select(static d => d.Id).ToList(),
            "VELOX_AI_TREE001",
            $"two methods taking one argument cannot both be reachable: {GeneratorProbe.Describe(diagnostics)}");
    }

    [TestMethod]
    public void TheObjectMethodsAreNotTreatedAsOverloads()
    {
        // ToString / GetHashCode / Equals / GetType 是每个类型都有的，不该进 Agent 面，
        // 也不该把重载诊断点着 —— 否则每个带强类型 Equals 的值类型都会报一条。
        var (diagnostics, _) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public struct Point { public bool Equals(Point other) => true; public override bool Equals(object? o) => true; }",
            "Probe");

        CollectionAssert.DoesNotContain(
            diagnostics.Select(static d => d.Id).ToList(),
            "VELOX_AI_TREE001",
            GeneratorProbe.Describe(diagnostics));
    }

    [TestMethod]
    public void APromotedFieldAppearsUnderTheNameTheMvvmGeneratorGivesIt()
    {
        var (_, generated) = GeneratorProbe.Run(new AIContextTree(), Fixture, "Probe");

        StringAssert.Contains(generated, "\"Delay\"", "a [VeloxProperty] field is described by its promoted property, not its field name");
        StringAssert.Contains(generated, "case \"Delay\":", "and the accessor acts on that same name");
    }

    [TestMethod]
    public void ACommandAppearsUnderTheNameTheCommandWriterGivesIt()
    {
        var (_, generated) = GeneratorProbe.Run(new AIContextTree(), Fixture, "Probe");

        // Auto 命名规则：RunAsync -> Run -> RunCommand。两个生成器看不见彼此，只能复现同一条规则。
        StringAssert.Contains(generated, "\"RunCommand\"");
        StringAssert.Contains(generated, "case \"RunCommand\":");
    }

    [TestMethod]
    public void AProjectWithoutTheAgentSurfaceProducesNothing()
    {
        var (_, generated) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public class Plain { public int Value { get; set; } }",
            "Probe");

        Assert.AreEqual(string.Empty, generated, "the generator must be inert where there is no agent surface");
    }
}
