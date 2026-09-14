using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using VeloxDev.AI;
using VeloxDev.AI.Skills;

namespace VeloxDev.Core.Extension.Test.Agent.Skills;

/// <summary>
/// Coverage for <see cref="SkillScope"/> and its two sources: what gets discovered, how the enabled flag
/// gates rendering, and how a malformed skill is reported rather than silently dropped.
/// </summary>
[TestClass]
public class SkillScopeTests
{
    private const string System = "Workflow";

    private static string NewTempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "veloxdev-skill-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteSkill(string root, string folder, string frontmatter, string body)
        => WriteSkill(root, folder, frontmatter, body, resources: null);

    private static void WriteSkill(string root, string folder, string frontmatter, string body, (string Path, string Content)[]? resources)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), frontmatter + "\n" + body);
        foreach (var (rel, content) in resources ?? [])
        {
            var target = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }
    }

    // ── Embedded source ──────────────────────────────────────────────────────

    [TestMethod]
    public void EmbeddedSource_DiscoversEveryShippedSkillInBothLanguages()
    {
        var descriptors = new EmbeddedSkillSource(System).Discover();

        // 7 skill documents per language, one descriptor each.
        Assert.HasCount(14, descriptors, "expected 7 English + 7 Chinese skill descriptors");
        Assert.HasCount(7, descriptors.Select(d => d.Name).Distinct().ToArray(),
            "both languages must collapse onto the same skill names");
        Assert.IsTrue(descriptors.All(d => d.Error is null), string.Join(" | ", descriptors.Select(d => d.Error)));
    }

    [TestMethod]
    public void EmbeddedSource_DerivesKebabCaseNamesFromFileNames()
    {
        var names = new EmbeddedSkillSource(System).Discover().Select(d => d.Name).Distinct().ToArray();

        CollectionAssert.Contains(names, "slot-enumerator");
        CollectionAssert.Contains(names, "compiler-usage");
        CollectionAssert.Contains(names, "smart-layout");
    }

    [TestMethod]
    public void EmbeddedBlock_ContainsEveryEnabledSkillBody()
    {
        var scope = new SkillScope().WithSource(new EmbeddedSkillSource(System));
        scope.Refresh();

        var block = scope.BuildEmbeddedBlock(AgentLanguages.English);

        foreach (var skill in scope.Status.Skills)
        {
            var body = scope.ReadSkillBody(skill.Name, AgentLanguages.English);
            Assert.IsNotNull(body, $"{skill.Name} produced no body");
            Assert.Contains(body.Trim(), block, $"{skill.Name}'s text is missing from the rendered block");
        }
    }

    [TestMethod]
    public void EmbeddedBlock_OmitsADisabledSkillAndRestoresItOnReEnable()
    {
        var scope = new SkillScope().WithSource(new EmbeddedSkillSource(System));
        scope.Refresh();

        var target = scope.Status.Skills.First(s => s.Name == "smart-layout");
        var before = scope.BuildEmbeddedBlock(AgentLanguages.English);
        var versionBefore = scope.Version;

        Assert.IsTrue(scope.Disable(target.Name));
        var afterDisable = scope.BuildEmbeddedBlock(AgentLanguages.English);

        Assert.IsTrue(scope.Version > versionBefore, "disabling a skill must advance Version");
        Assert.IsFalse(scope.Status.Skills.First(s => s.Name == target.Name).IsActive);
        Assert.IsFalse(afterDisable.Contains(scope.ReadSkillBody(target.Name, AgentLanguages.English)!),
            "a disabled skill must not reach the prompt");
        Assert.AreEqual(before.Replace(scope.ReadSkillBody(target.Name, AgentLanguages.English)!, string.Empty).Trim(),
            afterDisable.Trim(), "disabling one skill must not disturb the others");

        Assert.IsTrue(scope.Enable(target.Name));
        Assert.AreEqual(before.Trim(), scope.BuildEmbeddedBlock(AgentLanguages.English).Trim(),
            "re-enabling must restore the original block exactly");
    }

    [TestMethod]
    public void Refresh_PreservesEnabledFlags()
    {
        var scope = new SkillScope().WithSource(new EmbeddedSkillSource(System));
        scope.Refresh();
        scope.Disable("smart-layout");

        scope.Refresh();

        Assert.IsFalse(scope.Status.Skills.First(s => s.Name == "smart-layout").IsEnabled,
            "a refresh must not silently switch a disabled skill back on");
    }

    [TestMethod]
    public void SetEnabled_UnknownSkill_ReturnsFalse()
    {
        var scope = new SkillScope().WithSource(new EmbeddedSkillSource(System));
        scope.Refresh();

        Assert.IsFalse(scope.Enable("no-such-skill"));
    }

    // ── File source ──────────────────────────────────────────────────────────

    [TestMethod]
    public void FileSource_DiscoversSkillWithResources()
    {
        var root = NewTempRoot();
        WriteSkill(root, "my-skill",
            "---\nname: my-skill\ndescription: Does a thing worth matching on.\n---",
            "## Body\n\nInstructions here.",
            [("references/model.md", "# Model"), ("notes.txt", "hi")]);

        var descriptor = new FileSkillSource(root).Discover().Single();

        Assert.AreEqual("my-skill", descriptor.Name);
        Assert.AreEqual("Does a thing worth matching on.", descriptor.Description);
        Assert.IsNull(descriptor.Error, descriptor.Error);
        Assert.AreEqual(2, descriptor.ResourceCount);
    }

    [TestMethod]
    public void FileSource_ReadBody_StripsFrontmatter()
    {
        var root = NewTempRoot();
        WriteSkill(root, "my-skill", "---\nname: my-skill\ndescription: d\n---", "## Body\n\nInstructions here.");

        var source = new FileSkillSource(root);
        source.Discover();
        var body = source.ReadBody("my-skill", AgentLanguages.English);

        Assert.IsNotNull(body);
        Assert.IsFalse(body!.Contains("description:"), "frontmatter must not be part of the body");
        Assert.Contains("Instructions here.", body);
    }

    [TestMethod]
    public void FileSource_ReadResource_RefusesToEscapeTheSkillDirectory()
    {
        var root = NewTempRoot();
        WriteSkill(root, "my-skill", "---\nname: my-skill\ndescription: d\n---", "body",
            [("references/ok.md", "inside")]);
        File.WriteAllText(Path.Combine(root, "outside.md"), "should never be readable");

        var source = new FileSkillSource(root);
        source.Discover();

        Assert.AreEqual("inside", source.ReadResource("my-skill", "references/ok.md", AgentLanguages.English));
        Assert.IsNull(source.ReadResource("my-skill", "../outside.md", AgentLanguages.English),
            "a resource path must not be able to walk out of the skill directory");
        Assert.IsNull(source.ReadResource("my-skill", Path.Combine(root, "outside.md"), AgentLanguages.English));
    }

    [TestMethod]
    public void FileSource_FolderNameMismatch_IsReportedNotSwallowed()
    {
        var root = NewTempRoot();
        WriteSkill(root, "wrong-folder", "---\nname: actual-name\ndescription: d\n---", "body");

        var descriptor = new FileSkillSource(root).Discover().Single();

        Assert.IsFalse(string.IsNullOrEmpty(descriptor.Error), "a folder/name mismatch must be reported");
        Assert.Contains("wrong-folder", descriptor.Error);
        Assert.Contains("actual-name", descriptor.Error);
    }

    [TestMethod]
    public void FileSource_MissingFrontmatter_IsReportedAndStillIdentifiable()
    {
        var root = NewTempRoot();
        WriteSkill(root, "bare-skill", "## No frontmatter here", "body");

        var descriptor = new FileSkillSource(root).Discover().Single();

        Assert.AreEqual("bare-skill", descriptor.Name, "the folder name is the only identity left");
        Assert.IsFalse(string.IsNullOrEmpty(descriptor.Error));
    }

    [TestMethod]
    public void FileSource_RejectsNonKebabCaseName()
    {
        var root = NewTempRoot();
        WriteSkill(root, "BadName", "---\nname: BadName\ndescription: d\n---", "body");

        Assert.IsFalse(string.IsNullOrEmpty(new FileSkillSource(root).Discover().Single().Error));
    }

    [TestMethod]
    public void FileSource_EmptyRoot_DiscoversNothing()
    {
        Assert.IsEmpty(new FileSkillSource(NewTempRoot()).Discover());
    }

    // ── Scope composition ────────────────────────────────────────────────────

    [TestMethod]
    public void SourceWithError_IsSurfacedAsAnErroredSkill()
    {
        var root = NewTempRoot();
        WriteSkill(root, "wrong-folder", "---\nname: actual-name\ndescription: d\n---", "body");

        var scope = new SkillScope().WithSource(new FileSkillSource(root));
        scope.Refresh();

        var skill = scope.Status.Skills.Single();
        Assert.AreEqual(SkillState.Error, skill.State);
        Assert.IsFalse(string.IsNullOrEmpty(skill.Error));
        Assert.IsFalse(skill.IsActive, "an unreadable skill must not contribute to the prompt");
        Assert.AreEqual(1, scope.Status.ErrorCount);
    }

    [TestMethod]
    public void Advertisement_ListsFileSkillsOnlyAndFollowsTheConvention()
    {
        var root = NewTempRoot();
        WriteSkill(root, "disk-skill", "---\nname: disk-skill\ndescription: A disk skill.\n---", "body");

        var scope = new SkillScope()
            .WithSource(new EmbeddedSkillSource(System))
            .WithSource(new FileSkillSource(root));
        scope.Refresh();

        var advertisement = scope.BuildAdvertisement(AgentLanguages.English);

        Assert.Contains("<name>disk-skill</name>", advertisement);
        Assert.Contains("<description>A disk skill.</description>", advertisement);
        Assert.Contains("load_skill", advertisement);
        Assert.IsFalse(advertisement.Contains("slot-enumerator"),
            "embedded skills are injected in full, so they must not also be advertised for loading");
    }

    [TestMethod]
    public void Snapshot_TracksDiscoveryAndSwitches()
    {
        // The snapshot is what a prompt render reads, off the bound thread. It has to follow both the
        // discovery set and every switch, or a turn could see a stale skill list.
        var scope = new SkillScope().WithSource(new EmbeddedSkillSource(System));
        scope.Refresh();

        Assert.HasCount(7, scope.Status.Snapshot);
        Assert.IsTrue(scope.Status.Snapshot.All(s => s.IsActive));

        scope.Disable("smart-layout");

        var disabled = scope.Status.Snapshot.Single(s => s.Name == "smart-layout");
        Assert.IsFalse(disabled.IsActive, "the snapshot must carry the switch state, not just the roster");
        Assert.AreEqual(6, scope.Status.Snapshot.Count(s => s.IsActive));

        scope.Enable("smart-layout");
        Assert.IsTrue(scope.Status.Snapshot.Single(s => s.Name == "smart-layout").IsActive);
    }

    [TestMethod]
    public void Snapshot_CarriesTheSourceSoRenderersCanSplitByOrigin()
    {
        var root = NewTempRoot();
        WriteSkill(root, "disk-skill", "---\nname: disk-skill\ndescription: A disk skill.\n---", "body");

        var scope = new SkillScope()
            .WithSource(new EmbeddedSkillSource(System))
            .WithSource(new FileSkillSource(root));
        scope.Refresh();

        var snapshot = scope.Status.Snapshot;
        Assert.HasCount(8, snapshot);
        Assert.IsTrue(snapshot.Single(s => s.Name == "disk-skill").Source == SkillSourceKind.File);
        Assert.IsTrue(snapshot.Single(s => s.Name == "smart-layout").Source == SkillSourceKind.Embedded);
    }

    [TestMethod]
    public void DisabledFileSkill_LeavesTheAdvertisement()
    {
        var root = NewTempRoot();
        WriteSkill(root, "disk-skill", "---\nname: disk-skill\ndescription: A disk skill.\n---", "body");

        var scope = new SkillScope()
            .WithSource(new EmbeddedSkillSource(System))
            .WithSource(new FileSkillSource(root));
        scope.Refresh();
        scope.Disable("disk-skill");

        Assert.AreEqual(string.Empty, scope.BuildAdvertisement(AgentLanguages.English));
    }
}
