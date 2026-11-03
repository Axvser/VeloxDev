using System.Text.Json;
using Microsoft.Extensions.AI;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Cli.Test;

[TestClass]
public class WorkflowSessionTests
{
    private static CliOptions Options(string archive, params string[] extra)
    {
        List<string> args = ["mcp", "--archive", archive, .. extra];
        return CliOptions.Parse([.. args]);
    }

    private static async Task<JsonDocument> InvokeAsync(WorkflowSession session, string name, params (string Name, object? Value)[] args)
    {
        var function = session.Toolkit.CreateTools(WorkflowToolCategory.All)
            .OfType<AIFunction>()
            .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Tool '{name}' is not registered.");

        var arguments = new AIFunctionArguments();
        foreach (var (key, value) in args)
            arguments[key] = value;

        var result = await function.InvokeAsync(arguments);
        return JsonDocument.Parse(result?.ToString() ?? "null");
    }

    [TestMethod]
    public async Task TheMcpSurfaceOmitsExecutionUntilItIsAllowed()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var plainHost = WorkflowSessionHost.Create(Options(path));
        var plain = plainHost.Current;
        var plainNames = plain.CreateMcpTools(plainHost).Select(t => t.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(plainNames.Contains("ListNodes"));
        Assert.IsTrue(plainNames.Contains("CreateNode"));
        Assert.IsFalse(plainNames.Contains("RunCompiledWorkflow"), "node execution is opt-in");
        Assert.IsFalse(plainNames.Contains("LoadWorkflow"), "the CLI never replaces the tree behind its tracker");
        Assert.IsFalse(plainNames.Contains("MarkDirty"), "a GUI bookkeeping tool has no meaning here");
        Assert.IsFalse(plainNames.Contains("ResetToolCallLimit"), "the CLI sets no call budget");

        await using var allowedHost = WorkflowSessionHost.Create(Options(path, "--allow-execution"));
        var allowed = allowedHost.Current;
        var allowedNames = allowed.CreateMcpTools(allowedHost).Select(t => t.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.IsFalse(allowedNames.Contains("RunCompiledWorkflow"),
            "the default execution surface is the isolated one, registered by the host rather than the toolkit");
        Assert.IsFalse(allowedNames.Contains("PauseCompiledRun"), "handle-based runs are in-process only");
        Assert.AreEqual("isolated", allowed.ExecutionMode);

        await using var inProcessHost = WorkflowSessionHost.Create(
            Options(path, "--allow-execution", "--in-process-execution"));
        var inProcess = inProcessHost.Current;
        var inProcessNames = inProcess.CreateMcpTools(inProcessHost).Select(t => t.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(inProcessNames.Contains("RunCompiledWorkflow"));
        Assert.IsTrue(inProcessNames.Contains("PauseCompiledRun"));
        Assert.AreEqual("in-process", inProcess.ExecutionMode);
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task TheSurfaceUsesTheLibrariesOwnToolNames()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var host = WorkflowSessionHost.Create(Options(path));
        var session = host.Current;

        var names = session.CreateMcpTools(host).Select(t => t.ProtocolTool.Name).ToList();

        // The MCP server did not invent a tool vocabulary; it exposes the toolkit's names verbatim.
        CollectionAssert.Contains(names, "GetWorkflowSummary");
        CollectionAssert.Contains(names, "GetFullTopology");
        CollectionAssert.Contains(names, "ListCreatableTypes");
        CollectionAssert.Contains(names, "ConnectByProperty");
        CollectionAssert.Contains(names, "SerializeWorkflow");
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task ReloadSwapsTheSessionAndAFailedReloadKeepsTheOldOne()
    {
        using var first = new TestArchive();
        var firstPath = first.WriteTree(TestArchive.TreeWithOneNode(out _));

        using var second = new TestArchive();
        var secondTree = new TreeDefaultViewModel();
        secondTree.GetHelper().CreateNode(new NodeDefaultViewModel());
        secondTree.GetHelper().CreateNode(new NodeDefaultViewModel());
        var secondPath = second.WriteTree(secondTree);

        await using var host = WorkflowSessionHost.Create(Options(firstPath));
        Assert.AreEqual(1, host.Current.Scope.Tree.Nodes.Count);

        await host.ReloadAsync(new ArchiveRequest(secondPath), redirectOnError: null);

        Assert.AreEqual(secondPath, host.Current.ArchivePath);
        Assert.AreEqual(2, host.Current.Scope.Tree.Nodes.Count);

        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => host.ReloadAsync(new ArchiveRequest(missing), redirectOnError: null));

        Assert.AreEqual(secondPath, host.Current.ArchivePath, "a failed load must leave the current session in place");
        Assert.AreEqual(2, host.Current.Scope.Tree.Nodes.Count);
    }

    [TestMethod]
    public async Task ReloadIsRefusedInInProcessExecutionMode()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var host = WorkflowSessionHost.Create(
            Options(path, "--allow-execution", "--in-process-execution"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.ReloadAsync(new ArchiveRequest(path), redirectOnError: null));
    }

    [TestMethod]
    public async Task ANodeCreatedThroughTheToolIsInTheGraphAndSurvivesAReload()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using (var session = WorkflowSession.Create(Options(path)))
        {
            using var created = await InvokeAsync(session, "CreateNode",
                ("fullTypeName", typeof(NodeDefaultViewModel).FullName!));
            Assert.AreEqual("ok", created.RootElement.GetProperty("status").GetString(), created.RootElement.ToString());

            using var nodes = await InvokeAsync(session, "ListNodes");
            Assert.AreEqual(JsonValueKind.Array, nodes.RootElement.ValueKind);
            Assert.AreEqual(2, nodes.RootElement.GetArrayLength());

            var (written, bytes) = session.SaveArchive();
            Assert.AreEqual(path, written);
            Assert.IsTrue(bytes > 0);
        }

        var reloaded = ViewModelSerializer.Deserialize<TreeDefaultViewModel>(File.ReadAllText(path));
        Assert.AreEqual(2, reloaded.Nodes.Count);
    }

    [TestMethod]
    public async Task AConcreteRootTypeIsReadFromTheManifest()
    {
        using var fixture = new TestArchive();
        var tree = new Demo.ViewModels.TreeViewModel();
        tree.GetHelper().CreateNode(new Demo.ViewModels.TimerNodeViewModel());
        var path = fixture.WriteTree(tree);

        // Serializing through the concrete type wrote no root $type; the manifest supplies what the document
        // cannot carry. Without it the loader would silently produce a TreeDefaultViewModel.
        fixture.WriteManifest("""{ "rootType": "Demo.ViewModels.TreeViewModel" }""");

        await using var session = WorkflowSession.Create(Options(path));

        Assert.AreEqual(typeof(Demo.ViewModels.TreeViewModel), session.Scope.Tree.GetType());
        Assert.AreEqual(1, session.Scope.Tree.Nodes.Count);
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task TheRootTypeCanAlsoComeFromTheCommandLine()
    {
        using var fixture = new TestArchive();
        var tree = new Demo.ViewModels.TreeViewModel();
        var path = fixture.WriteTree(tree);

        await using var session = WorkflowSession.Create(
            Options(path, "--root-type", typeof(Demo.ViewModels.TreeViewModel).FullName!));

        Assert.AreEqual(typeof(Demo.ViewModels.TreeViewModel), session.Scope.Tree.GetType());
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task TheWorkspaceRefusesEveryPathOutsideIt()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));
        using var outside = new TestArchive();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => WorkflowSession.Create(Options(path, "--workspace", outside.Directory)));

