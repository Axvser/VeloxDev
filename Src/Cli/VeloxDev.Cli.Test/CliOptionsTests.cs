namespace VeloxDev.Cli.Test;

[TestClass]
public class CliOptionsTests
{
    [TestMethod]
    public void ParsesTheRunCommand()
    {
        var options = CliOptions.Parse(
            ["run", "--archive", "graph.json", "--start", "3", "--seed", "{}", "--timeout-ms", "1500"]);

        Assert.AreEqual("run", options.Mode);
        Assert.AreEqual("graph.json", options.Archive);
        Assert.AreEqual(3, options.StartNode);
        Assert.AreEqual("{}", options.Seed);
        Assert.AreEqual(1500, options.TimeoutMs);
        Assert.IsFalse(options.AllowExecution);
    }

    [TestMethod]
    public void ParsesRepeatedAndSemicolonSeparatedAssemblies()
    {
        var options = CliOptions.Parse(
            ["mcp", "--archive", "a.json", "--assembly", "one.dll", "--assemblies", "two.dll;three.dll"]);

        CollectionAssert.AreEqual(new[] { "one.dll", "two.dll", "three.dll" }, options.Assemblies);
    }

    [TestMethod]
    public void ParsesTheStrictPluginVersionFlag()
    {
        var strict = CliOptions.Parse(["mcp", "--archive", "a.json", "--strict-plugin-versions"]);
        var permissive = CliOptions.Parse(["mcp", "--archive", "a.json"]);

        Assert.IsTrue(strict.StrictPluginVersions);
        Assert.IsFalse(permissive.StrictPluginVersions);
    }

    [TestMethod]
    public void ParsesTheSingleArchiveFlag()
    {
        var single = CliOptions.Parse(["mcp", "--archive", "a.json", "--single-archive"]);
        var multi = CliOptions.Parse(["mcp", "--archive", "a.json"]);

        Assert.IsTrue(single.SingleArchive);
        Assert.IsFalse(multi.SingleArchive);
    }

    [TestMethod]
    public void ParsesTheAssemblyCopyOptOut()
    {
        var copies = CliOptions.Parse(["mcp", "--archive", "a.json"]);
        var noCopy = CliOptions.Parse(["mcp", "--archive", "a.json", "--no-assembly-copy"]);

        Assert.IsFalse(copies.NoAssemblyCopy);
        Assert.IsTrue(noCopy.NoAssemblyCopy);
    }

    [TestMethod]
    public void ParsesTheExecutionAndRecoveryOptions()
    {
        var options = CliOptions.Parse([
            "run", "--archive", "a.json", "--start", "2", "--node-result",
            "--in-process-execution", "--redirect-on-error", "1", "--checkpoint-file", "state.json",
            "--log-file", "run.log", "--max-concurrent-runs", "4", "--worker-kill-grace-ms", "50",
            "--output-tree", "post-run.json", "--workspace", "C:\\work",
        ]);

        Assert.IsTrue(options.NodeResult);
        Assert.IsTrue(options.InProcessExecution);
        Assert.AreEqual(1, options.RedirectOnError);
        Assert.AreEqual("state.json", options.CheckpointFile);
        Assert.AreEqual("run.log", options.LogFile);
        Assert.AreEqual(4, options.MaxConcurrentRuns);
        Assert.AreEqual(50, options.WorkerKillGraceMs);
        Assert.AreEqual("post-run.json", options.OutputTree);
        Assert.AreEqual("C:\\work", options.Workspace);
    }

    [TestMethod]
    public void ParsesTheRootType()
    {
        var options = CliOptions.Parse(["mcp", "--archive", "a.json", "--root-type", "Demo.ViewModels.TreeViewModel"]);

        Assert.AreEqual("Demo.ViewModels.TreeViewModel", options.RootType);
    }

    [TestMethod]
    public void AcceptsTheInlineValueForm()
    {
        var options = CliOptions.Parse(["mcp", "--archive=a.json", "--allow-execution"]);

        Assert.AreEqual("a.json", options.Archive);
        Assert.IsTrue(options.AllowExecution);
    }

    [TestMethod]
    public void RunRequiresAStartNode()
    {
        var error = Assert.ThrowsExactly<CliUsageException>(
            () => CliOptions.Parse(["run", "--archive", "a.json"]));

        StringAssert.Contains(error.Message, "--start");
    }

    [TestMethod]
    public void ArchiveIsAlwaysRequired()
    {
        var error = Assert.ThrowsExactly<CliUsageException>(() => CliOptions.Parse(["mcp"]));

        StringAssert.Contains(error.Message, "--archive");
    }

    [TestMethod]
    public void UnknownOptionsAreRefused()
    {
        var error = Assert.ThrowsExactly<CliUsageException>(
            () => CliOptions.Parse(["mcp", "--archive", "a.json", "--wat"]));

        StringAssert.Contains(error.Message, "--wat");
    }

    [TestMethod]
    public void NoArgumentsShowsHelp()
        => Assert.IsTrue(CliOptions.Parse([]).ShowHelp);
}
