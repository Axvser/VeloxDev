using System.Text.Json;
using Microsoft.Extensions.AI;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.Cli;
using VeloxDev.Serialization;

// Without this, Windows consoles render non-ASCII log lines and help text in the active code page. The MCP
// transport is UTF-8 on the wire; the writer's encoding is only relevant to Console.Out, which MCP mode does
// not use for protocol messages.
Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

try
{
    var options = CliOptions.Parse(args);

    if (options.ShowHelp)
    {
        Console.Out.WriteLine(CliOptions.Usage);
        return 0;
    }

    if (options.Mode == "run")
        return await RunOnceAsync(options);

    await McpHost.RunAsync(options);
    return 0;
}
catch (CliUsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.Usage);
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine("veloxdev: " + ex.Message);
    if (ex.InnerException is not null)
        Console.Error.WriteLine("  cause: " + ex.InnerException.Message);
    return 1;
}

/// <summary>
/// One-shot headless execution. The MCP server is the long-lived entry; this exists so a human or CI can run
/// the same archive without a client, and so the execution path can be tested without a protocol.
/// </summary>
static async Task<int> RunOnceAsync(CliOptions options)
{
    await using var session = WorkflowSession.Create(options, forceExecution: true);

    var toolName = options.NodeResult ? "GetNodeResult" : "RunCompiledWorkflow";
    var tool = session.Toolkit.CreateTools(WorkflowToolCategory.Execution)
        .OfType<AIFunction>()
        .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"{toolName} is not available on this toolkit.");

    var arguments = new AIFunctionArguments
    {
        [options.NodeResult ? "nodeIndex" : "startNodeIndex"] = options.StartNode!.Value,
    };
    if (options.Seed is not null) arguments["seed"] = options.Seed;
    if (options.TimeoutMs is { } timeout) arguments["timeoutMs"] = timeout;

    var result = await tool.InvokeAsync(arguments).ConfigureAwait(false);
    var text = result?.ToString() ?? string.Empty;

    if (!string.IsNullOrWhiteSpace(options.OutputTree))
    {
        try
        {
            var state = System.IO.Path.GetFullPath(options.OutputTree);
            WorkspaceRule.Ensure(session.Workspace, state, "Output tree");
            var directory = System.IO.Path.GetDirectoryName(state);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(state, session.Scope.Tree.Serialize());
            text = WithStateFile(text, state);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("veloxdev: the post-run tree could not be written: " + ex.Message);
        }
    }

    Console.Out.WriteLine(text);

    return IsOk(text) ? 0 : 1;
}

/// <summary>Adds the state path to a successful run result without changing any field the library produced.</summary>
static string WithStateFile(string text, string statePath)
{
    try
    {
        if (System.Text.Json.Nodes.JsonNode.Parse(text) is System.Text.Json.Nodes.JsonObject result)
        {
            result["stateFile"] = statePath;
            return result.ToJsonString();
        }
    }
    catch (JsonException)
    {
        // A non-JSON answer stays exactly as it was; stderr already explains any write failure.
    }

    return text;
}

static bool IsOk(string text)
{
    try
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("status", out var status)
            && string.Equals(status.GetString(), "ok", StringComparison.Ordinal);
    }
    catch (JsonException)
    {
        return false;
    }
}
