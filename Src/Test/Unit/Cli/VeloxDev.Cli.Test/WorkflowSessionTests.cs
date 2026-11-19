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

            var saved = session.SaveArchive();
            Assert.AreEqual(path, saved.Path);
            Assert.IsTrue(saved.Bytes > 0);
            Assert.AreEqual("compact", saved.Format);
            Assert.AreEqual(path + ".bak", saved.BackupPath);
        }

        var reloaded = ViewModelSerializer.Deserialize<TreeDefaultViewModel>(File.ReadAllText(path));
        Assert.AreEqual(2, reloaded.Nodes.Count);
    }

    [TestMethod]
    public async Task SaveArchiveDryRunReportsWithoutTouchingTheFile()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var session = WorkflowSession.Create(Options(path));

        var before = File.ReadAllBytes(path);
        var beforeWrite = File.GetLastWriteTimeUtc(path);

        using var created = await InvokeAsync(session, "CreateNode",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!));
        Assert.AreEqual("ok", created.RootElement.GetProperty("status").GetString(), created.RootElement.ToString());

        var dry = session.SaveArchive(dryRun: true);

        Assert.AreEqual(path, dry.Path);
        Assert.AreEqual(path, dry.ArchivePathAfterSave);
        Assert.AreEqual(2, dry.NodeCount);
        Assert.IsNull(dry.BackupPath);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path), "dry run must not rewrite the archive");
        Assert.AreEqual(beforeWrite, File.GetLastWriteTimeUtc(path), "dry run must not touch the file timestamp");
        Assert.IsFalse(File.Exists(path + ".bak"), "dry run must not create a backup");
    }

    [TestMethod]
    public async Task SaveArchiveBacksUpThePreviousDocumentAndWritesCompactByDefault()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var session = WorkflowSession.Create(Options(path));

        var first = session.SaveArchive();
        var firstDocument = File.ReadAllText(path);
        Assert.AreEqual("compact", first.Format);

        using var created = await InvokeAsync(session, "CreateNode",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!));
        Assert.AreEqual("ok", created.RootElement.GetProperty("status").GetString(), created.RootElement.ToString());

        var second = session.SaveArchive();

        Assert.AreEqual(path + ".bak", second.BackupPath);
        Assert.IsTrue(File.Exists(path + ".bak"));
        Assert.AreEqual(firstDocument, File.ReadAllText(path + ".bak"), "the backup is the previous document");
        Assert.IsFalse(File.ReadAllText(path).Contains('\n'), "compact output has no formatting newlines");

        var reloaded = ViewModelSerializer.Deserialize<TreeDefaultViewModel>(File.ReadAllText(path));
        Assert.AreEqual(2, reloaded.Nodes.Count);
    }

    [TestMethod]
    public async Task SaveArchiveCanWriteAnIndentedDocumentWithoutABackup()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var session = WorkflowSession.Create(Options(path));

        var saved = session.SaveArchive(backup: false, compact: false);

        Assert.AreEqual("indented", saved.Format);
        Assert.IsNull(saved.BackupPath);
        Assert.IsFalse(File.Exists(path + ".bak"));
        Assert.IsTrue(File.ReadAllText(path).Contains('\n'), "indented output keeps line breaks");
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
        Assert.AreEqual(
            typeof(IWorkflowTreeViewModel).Assembly.GetName().Version?.ToString(),
            info.RootElement.GetProperty("coreVersion").GetString(),
            "coreVersion must name the Core assembly, not the Extension assembly");
        Assert.AreEqual(
            typeof(VeloxDev.AI.Workflow.WorkflowAgentScope).Assembly.GetName().Version?.ToString(),
            info.RootElement.GetProperty("extensionVersion").GetString());
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

    [TestMethod]
    public async Task CapabilitiesAndAuditExposeTheSessionSurface()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var host = WorkflowSessionHost.Create(Options(path));
        var archiveTools = new ArchiveTools(host);

        using var capabilities = JsonDocument.Parse(archiveTools.GetCapabilities());
        var root = capabilities.RootElement;
        var tools = root.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(tools.Contains("ListNodes"));
        Assert.IsTrue(tools.Contains("SaveArchive"));
        Assert.IsTrue(tools.Contains("GetCapabilities"));
        Assert.AreEqual(tools.Count, root.GetProperty("toolCount").GetInt32());
        Assert.AreEqual("disabled", root.GetProperty("executionMode").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("compatibility").GetProperty("status").GetString()));

        await host.InvokeToolAsync("ListNodes", new AIFunctionArguments(), CancellationToken.None);
        archiveTools.SaveArchive(dryRun: true);

        using var audit = JsonDocument.Parse(archiveTools.GetAuditLog());
        var names = audit.RootElement.GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("tool").GetString())
            .ToList();
        CollectionAssert.Contains(names, "ListNodes");
        CollectionAssert.Contains(names, "SaveArchive");
    }

    [TestMethod]
    public async Task SingleArchiveRefusesReloadAndKeepsRuntimeIdsStable()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));
        await using var host = WorkflowSessionHost.Create(Options(path, "--single-archive"));
        var archiveTools = new ArchiveTools(host);

        using var before = await InvokeAsync(host.Current, "ListNodes");
        var idBefore = before.RootElement[0].GetProperty("id").GetString();

        using var reload = JsonDocument.Parse(await archiveTools.LoadArchive(path));
        Assert.AreEqual("error", reload.RootElement.GetProperty("status").GetString());
        StringAssert.Contains(reload.RootElement.GetProperty("message").GetString(), "single-archive");

        using var after = await InvokeAsync(host.Current, "ListNodes");
        Assert.AreEqual(idBefore, after.RootElement[0].GetProperty("id").GetString());

        using var info = JsonDocument.Parse(host.Current.Describe());
        Assert.IsTrue(info.RootElement.GetProperty("singleArchive").GetBoolean());
        Assert.AreEqual("process", info.RootElement.GetProperty("runtimeIdScope").GetString());
    }

    [TestMethod]
    public async Task InProcessExecutionWritesACsvLogIntoTheTaskDirectory()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));
        await using var host = WorkflowSessionHost.Create(
            Options(path, "--allow-execution", "--in-process-execution"));

        var arguments = new AIFunctionArguments { ["startNodeIndex"] = 0 };
        var raw = await host.InvokeToolAsync("RunCompiledWorkflow", arguments, CancellationToken.None);
        var text = raw as string ?? raw?.ToString() ?? string.Empty;
        using var result = JsonDocument.Parse(text);

        Assert.AreEqual("ok", result.RootElement.GetProperty("status").GetString(), text);
        var csv = result.RootElement.GetProperty("csvFile").GetString();
        Assert.IsNotNull(csv, text);
        Assert.IsTrue(File.Exists(csv), csv);

        var content = File.ReadAllText(csv!);
        StringAssert.StartsWith(content, "seq,record,level,phase,order,attempt,node,message");
        StringAssert.Contains(content, "NodeDefaultViewModel");
    }

    [TestMethod]
    public async Task ApplyBatchCreatesBothNodesInOneCall()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));
        await using var host = WorkflowSessionHost.Create(Options(path));
        var archiveTools = new ArchiveTools(host);

        var operations = JsonSerializer.Serialize(new object[]
        {
            new { tool = "CreateNode", arguments = new { fullTypeName = typeof(NodeDefaultViewModel).FullName! } },
            new { tool = "CreateNode", arguments = new { fullTypeName = typeof(NodeDefaultViewModel).FullName! } },
        });

        using var answer = JsonDocument.Parse(await archiveTools.ApplyBatch(operations));
        Assert.AreEqual("ok", answer.RootElement.GetProperty("status").GetString(), answer.RootElement.ToString());
        Assert.AreEqual(2, answer.RootElement.GetProperty("operationCount").GetInt32());
        Assert.AreEqual(0, answer.RootElement.GetProperty("failedCount").GetInt32());
        Assert.IsFalse(answer.RootElement.GetProperty("rolledBack").GetBoolean());

        using var nodes = await InvokeAsync(host.Current, "ListNodes");
        Assert.AreEqual(3, nodes.RootElement.GetArrayLength(), "one original node plus two created nodes");
    }

    [TestMethod]
    public async Task ApplyBatchRollsBackToTheSnapshotOnFailure()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));
        await using var host = WorkflowSessionHost.Create(Options(path));
        var archiveTools = new ArchiveTools(host);

        var operations = JsonSerializer.Serialize(new object[]
        {
            new { tool = "CreateNode", arguments = new { fullTypeName = typeof(NodeDefaultViewModel).FullName! } },
            new { tool = "CreateNode", arguments = new { fullTypeName = "No.Such.NodeType" } },
        });

        using var answer = JsonDocument.Parse(await archiveTools.ApplyBatch(operations));
        Assert.AreEqual("error", answer.RootElement.GetProperty("status").GetString(), answer.RootElement.ToString());
        Assert.AreEqual(1, answer.RootElement.GetProperty("failedCount").GetInt32());
        Assert.IsTrue(answer.RootElement.GetProperty("rolledBack").GetBoolean());

        var stateFile = answer.RootElement.GetProperty("stateFile").GetString();
        Assert.IsNotNull(stateFile);
        Assert.IsTrue(File.Exists(stateFile));

        try
        {
            using var nodes = await InvokeAsync(host.Current, "ListNodes");
            Assert.AreEqual(1, nodes.RootElement.GetArrayLength(), "the failed batch left the pre-batch graph");
        }
        finally
        {
            File.Delete(stateFile!);
        }
    }

    [TestMethod]
    public async Task NoRunCsv_OmitsTheCsvFileFromExecutionResults()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var host = WorkflowSessionHost.Create(
            Options(path, "--allow-execution", "--in-process-execution", "--no-run-csv"));
        var archiveTools = new ArchiveTools(host);

        using var info = JsonDocument.Parse(archiveTools.ArchiveInfo());
        Assert.IsFalse(info.RootElement.GetProperty("runCsv").GetBoolean());

        var result = await host.InvokeToolAsync(
            "RunCompiledWorkflow",
            new AIFunctionArguments { ["startNodeIndex"] = 0 },
            CancellationToken.None);
        var text = result?.ToString() ?? string.Empty;

        StringAssert.Contains(text, "\"status\":\"ok\"");
        Assert.IsFalse(text.Contains("csvFile", StringComparison.Ordinal),
            "the result must not name a CSV the option told it not to write");
    }

    [TestMethod]
    public async Task CapabilitiesIncludeRedirectToolsWhenExecutionIsEnabled()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        await using var host = WorkflowSessionHost.Create(Options(path, "--allow-execution"));
        var archiveTools = new ArchiveTools(host);

        using var capabilities = JsonDocument.Parse(archiveTools.GetCapabilities());
        var root = capabilities.RootElement;
        var tools = root.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        // RedirectTools are registered by McpHost whenever execution is on; the capability manifest must include them
        // in both execution modes, or a client reading only this tool believes they do not exist.
        Assert.IsTrue(tools.Contains("SetRedirectOnError"), "the host-level redirect must be discoverable");
        Assert.IsTrue(tools.Contains("ClearRedirectOnError"), "and so must clearing it");
        Assert.AreEqual(tools.Count, root.GetProperty("toolCount").GetInt32(),
            "toolCount must be the size of the reported surface");
    }

    [TestMethod]
    public async Task ApplyBatchRejectsArchiveAndQueryToolsUpFront()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));
        await using var host = WorkflowSessionHost.Create(Options(path));
        var archiveTools = new ArchiveTools(host);

        var operations = JsonSerializer.Serialize(new object[]
        {
            new { tool = "LoadArchive", arguments = new { path } },
        });

        using var answer = JsonDocument.Parse(await archiveTools.ApplyBatch(operations));
        Assert.AreEqual("error", answer.RootElement.GetProperty("status").GetString());
        StringAssert.Contains(answer.RootElement.GetProperty("message").GetString(), "Not batchable");
    }
}
