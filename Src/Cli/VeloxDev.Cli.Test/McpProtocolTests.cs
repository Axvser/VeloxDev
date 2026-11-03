using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace VeloxDev.Cli.Test;

/// <summary>
/// Drives the built executable over stdio, the way a real MCP client launches it. Everything else in this suite
/// tests the objects behind the tools; this one tests the transport, the handshake and the process boundary.
/// </summary>
[TestClass]
public class McpProtocolTests
{
    [TestMethod]
    public async Task TheServerAnswersOverStdioAndCallsTheLoadedGraph()
    {
        using var fixture = new TestArchive();
        var archive = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));
        var log = Path.Combine(fixture.Directory, "run.log");

        // The test output flattens the dependency graph for net10 and would hand the net8 CLI a net10 copy of a
        // shared library, so launch the CLI from its own output folder. Four levels up from the test's net10.0
        // folder is Src/Cli; the configuration name is shared by both projects.
        var (cli, cliDirectory) = CliLocation();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [cli, "mcp", "--archive", archive, "--allow-execution", "--log-file", log],
            Name = "veloxdev-cli-test",
            WorkingDirectory = cliDirectory,
            StandardErrorLines = _ => { },
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

        var tools = await client.ListToolsAsync();
        Assert.IsTrue(tools.Any(t => t.Name == "ListNodes"), "the native inspection tools must be on the surface");
        Assert.IsTrue(tools.Any(t => t.Name == "CreateNode"), "the native editing tools must be on the surface");
        Assert.IsTrue(tools.Any(t => t.Name == "RunCompiledWorkflow"), "execution was explicitly allowed");
        Assert.IsTrue(tools.Any(t => t.Name == "GetNodeResult"), "the terminal role is exposed too");
        Assert.IsTrue(tools.Any(t => t.Name == "SetRedirectOnError"), "the host redirect setting is agent-visible");
        Assert.IsFalse(tools.Any(t => t.Name == "PauseCompiledRun"),
            "the default surface is isolated; handle-based runs are not in the server process");

        var resources = await client.ListResourcesAsync(new ListResourcesRequestParams());
        Assert.IsTrue(resources.Resources.Any(r => r.Uri.ToString() == "veloxdev://archive/info"),
            "the archive info must be readable as a resource too");
        var infoResource = await client.ReadResourceAsync(
            new ReadResourceRequestParams { Uri = "veloxdev://archive/info" });
        var infoText = string.Join("", infoResource.Contents.OfType<TextResourceContents>().Select(c => c.Text));
        StringAssert.Contains(infoText, "\"status\":\"ok\"");

        var prompts = await client.ListPromptsAsync(new ListPromptsRequestParams());
        Assert.IsTrue(prompts.Prompts.Any(p => p.Name == "workflow-operator"));
        var prompt = await client.GetPromptAsync(new GetPromptRequestParams { Name = "workflow-operator" });
        Assert.IsTrue(prompt.Messages.Count > 0, "the operator prompt must carry instructions");

        var result = await client.CallToolAsync("ListNodes", new Dictionary<string, object?>());
        var text = string.Join("\n", result.Content.Select(c => c.ToString()));
        StringAssert.Contains(text, "NodeDefaultViewModel");

        // This is the worker path end to end: the server serializes the live graph, launches a child, and returns
        // the child's result.
        var run = await client.CallToolAsync("RunCompiledWorkflow",
            new Dictionary<string, object?> { ["startNodeIndex"] = 0 });
        var runText = string.Join("", run.Content.OfType<TextContentBlock>().Select(c => c.Text));
        StringAssert.Contains(runText, "\"outcome\":\"Completed\"");
        StringAssert.Contains(runText, "run.log", "the isolated run must report the log file it wrote");
        Assert.IsTrue(File.Exists(log));
        StringAssert.Contains(File.ReadAllText(log), "NodeDefaultViewModel");

        // The worker wrote its post-run tree. Adopting it is an explicit LoadArchive; no state is merged behind
        // the client's back.
        using var runJson = JsonDocument.Parse(runText);
        var stateFile = runJson.RootElement.GetProperty("stateFile").GetString();
        Assert.IsTrue(File.Exists(stateFile), $"the worker's post-run state was not written: {stateFile}");

        var adopted = await client.CallToolAsync("LoadArchive",
            new Dictionary<string, object?> { ["path"] = stateFile! });
        StringAssert.Contains(
            string.Join("", adopted.Content.OfType<TextContentBlock>().Select(c => c.Text)),
            "\"nodeCount\":1");

        // LoadArchive replaces the session behind the already-registered tools. The tool list must stay usable.
        using var second = new TestArchive();
        var secondTree = new VeloxDev.WorkflowSystem.TreeDefaultViewModel();
        secondTree.GetHelper().CreateNode(new VeloxDev.WorkflowSystem.NodeDefaultViewModel());
        secondTree.GetHelper().CreateNode(new VeloxDev.WorkflowSystem.NodeDefaultViewModel());
        var secondPath = second.WriteTree(secondTree);

        var loaded = await client.CallToolAsync("LoadArchive",
            new Dictionary<string, object?> { ["path"] = secondPath });
        var loadedText = string.Join("\n", loaded.Content.Select(c => c.ToString()));
        StringAssert.Contains(loadedText, "\"nodeCount\":2");

        var after = await client.CallToolAsync("ListNodes", new Dictionary<string, object?>());
        var afterText = string.Join("\n", after.Content.Select(c => c.ToString()));
        Assert.AreEqual(2, CountOccurrences(afterText, "NodeDefaultViewModel"),
            $"the graph after the swap is the second archive's, not the first: {afterText}");
    }

    [TestMethod]
    public async Task LoadArchiveCanBringInANewComponentAssembly()
    {
        using var first = new TestArchive();
        var firstPath = first.WriteTree(TestArchive.TreeWithOneNode(out _));

        using var second = new TestArchive();
        var secondTree = new VeloxDev.WorkflowSystem.TreeDefaultViewModel();
        secondTree.GetHelper().CreateNode(new VeloxDev.Cli.Test.Plugin.CrashingNodeViewModel());
        var secondPath = second.WriteTree(secondTree);
        var plugin = typeof(VeloxDev.Cli.Test.Plugin.CrashingNodeViewModel).Assembly.Location;

        var (cli, cliDirectory) = CliLocation();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [cli, "mcp", "--archive", firstPath],
            Name = "veloxdev-reload-plugin-test",
            WorkingDirectory = cliDirectory,
            StandardErrorLines = _ => { },
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

        var loaded = await client.CallToolAsync("LoadArchive", new Dictionary<string, object?>
        {
            ["path"] = secondPath,
            ["assemblies"] = new[] { plugin },
        });
        StringAssert.Contains(
            string.Join("", loaded.Content.OfType<TextContentBlock>().Select(c => c.Text)),
            "\"nodeCount\":1");

        var types = await client.CallToolAsync("ListCreatableTypes", new Dictionary<string, object?>());
        var typesText = string.Join("", types.Content.OfType<TextContentBlock>().Select(c => c.Text));
        StringAssert.Contains(typesText, "CrashingNodeViewModel",
            "the assembly loaded by LoadArchive must be visible to the new session");
    }

    [TestMethod]
    public async Task AWorkerCrash_IsReportedAndTheServerSurvives()
    {
        // The node calls Environment.Exit(42). The worker must die; the MCP server must answer the next call.
        using var fixture = new TestArchive();
        var tree = new VeloxDev.WorkflowSystem.TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new VeloxDev.Cli.Test.Plugin.CrashingNodeViewModel());
        var archive = fixture.WriteTree(tree);
        var plugin = typeof(VeloxDev.Cli.Test.Plugin.CrashingNodeViewModel).Assembly.Location;

        var (cli, cliDirectory) = CliLocation();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [cli, "mcp", "--archive", archive, "--assembly", plugin, "--allow-execution"],
            Name = "veloxdev-crash-test",
            WorkingDirectory = cliDirectory,
            StandardErrorLines = _ => { },
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

        var crash = await client.CallToolAsync("RunCompiledWorkflow",
            new Dictionary<string, object?> { ["startNodeIndex"] = 0 });
        var crashText = string.Join("\n", crash.Content.Select(c => c.ToString()));
        StringAssert.Contains(crashText, "error", "a dead worker is an error result, not a dead server");
        StringAssert.Contains(crashText, "42", "the worker's exit code must reach the caller");

        var info = await client.CallToolAsync("ArchiveInfo", new Dictionary<string, object?>());
        var infoText = string.Join("\n", info.Content.Select(c => c.ToString()));
        StringAssert.Contains(infoText, "\"status\":\"ok\"", "the server must still be serving after the crash");
    }

    [TestMethod]
    public async Task AHangingWorker_IsKilledAndReportedAsCancelled()
    {
        using var fixture = new TestArchive();
        var tree = new VeloxDev.WorkflowSystem.TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new VeloxDev.Cli.Test.Plugin.HangingNodeViewModel());
        var archive = fixture.WriteTree(tree);
        var plugin = typeof(VeloxDev.Cli.Test.Plugin.HangingNodeViewModel).Assembly.Location;

        var (cli, cliDirectory) = CliLocation();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments =
            [
                cli, "mcp", "--archive", archive, "--assembly", plugin, "--allow-execution",
                "--worker-kill-grace-ms", "200",
            ],
            Name = "veloxdev-hang-test",
            WorkingDirectory = cliDirectory,
            StandardErrorLines = _ => { },
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

        var result = await client.CallToolAsync("RunCompiledWorkflow",
            new Dictionary<string, object?> { ["startNodeIndex"] = 0, ["timeoutMs"] = 100 });
        var text = string.Join("\n", result.Content.Select(c => c.ToString()));

        StringAssert.Contains(text, "\"outcome\":\"Cancelled\"", text);
        StringAssert.Contains(text, "\"timedOut\":true", text);

        var info = await client.CallToolAsync("ArchiveInfo", new Dictionary<string, object?>());
        StringAssert.Contains(string.Join("\n", info.Content.Select(c => c.ToString())), "\"status\":\"ok\"");
    }

    [TestMethod]
    public async Task CancellingARunKillsTheWorkerAndLeavesTheServerAlive()
    {
        using var fixture = new TestArchive();
        var tree = new VeloxDev.WorkflowSystem.TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new VeloxDev.Cli.Test.Plugin.HangingNodeViewModel());
        var archive = fixture.WriteTree(tree);
        var plugin = typeof(VeloxDev.Cli.Test.Plugin.HangingNodeViewModel).Assembly.Location;

        var (cli, cliDirectory) = CliLocation();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments =
            [
                cli, "mcp", "--archive", archive, "--assembly", plugin, "--allow-execution",
                "--worker-kill-grace-ms", "200",
            ],
            Name = "veloxdev-cancel-test",
            WorkingDirectory = cliDirectory,
            StandardErrorLines = _ => { },
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        try
        {
            await client.CallToolAsync(
                "RunCompiledWorkflow",
                new Dictionary<string, object?> { ["startNodeIndex"] = 0 },
                cancellationToken: cancel.Token);
            Assert.Fail("the hanging run should have been cancelled");
        }
        catch (OperationCanceledException)
        {
            // expected
        }

        var info = await client.CallToolAsync("ArchiveInfo", new Dictionary<string, object?>());
        StringAssert.Contains(
            string.Join("", info.Content.OfType<TextContentBlock>().Select(c => c.Text)),
            "\"status\":\"ok\"",
            "the server must still serve after a cancelled worker");
    }

    [TestMethod]
    public async Task BackgroundJobsRunAndReportTheirResult()
    {
        using var fixture = new TestArchive();
        var archive = fixture.WriteTree(TestArchive.TreeWithOneNode(out _));

        var (cli, cliDirectory) = CliLocation();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [cli, "mcp", "--archive", archive, "--allow-execution"],
            Name = "veloxdev-job-test",
            WorkingDirectory = cliDirectory,
            StandardErrorLines = _ => { },
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

        var started = await client.CallToolAsync("StartWorkflow",
            new Dictionary<string, object?> { ["startNodeIndex"] = 0 });
        var startedText = string.Join("", started.Content.OfType<TextContentBlock>().Select(c => c.Text));
        using var startedJson = JsonDocument.Parse(startedText);
        var id = startedJson.RootElement.GetProperty("id").GetString()!;

        var state = await WaitForJobStateAsync(client, id, "Completed");
        StringAssert.Contains(state, "\"outcome\":\"Completed\"", state);

        var list = await client.CallToolAsync("ListWorkflowJobs", new Dictionary<string, object?>());
        StringAssert.Contains(
            string.Join("", list.Content.OfType<TextContentBlock>().Select(c => c.Text)), id);
    }

    [TestMethod]
    public async Task ABackgroundJobCanBeCancelled()
    {
        using var fixture = new TestArchive();
        var tree = new VeloxDev.WorkflowSystem.TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new VeloxDev.Cli.Test.Plugin.HangingNodeViewModel());
        var archive = fixture.WriteTree(tree);
        var plugin = typeof(VeloxDev.Cli.Test.Plugin.HangingNodeViewModel).Assembly.Location;

        var (cli, cliDirectory) = CliLocation();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments =
            [
                cli, "mcp", "--archive", archive, "--assembly", plugin, "--allow-execution",
                "--worker-kill-grace-ms", "200",
            ],
            Name = "veloxdev-job-cancel-test",
            WorkingDirectory = cliDirectory,
            StandardErrorLines = _ => { },
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

        var started = await client.CallToolAsync("StartWorkflow",
            new Dictionary<string, object?> { ["startNodeIndex"] = 0 });
        var startedText = string.Join("", started.Content.OfType<TextContentBlock>().Select(c => c.Text));
        using var startedJson = JsonDocument.Parse(startedText);
        var id = startedJson.RootElement.GetProperty("id").GetString()!;

        await WaitForJobStateAsync(client, id, "Running");
        var cancelled = await client.CallToolAsync("CancelWorkflowJob",
            new Dictionary<string, object?> { ["id"] = id });
        StringAssert.Contains(
            string.Join("", cancelled.Content.OfType<TextContentBlock>().Select(c => c.Text)), "ok");

        await WaitForJobStateAsync(client, id, "Cancelled");

        var info = await client.CallToolAsync("ArchiveInfo", new Dictionary<string, object?>());
        StringAssert.Contains(
            string.Join("", info.Content.OfType<TextContentBlock>().Select(c => c.Text)), "\"status\":\"ok\"");
    }

    private static async Task<string> WaitForJobStateAsync(McpClient client, string id, string expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        string text = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            var result = await client.CallToolAsync("GetWorkflowJob",
                new Dictionary<string, object?> { ["id"] = id });
            text = string.Join("", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
            if (text.Contains($"\"state\":\"{expected}\"", StringComparison.Ordinal)) return text;
            await Task.Delay(100);
        }

        Assert.Fail($"The job did not reach {expected} in time: {text}");
        return text;
    }

    private static (string Cli, string Directory) CliLocation()
    {
        var testDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = testDirectory.Parent!.Name;
        var cliDirectory = Path.GetFullPath(Path.Combine(
            testDirectory.FullName, "..", "..", "..", "..",
            "VeloxDev.Cli", "bin", configuration, "net8.0"));
        var cli = Path.Combine(cliDirectory, "veloxdev.dll");
        Assert.IsTrue(File.Exists(cli), $"The CLI was not built at its own output: {cli}");
        return (cli, cliDirectory);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }
}