        await using var session = WorkflowSession.Create(Options(path, "--workspace", fixture.Directory));

        Assert.ThrowsExactly<InvalidOperationException>(
            () => session.SaveArchive(Path.Combine(outside.Directory, "escape.json")));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => session.LoadAssemblies([Path.Combine(outside.Directory, "escape.dll")]));

        var inside = Path.Combine(fixture.Directory, "inside.json");
        session.SaveArchive(inside);
        Assert.IsTrue(File.Exists(inside));
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task TheRedirectPolicyIsSessionLevelAndValidated()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var session = WorkflowSession.Create(Options(path));

        Assert.IsNull(session.Redirect.NodeIndex);
        session.SetRedirectOnError(0);
        Assert.AreEqual(0, session.Redirect.NodeIndex);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.SetRedirectOnError(1));
        session.SetRedirectOnError(null);
        Assert.IsNull(session.Redirect.NodeIndex);
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task TheLogFileReceivesTheRunAndIsNamedInTheResult()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));
        var log = Path.Combine(fixture.Directory, "state", "run.log");

        await using var session = WorkflowSession.Create(
            Options(path, "--allow-execution", "--log-file", log), forceExecution: true);

        using var result = await InvokeAsync(session, "RunCompiledWorkflow", ("startNodeIndex", 0));

        Assert.AreEqual(Path.GetFullPath(log), result.RootElement.GetProperty("logFile").GetString());
        Assert.IsTrue(File.Exists(log));

        // The session still owns the writer; read with a share mode that permits it.
        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        StringAssert.Contains(reader.ReadToEnd(), "NodeDefaultViewModel");
    }

    [TestMethod]
    public async Task TheWorkerPoolIsBoundedByTheOption()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var bounded = WorkflowSessionHost.Create(
            Options(path, "--allow-execution", "--max-concurrent-runs", "3"));
        Assert.AreEqual(3, bounded.RunSlots!.CurrentCount);

        await using var unlimited = WorkflowSessionHost.Create(
            Options(path, "--allow-execution", "--max-concurrent-runs", "0"));
        Assert.IsNull(unlimited.RunSlots);
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task ACheckpointFileIsResolvedAndItsDirectoryCreated()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));
        var checkpoint = Path.Combine(fixture.Directory, "state", "run.checkpoint.json");

        await using var session = WorkflowSession.Create(Options(path, "--checkpoint-file", checkpoint));

        Assert.AreEqual(Path.GetFullPath(checkpoint), session.CheckpointFile);
        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(checkpoint))));
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task ArchiveInfoReportsWhatTheClientNeeds()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var session = WorkflowSession.Create(Options(path));

        using var info = JsonDocument.Parse(session.Describe());
        Assert.AreEqual(path, info.RootElement.GetProperty("archive").GetString());
        Assert.AreEqual(1, info.RootElement.GetProperty("nodeCount").GetInt32());
        Assert.IsFalse(info.RootElement.GetProperty("executionEnabled").GetBoolean());
        Assert.AreEqual(typeof(TreeDefaultViewModel).FullName, info.RootElement.GetProperty("rootType").GetString());
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task ExecutionRunsASingleNodeChainToCompletion()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var session = WorkflowSession.Create(
            CliOptions.Parse(["run", "--archive", path, "--start", "0"]), forceExecution: true);

        using var result = await InvokeAsync(session, "RunCompiledWorkflow", ("startNodeIndex", 0));

        Assert.AreEqual("ok", result.RootElement.GetProperty("status").GetString(), result.RootElement.ToString());
        Assert.AreEqual("Completed", result.RootElement.GetProperty("outcome").GetString(),
            result.RootElement.ToString());
    }
}
