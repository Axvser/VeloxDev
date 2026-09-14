using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using VeloxDev.AI;
using VeloxDev.AI.Skills;

namespace VeloxDev.Core.Extension.Test.Agent.Skills;

/// <summary>
/// Coverage for <see cref="SkillAgentToolkit"/> — the Agent-facing skill tools. They are registered by
/// name like every other tool, so these invoke them through the AIFunction surface the model would use.
/// </summary>
[TestClass]
public class SkillAgentToolkitTests
{
    private const string System = "Workflow";

    private static string Invoke(AITool tool, params (string Name, object? Value)[] args)
    {
        var callArgs = new AIFunctionArguments();
        foreach (var (n, v) in args)
            callArgs[n] = v;
        var result = ((AIFunction)tool).InvokeAsync(callArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    private static SkillScope ScopeWithEmbeddedSkills()
    {
        var scope = new SkillScope().WithSource(new EmbeddedSkillSource(System));
        scope.Refresh();
        return scope;
    }

    /// <summary>Shared setup, so no test can silently depend on a tool having been registered twice.</summary>
    private static SkillAgentToolkit Toolkit(SkillScope scope) => new(scope);

    [TestMethod]
    public void CreateTools_RegistersTheFourSkillTools()
    {
        var names = Toolkit(ScopeWithEmbeddedSkills()).CreateTools().Select(t => t.Name).ToArray();

        Assert.HasCount(4, names);
        CollectionAssert.Contains(names, "ListSkills");
        // The two loading tools keep the Agent Skills convention's names.
        CollectionAssert.Contains(names, "load_skill");
        CollectionAssert.Contains(names, "read_skill_resource");
        CollectionAssert.Contains(names, "UnloadSkill");
    }

    [TestMethod]
    public void ListSkills_ReportsEveryDiscoveredSkill()
    {
        var scope = ScopeWithEmbeddedSkills();
        var list = Toolkit(scope).CreateTools().Single(t => t.Name == "ListSkills");

        var json = JObject.Parse(Invoke(list));

        Assert.AreEqual("ok", json["status"]?.Value<string>());
        Assert.AreEqual(7, json["skillCount"]?.Value<int>());
        Assert.AreEqual(7, json["activeCount"]?.Value<int>(), "skills start enabled");
        Assert.AreEqual(0, json["errorCount"]?.Value<int>());

        var first = (json["skills"] as JArray)![0];
        Assert.IsNotNull(first["name"]);
        Assert.AreEqual("Embedded", first["source"]?.Value<string>());
        Assert.AreEqual("Ready", first["state"]?.Value<string>());
        Assert.IsTrue(first["enabled"]?.Value<bool>());
    }

    [TestMethod]
    public void LoadSkill_ReturnsTheTextAndSwitchesTheSkillOn()
    {
        var scope = ScopeWithEmbeddedSkills();
        scope.Disable("compiler-usage");
        var load = Toolkit(scope).CreateTools().Single(t => t.Name == "load_skill");

        var json = JObject.Parse(Invoke(load, ("skillName", "compiler-usage")));

        Assert.AreEqual("ok", json["status"]?.Value<string>(), json.ToString());
        Assert.IsTrue(scope.Find("compiler-usage")!.IsEnabled, "loading must switch the skill on");
        Assert.IsFalse(string.IsNullOrWhiteSpace(json["content"]?.Value<string>()));

        // The body must be the document's text, not its frontmatter header.
        Assert.IsFalse(json["content"]!.Value<string>()!.StartsWith("---", StringComparison.Ordinal));
    }

    [TestMethod]
    public void UnloadSkill_SwitchesTheSkillOff()
    {
        var scope = ScopeWithEmbeddedSkills();
        var unload = Toolkit(scope).CreateTools().Single(t => t.Name == "UnloadSkill");

        var json = JObject.Parse(Invoke(unload, ("skillName", "smart-layout")));

        Assert.AreEqual("ok", json["status"]?.Value<string>());
        Assert.IsFalse(scope.Find("smart-layout")!.IsEnabled);
    }

    [TestMethod]
    public void LoadSkill_UnknownName_IsAnActionableError()
    {
        var scope = ScopeWithEmbeddedSkills();
        var load = Toolkit(scope).CreateTools().Single(t => t.Name == "load_skill");

        var json = JObject.Parse(Invoke(load, ("skillName", "nope")));

        Assert.AreEqual("error", json["status"]?.Value<string>());
        Assert.Contains("ListSkills", json["message"]?.Value<string>() ?? string.Empty, "the error must say how to recover");
    }

    [TestMethod]
    public void ReadSkillResource_EmbeddedSkill_SaysThereAreNone()
    {
        var scope = ScopeWithEmbeddedSkills();
        var read = Toolkit(scope).CreateTools().Single(t => t.Name == "read_skill_resource");

        var json = JObject.Parse(Invoke(read, ("skillName", "compiler-usage"), ("relativePath", "references/x.md")));

        Assert.AreEqual("error", json["status"]?.Value<string>());
        Assert.Contains("no resources", json["message"]?.Value<string>() ?? string.Empty);
    }

    [TestMethod]
    public void ReadSkillResource_FileSkill_ReturnsTheResourceAndRefusesEscapes()
    {
        var root = Path.Combine(Path.GetTempPath(), "veloxdev-skill-tests", Guid.NewGuid().ToString("N"));
        var skillDir = Path.Combine(root, "disk-skill");
        Directory.CreateDirectory(Path.Combine(skillDir, "references"));
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"),
            "---\nname: disk-skill\ndescription: A disk skill.\n---\n\n## Body");
        File.WriteAllText(Path.Combine(skillDir, "references", "model.md"), "the model");
        File.WriteAllText(Path.Combine(root, "secret.md"), "must not be reachable");

        var scope = new SkillScope().WithSource(new FileSkillSource(root));
        scope.Refresh();
        var read = Toolkit(scope).CreateTools().Single(t => t.Name == "read_skill_resource");

        var ok = JObject.Parse(Invoke(read, ("skillName", "disk-skill"), ("relativePath", "references/model.md")));
        Assert.AreEqual("ok", ok["status"]?.Value<string>());
        Assert.AreEqual("the model", ok["content"]?.Value<string>());

        var escaped = JObject.Parse(Invoke(read, ("skillName", "disk-skill"), ("relativePath", "../secret.md")));
        Assert.AreEqual("error", escaped["status"]?.Value<string>(), "a traversal attempt must not read the file");
    }
}
