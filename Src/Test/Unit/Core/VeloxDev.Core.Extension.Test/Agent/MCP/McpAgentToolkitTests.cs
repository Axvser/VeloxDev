using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.MCP;

namespace VeloxDev.Core.Extension.Test.Agent.MCP;

/// <summary>
/// Coverage for <see cref="McpAgentToolkit"/> — the Agent-facing MCP management tools
/// (<c>ListMcpServers</c> / <c>LoadMcpServers</c>).
/// </summary>
[TestClass]
public class McpAgentToolkitTests
{
    private static string Invoke(AITool tool, params (string Name, object? Value)[] args)
    {
        var callArgs = new AIFunctionArguments();
        foreach (var (n, v) in args)
            callArgs[n] = v;
        var result = ((AIFunction)tool).InvokeAsync(callArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Resources and prompts are reads, so they are on every scope — including the granted view a child gets —
    /// while the tools that change connections still are not.
    /// </summary>
    [TestMethod]
    public void ResourcesAndPrompts_AreReadingTools_OnEveryView()
    {
        var toolkit = new McpAgentToolkit(new McpScope(), []);
        var names = toolkit.CreateTools().Select(t => t.Name).ToArray();

        foreach (var name in new[]
        {
            McpAgentToolkit.ListResourcesName,
            McpAgentToolkit.ReadResourceName,
            McpAgentToolkit.ListPromptsName,
            McpAgentToolkit.GetPromptName,
        })
        {
            CollectionAssert.Contains(names, name);
            CollectionAssert.Contains(McpAgentToolkit.ReadToolNames, name, $"'{name}' must be classified as a read");
        }

        Assert.IsFalse(McpAgentToolkit.ReadToolNames.Contains(McpAgentToolkit.LoadName),
            "loading installs and launches software — it is not a read");
    }

    /// <summary>A read tool answers with an error envelope when the server is not connected, rather than throwing.</summary>
    [TestMethod]
    public void ResourceAndPromptReads_OnAnUnknownServer_ReturnAnErrorEnvelope()
    {
        var toolkit = new McpAgentToolkit(new McpScope(), []);

        foreach (var (toolName, arguments) in new (string, (string, object?)[])[]
        {
            (McpAgentToolkit.ListResourcesName, [("serverName", "missing")]),
            (McpAgentToolkit.ReadResourceName, [("serverName", "missing"), ("uri", "file:///x")]),
            (McpAgentToolkit.ListPromptsName, [("serverName", "missing")]),
            (McpAgentToolkit.GetPromptName, [("serverName", "missing"), ("promptName", "p")]),
        })
        {
            var tool = toolkit.CreateTools().Single(t => t.Name == toolName);
            var json = JObject.Parse(Invoke(tool, arguments));
            Assert.AreEqual("error", json["status"]?.Value<string>(), toolName);
            StringAssert.Contains(json["message"]!.Value<string>()!, "missing", toolName);
        }
    }

    [TestMethod]
    public async Task ListMcpServers_ReturnsTrackedStatus()
    {
        var scope = new McpScope();
        var configs = new McpServerConfiguration[]
        {
            new() { Name = "bad", RunMode = McpServerRunMode.Http, Endpoint = null },
        };
        await scope.LoadAsync(configs);   // bad endpoint → Error status tracked

        var toolkit = new McpAgentToolkit(scope, configs);
        var list = toolkit.CreateTools().Single(t => t.Name == "ListMcpServers");

        var result = Invoke(list);
        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>(), result);
        Assert.AreEqual(1, json["serverCount"]?.Value<int>());
        Assert.AreEqual(1, json["errorCount"]?.Value<int>());

        var server = (json["servers"] as JArray)![0];
        Assert.AreEqual("bad", server["name"]?.Value<string>());
        Assert.AreEqual("Error", server["state"]?.Value<string>());
        Assert.IsFalse(string.IsNullOrEmpty(server["error"]?.Value<string>()));
    }

    [TestMethod]
    public async Task LoadMcpServers_BySubsetName_OnlyLoadsMatching()
    {
        var scope = new McpScope();
        var configs = new McpServerConfiguration[]
        {
            new() { Name = "bad", RunMode = McpServerRunMode.Http, Endpoint = null },
            new() { Name = "other", RunMode = McpServerRunMode.Http, Endpoint = null },
        };
        var toolkit = new McpAgentToolkit(scope, configs);
        var load = toolkit.CreateTools().Single(t => t.Name == "LoadMcpServers");

        // Unknown name → clear error, nothing loaded.
        var unknown = Invoke(load, ("namesJson", "[\"nope\"]"));
        Assert.IsTrue(unknown.Contains("No matching", System.StringComparison.OrdinalIgnoreCase), unknown);

        // Subset load by name → only that server is tracked.
        var result = Invoke(load, ("namesJson", "[\"bad\"]"));
        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>(), result);
        Assert.AreEqual(1, (json["servers"] as JArray)!.Count, "only the requested server is loaded");
        Assert.AreEqual("bad", (json["servers"] as JArray)![0]["name"]?.Value<string>());
        Assert.AreEqual(1, scope.Status.Servers.Count, "status must reflect only the loaded subset");
    }

    [TestMethod]
    public void McpAgentToolkit_ExposesUnloadTool()
    {
        var toolkit = new McpAgentToolkit(new McpScope(), []);

        Assert.IsTrue(toolkit.CreateTools().Any(t => t.Name == "UnloadMcpServer"),
            "UnloadMcpServer must be available for mid-session removal");
    }

    // ── DescribeMcpServer ──

    [TestMethod]
    public void DescribeMcpServer_Unconnected_ReturnsError()
    {
        var toolkit = new McpAgentToolkit(new McpScope(), []);
        var describe = toolkit.CreateTools().Single(t => t.Name == "DescribeMcpServer");

        var result = Invoke(describe, ("serverName", "nope"));
        Assert.IsTrue(result.Contains("no loaded tools", StringComparison.OrdinalIgnoreCase), result);
    }

    [TestMethod]
    public async Task LoadMcpServers_WithoutNames_LoadsAll()
    {
        var scope = new McpScope();
        var configs = new McpServerConfiguration[]
        {
            new() { Name = "a", RunMode = McpServerRunMode.Http, Endpoint = null },
            new() { Name = "b", RunMode = McpServerRunMode.Http, Endpoint = null },
        };
        var toolkit = new McpAgentToolkit(scope, configs);
        var load = toolkit.CreateTools().Single(t => t.Name == "LoadMcpServers");

        var result = Invoke(load);
        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>(), result);
        Assert.AreEqual(2, (json["servers"] as JArray)!.Count);
        Assert.AreEqual(2, scope.Status.Servers.Count);
    }
}
