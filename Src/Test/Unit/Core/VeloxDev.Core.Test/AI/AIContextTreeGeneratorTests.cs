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

    // ── 资源文件 ──

    private static (string Path, string Text) Resource(string xml) => ("AgentContext.agentcontext.xml", xml);

    [TestMethod]
    public void AResourceCanDescribeATypeTheCodeDoesNotAnnotate()
    {
        var (_, generated) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public class Node { public int Title { get; set; } public void Run() { } }",
            "Probe",
            [
                Resource("""
                    <AgentContext>
                      <Type name="Probe.Node">
                        <Text language="Chinese">一个节点</Text>
                        <Member name="Title">
                          <Text language="Chinese">标题</Text>
                        </Member>
                        <Member name="Run">
                          <Text language="English">Runs the node</Text>
                        </Member>
                      </Type>
                    </AgentContext>
                    """),
            ]);

        StringAssert.Contains(generated, "\"一个节点\"", "the type-level text must reach the fragment");
        StringAssert.Contains(generated, "\"标题\"", "the member text must reach the fragment");
        StringAssert.Contains(generated, "\"Runs the node\"", "a method can be described from the resource");
        StringAssert.Contains(generated, "case \"Title\":", "the described property must be actionable");
    }

    [TestMethod]
    public void AResourceAddressesAPromotedCommandByItsMethodName()
    {
        var (_, generated) = GeneratorProbe.Run(
            new AIContextTree(),
            """
            namespace Probe;

            public partial class Vm
            {
                [VeloxDev.MVVM.VeloxCommand]
                private void Save() { }
            }
            """,
            "Probe",
            [
                Resource("""
                    <AgentContext>
                      <Type name="Probe.Vm">
                        <Member name="Save">
                          <Text language="English">Saves the document</Text>
                        </Member>
                      </Type>
                    </AgentContext>
                    """),
            ]);

        StringAssert.Contains(generated, "\"SaveCommand\"", "the command node keeps its generated name");
        StringAssert.Contains(generated, "\"Saves the document\"", "the resource matches the source method name");
    }

    [TestMethod]
    public void AResourceAddressesAPromotedFieldByItsFieldName()
    {
        var (_, generated) = GeneratorProbe.Run(
            new AIContextTree(),
            """
            namespace Probe;

            public partial class Vm
            {
                [VeloxDev.MVVM.VeloxProperty]
                private int _delay = 0;
            }
            """,
            "Probe",
            [
                Resource("""
                    <AgentContext>
                      <Type name="Probe.Vm">
                        <Member name="_delay">
                          <Text language="English">Delay in milliseconds</Text>
                        </Member>
                      </Type>
                    </AgentContext>
                    """),
            ]);

        StringAssert.Contains(generated, "\"Delay\"", "the promoted property keeps its generated name");
        StringAssert.Contains(generated, "\"Delay in milliseconds\"", "the resource matches the field name");
    }

    [TestMethod]
    public void ANestedTypeCanBeNamedWithDots()
    {
        var (_, generated) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public class Outer { public class Inner { public int Value { get; set; } } }",
            "Probe",
            [
                Resource("""
                    <AgentContext>
                      <Type name="Probe.Outer.Inner">
                        <Member name="Value">
                          <Text language="English">A value</Text>
                        </Member>
                      </Type>
                    </AgentContext>
                    """),
            ]);

        StringAssert.Contains(generated, "\"Probe.Outer+Inner\"", "the fragment indexes the reflection full name");
        StringAssert.Contains(generated, "\"A value\"");
    }

    [TestMethod]
    public void AResourceCanDescribeAnEnumMember()
    {
        var (_, generated) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public enum Mode { Fast, Slow }",
            "Probe",
            [
                Resource("""
                    <AgentContext>
                      <Type name="Probe.Mode">
                        <Member name="Fast">
                          <Text language="English">Quick</Text>
                        </Member>
                      </Type>
                    </AgentContext>
                    """),
            ]);

        StringAssert.Contains(generated, "\"Quick\"");
        StringAssert.Contains(generated, "global::VeloxDev.AI.AIContextNodeKind.EnumMember");
    }

    [TestMethod]
    public void AResourceCanDescribeAPrivateFieldWithoutAnAttribute()
    {
        var (diagnostics, generated) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public class Node { private int _secret; public int Value { get; set; } }",
            "Probe",
            [
                Resource("""
                    <AgentContext>
                      <Type name="Probe.Node">
                        <Member name="_secret">
                          <Text language="English">A private detail</Text>
                        </Member>
                      </Type>
                    </AgentContext>
                    """),
            ]);

        StringAssert.Contains(generated, "\"Secret\"", "the private field keeps its promoted name");
        StringAssert.Contains(generated, "\"A private detail\"");
        CollectionAssert.DoesNotContain(
            diagnostics.Select(static d => d.Id).ToList(),
            "VELOX_AI_TREE004",
            GeneratorProbe.Describe(diagnostics));
    }

    [TestMethod]
    public void AnUnknownMemberIsReported()
    {
        var (diagnostics, _) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public class Node { public int Title { get; set; } }",
            "Probe",
            [
                Resource("""
                    <AgentContext>
                      <Type name="Probe.Node">
                        <Text language="English">A node</Text>
                        <Member name="Missing">
                          <Text language="English">nope</Text>
                        </Member>
                      </Type>
                    </AgentContext>
                    """),
            ]);

        CollectionAssert.Contains(
            diagnostics.Select(static d => d.Id).ToList(),
            "VELOX_AI_TREE004",
            GeneratorProbe.Describe(diagnostics));
    }

    [TestMethod]
    public void AnUnknownTypeIsReportedEvenWhenNoTreeIsProduced()
    {
        var (diagnostics, generated) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public class Plain { public int Value { get; set; } }",
            "Probe",
            [
                Resource("""
                    <AgentContext>
                      <Type name="Probe.Missing">
                        <Text language="English">nope</Text>
                      </Type>
                    </AgentContext>
                    """),
            ]);

        Assert.AreEqual(string.Empty, generated, "nothing matched, so there is no fragment");
        CollectionAssert.Contains(
            diagnostics.Select(static d => d.Id).ToList(),
            "VELOX_AI_TREE003",
            GeneratorProbe.Describe(diagnostics));
    }

    [TestMethod]
    public void AnUnknownLanguageIsReported()
    {
        var (diagnostics, _) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public class Node { public int Title { get; set; } }",
            "Probe",
            [
                Resource("""
                    <AgentContext>
                      <Type name="Probe.Node">
                        <Text language="English">A node</Text>
                        <Text language="Klingon">nope</Text>
                      </Type>
                    </AgentContext>
                    """),
            ]);

        CollectionAssert.Contains(
            diagnostics.Select(static d => d.Id).ToList(),
            "VELOX_AI_TREE005",
            GeneratorProbe.Describe(diagnostics));
    }

    [TestMethod]
    public void AnEmptyTypeEntryDoesNotPullTheTypeIntoTheTree()
    {
        var (diagnostics, generated) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public class Node { public int Value { get; set; } }",
            "Probe",
            [Resource("""<AgentContext><Type name="Probe.Node" /></AgentContext>""")]);

        Assert.AreEqual(string.Empty, generated, "an entry with no text describes nothing");
        CollectionAssert.Contains(
            diagnostics.Select(static d => d.Id).ToList(),
            "VELOX_AI_TREE003",
            GeneratorProbe.Describe(diagnostics));
    }

    [TestMethod]
    public void AMalformedResourceIsReported()
    {
        var (diagnostics, _) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public class Node { public int Value { get; set; } }",
            "Probe",
            [Resource("<AgentContext><Type name=\"Probe.Node\"></AgentContext>")]);

        CollectionAssert.Contains(
            diagnostics.Select(static d => d.Id).ToList(),
            "VELOX_AI_TREE002",
            GeneratorProbe.Describe(diagnostics));
    }

    [TestMethod]
    public void AMultiLineTextKeepsItsLinesAndLosesTheXmlIndentation()
    {
        var (_, generated) = GeneratorProbe.Run(
            new AIContextTree(),
            "namespace Probe; public class Node { public int Title { get; set; } }",
            "Probe",
            [
                Resource("""
                    <AgentContext>
                      <Type name="Probe.Node">
                        <Text language="English">
                          First line
                            indented second
                        </Text>
                      </Type>
                    </AgentContext>
                    """),
            ]);

        StringAssert.Contains(generated, "First line\\n  indented second");
    }
}
