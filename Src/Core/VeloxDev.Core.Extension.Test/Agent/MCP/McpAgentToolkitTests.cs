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
