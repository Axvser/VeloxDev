using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// The tree-backed context blocks must say what the reflective ones said.
/// </summary>
/// <remarks>
/// The prompt tables are read by a model, so a silent divergence here changes what the agent believes about the
/// framework. Both renderers are driven over every type the tree carries, in every language, and compared
/// character for character.
/// </remarks>
[TestClass]
public class AgentContextTreeParityTests
{
    private const string FrameworkRoot = "Framework";

    [TestMethod]
    public void EveryRenderedBlockMatchesTheReflectiveOne()
    {
        var mismatches = new List<string>();
        var compared = 0;

        foreach (var entry in TypeEntries(FrameworkRoot))
        {
            var accessor = AIContextTreeRegistry.FindAccessor(entry.TypeName!);
            if (accessor is null) continue;

            foreach (var language in new[] { AgentLanguages.English, AgentLanguages.ChineseSimplified })
            {
                var expected = RenderByReflection(entry, accessor.TargetType, language);
                if (expected is null) continue;

                var actual = RenderFromTree(entry, language);
                compared++;

                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    mismatches.Add($"### {entry.TypeName} ({language})\n--- reflection ---\n{expected}\n--- tree ---\n{actual}");
                }
            }
        }

        Assert.IsGreaterThan(0, compared, "the walk must actually compare something");
        Assert.AreEqual(
            0,
            mismatches.Count,
            $"{mismatches.Count} of {compared} blocks differ:\n\n{string.Join("\n\n", mismatches.Take(2))}");
    }

    private static string? RenderByReflection(AIContextNode entry, Type type, AgentLanguages language)
        => entry.Kind switch
        {
            AIContextNodeKind.EnumType => AgentContextCollector.GetEnumContextByReflection(type, language),
            AIContextNodeKind.InterfaceType => AgentContextCollector.GetInterfaceContextByReflection(type, language),
            AIContextNodeKind.ComponentType => AgentContextCollector.GetClassContextByReflection(type, language),
            AIContextNodeKind.DataType => AgentContextCollector.GetDataContextByReflection(type, language),
            _ => null,
        };

    private static string RenderFromTree(AIContextNode entry, AgentLanguages language)
        => entry.Kind switch
        {
            AIContextNodeKind.EnumType => AgentContextTreeRenderer.Enum(entry, language),
            AIContextNodeKind.InterfaceType => AgentContextTreeRenderer.Interface(entry, language),
            AIContextNodeKind.ComponentType => AgentContextTreeRenderer.Class(entry, language),
            _ => AgentContextTreeRenderer.Data(entry, language),
        };

    /// <summary>Every type entry under a root, at any depth.</summary>
    private static IEnumerable<AIContextNode> TypeEntries(string path)
    {
        foreach (var node in AIContextDirectory.Shared.List(path))
        {
            if (node.Kind == AIContextNodeKind.Directory)
            {
                foreach (var inner in TypeEntries($"{path}/{node.Name}")) yield return inner;
            }
            else if (node.Kind is AIContextNodeKind.EnumType
                              or AIContextNodeKind.InterfaceType
                              or AIContextNodeKind.ComponentType
                              or AIContextNodeKind.DataType)
            {
                yield return node;
            }
        }
    }
}
