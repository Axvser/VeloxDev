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
/// Coverage for the MCP self-service ladder: which servers the Agent may add at each level, when the
/// add tool exists at all, and that a refusal never reaches the point of connecting anything.
/// </summary>
[TestClass]
public class McpSelfServiceTests
{
    private static string Invoke(AITool tool, params (string Name, object? Value)[] args)
    {
        var callArgs = new AIFunctionArguments();
        foreach (var (n, v) in args)
            callArgs[n] = v;
        var result = ((AIFunction)tool).InvokeAsync(callArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    private static AITool? AddTool(McpScope scope)
        => new McpAgentToolkit(scope, []).CreateTools().FirstOrDefault(t => t.Name == "AddMcpServer");

    // ── The gate itself ──────────────────────────────────────────────────────

    [TestMethod]
    public void Closed_DoesNotRegisterTheAddTool()
    {
        // The default level must be the pre-existing behaviour: no tool, so the model never sees the
        // capability and cannot be talked into retrying it.
        var scope = new McpScope();

        Assert.AreEqual(McpSelfServiceLevel.Closed, scope.SelfServiceLevel);
        Assert.IsNull(AddTool(scope));
        Assert.IsFalse(scope.CanAddServer(McpServerRunMode.Http));
        Assert.IsFalse(scope.CanAddServer(McpServerRunMode.Npx));
    }

    [TestMethod]
    public void RemoteConfirmed_OpensRemoteOnly()
    {
        var scope = new McpScope().WithSelfService(McpSelfServiceLevel.RemoteConfirmed);

        Assert.IsNotNull(AddTool(scope));
        Assert.IsTrue(scope.CanAddServer(McpServerRunMode.Http));
        Assert.IsFalse(scope.CanAddServer(McpServerRunMode.Npx), "local servers open one rung later");
        Assert.IsFalse(scope.CanAddServer(McpServerRunMode.Pip));
        Assert.IsFalse(scope.CanAddServer(McpServerRunMode.Dotnet));
        Assert.IsFalse(scope.CanAddServer(McpServerRunMode.Exe));
        Assert.IsTrue(scope.RequiresConfirmationToAdd());
    }

    [TestMethod]
    public void AllConfirmed_OpensLocalTooButStillAsks()
    {
        var scope = new McpScope().WithSelfService(McpSelfServiceLevel.AllConfirmed);

        Assert.IsTrue(scope.CanAddServer(McpServerRunMode.Http));
        Assert.IsTrue(scope.CanAddServer(McpServerRunMode.Npx));
        Assert.IsTrue(scope.CanAddServer(McpServerRunMode.Pip));
        Assert.IsTrue(scope.CanAddServer(McpServerRunMode.Dotnet));
        Assert.IsTrue(scope.CanAddServer(McpServerRunMode.Exe));
        Assert.IsTrue(scope.RequiresConfirmationToAdd());
    }

    [TestMethod]
    public void Unrestricted_OpensEverythingAndStopsAsking()
    {
        var scope = new McpScope().WithSelfService(McpSelfServiceLevel.Unrestricted);

        Assert.IsTrue(scope.CanAddServer(McpServerRunMode.Npx));
        Assert.IsFalse(scope.RequiresConfirmationToAdd());
    }

    // ── Refusals never reach the network or the package manager ──────────────

    [TestMethod]
    public void AddServer_LocalModeAtRemoteConfirmed_IsRefusedWithoutConfirmation()
    {
        var asked = false;
        var scope = new McpScope()
            .WithSelfService(McpSelfServiceLevel.RemoteConfirmed)
            .WithConfirmationHandler((_, _) => { asked = true; return Task.FromResult(true); });

        var json = JObject.Parse(Invoke(AddTool(scope)!,
            ("name", "local-thing"), ("runMode", "Npx"), ("package", "@modelcontextprotocol/server-filesystem")));

        Assert.AreEqual("error", json["status"]?.Value<string>());
        Assert.IsFalse(asked, "a policy refusal must not prompt the user");
        Assert.IsEmpty(scope.LoadedTools);
    }

    [TestMethod]
    public void AddServer_DeniedConfirmation_LoadsNothing()
    {
        var scope = new McpScope()
            .WithSelfService(McpSelfServiceLevel.RemoteConfirmed)
            .WithConfirmationHandler((_, _) => Task.FromResult(false));

        var json = JObject.Parse(Invoke(AddTool(scope)!,
            ("name", "denied"), ("runMode", "Http"), ("endpoint", "https://mcp.example.invalid/mcp")));

        Assert.AreEqual("denied", json["status"]?.Value<string>());
        Assert.IsEmpty(scope.LoadedTools);
        Assert.IsEmpty(scope.Status.Servers, "a denied server must not even be tracked");
    }

    [TestMethod]
    public void AddServer_WithoutAConfirmationHandler_Denies()
    {
        // No handler means the prompt cannot be answered, and an unanswerable prompt must deny rather
        // than quietly allow.
        var scope = new McpScope().WithSelfService(McpSelfServiceLevel.AllConfirmed);

        var json = JObject.Parse(Invoke(AddTool(scope)!,
            ("name", "unanswerable"), ("runMode", "Http"), ("endpoint", "https://mcp.example.invalid/mcp")));

        Assert.AreEqual("denied", json["status"]?.Value<string>());
        Assert.IsEmpty(scope.LoadedTools);
    }

    [TestMethod]
    public void AddServer_MissingEndpointOrPackage_IsAnActionableError()
    {
        var scope = new McpScope()
            .WithSelfService(McpSelfServiceLevel.Unrestricted);

        var noEndpoint = JObject.Parse(Invoke(AddTool(scope)!, ("name", "x"), ("runMode", "Http")));
        Assert.AreEqual("error", noEndpoint["status"]?.Value<string>());
        Assert.Contains("endpoint", noEndpoint["message"]?.Value<string>() ?? string.Empty);

        var noPackage = JObject.Parse(Invoke(AddTool(scope)!, ("name", "y"), ("runMode", "Npx")));
        Assert.AreEqual("error", noPackage["status"]?.Value<string>());
        Assert.Contains("package", noPackage["message"]?.Value<string>() ?? string.Empty);

        Assert.IsEmpty(scope.Status.Servers);
    }

    [TestMethod]
    public void AddServer_UnknownRunMode_IsRefused()
    {
        var scope = new McpScope().WithSelfService(McpSelfServiceLevel.Unrestricted);

        var json = JObject.Parse(Invoke(AddTool(scope)!, ("name", "z"), ("runMode", "Telepathy")));

        Assert.AreEqual("error", json["status"]?.Value<string>());
        Assert.IsEmpty(scope.Status.Servers);
    }

    [TestMethod]
    public async Task AddAsync_DoesNotDisturbAlreadyLoadedServers()
    {
        // The replace path clears everything, so adding must go through the incremental one — otherwise
        // asking the Agent for one more server would silently drop the ones already working.
        var scope = new McpScope();
        var preexisting = new McpServerConfiguration
        {
            Name = "preexisting", RunMode = McpServerRunMode.Http, Endpoint = null,   // fails fast, but is tracked
        };
        await scope.AddAsync(preexisting);
        Assert.HasCount(1, scope.Status.Servers);

        await scope.AddAsync(new McpServerConfiguration
        {
            Name = "second", RunMode = McpServerRunMode.Http, Endpoint = null,
        });

        Assert.HasCount(2, scope.Status.Servers, "adding must not unload the servers already there");
        CollectionAssert.Contains(scope.Status.Servers.Select(s => s.Name).ToArray(), "preexisting");
        CollectionAssert.Contains(scope.Status.Servers.Select(s => s.Name).ToArray(), "second");
    }

    [TestMethod]
    public async Task AddAsync_EachFailureIsRecordedOnItsOwnStatusEntry()
    {
        var scope = new McpScope();
        await scope.AddAsync(new McpServerConfiguration { Name = "ok-one", RunMode = McpServerRunMode.Http, Endpoint = null });
        await scope.AddAsync(new McpServerConfiguration { Name = "bad-one", RunMode = McpServerRunMode.Http, Endpoint = null });

        Assert.AreEqual(2, scope.Status.ErrorCount);
        Assert.IsNotNull(scope.Status.Servers.Single(s => s.Name == "bad-one").Error);
        Assert.IsNotNull(scope.Status.Servers.Single(s => s.Name == "ok-one").Error);
    }

    [TestMethod]
    public async Task UnloadServerAsync_ReleasesOneServerAndLeavesTheRest()
    {
        var scope = new McpScope();
        await scope.AddAsync(new McpServerConfiguration { Name = "a", RunMode = McpServerRunMode.Http, Endpoint = null });
        await scope.AddAsync(new McpServerConfiguration { Name = "b", RunMode = McpServerRunMode.Http, Endpoint = null });

        var removed = await scope.UnloadServerAsync("a");

        Assert.IsFalse(removed, "neither server connected, so neither had tools to remove");
        Assert.HasCount(2, scope.Status.Servers, "unloading must not drop the status of other servers");
        Assert.AreEqual(McpServerStatus.NotStarted, scope.Status.Servers.Single(s => s.Name == "a").State);
    }

    [TestMethod]
    public async Task StatusSnapshot_ReflectsAddedServers()
    {
        // The prompt's MCP inventory is rendered off the UI thread, so it reads the snapshot rather than
        // the bound collection. The snapshot must follow every add and removal.
        var scope = new McpScope();
        Assert.IsEmpty(scope.Status.Snapshot);

        await scope.AddAsync(new McpServerConfiguration { Name = "a", RunMode = McpServerRunMode.Http, Endpoint = null });
        await scope.AddAsync(new McpServerConfiguration { Name = "b", RunMode = McpServerRunMode.Http, Endpoint = null });

        Assert.HasCount(2, scope.Status.Snapshot);
        var first = scope.Status.Snapshot.Single(s => s.Name == "a");
        Assert.AreEqual("错误", first.StateText, "the snapshot must carry the state text the prompt renders");
        Assert.AreEqual(0, first.ToolCount);

        // Unloading resets a server's state but deliberately keeps its status row: the panel lists every
        // server the host has registered, including the ones currently switched off.
        await scope.UnloadServerAsync("a");
        Assert.HasCount(2, scope.Status.Snapshot, "the status row outlives the connection");
        Assert.AreEqual("未启动", scope.Status.Snapshot.Single(s => s.Name == "a").StateText,
            "but its state must show it is no longer running");
    }

    [TestMethod]
    public async Task Version_AdvancesWhenServersComeAndGo()
    {
        var scope = new McpScope();
        var start = scope.Version;

        await scope.AddAsync(new McpServerConfiguration { Name = "a", RunMode = McpServerRunMode.Http, Endpoint = null });
        var afterAdd = scope.Version;
        Assert.IsTrue(afterAdd > start, "adding a server must invalidate a cached tool list");

        await scope.UnloadServerAsync("a");
        Assert.IsTrue(scope.Version > afterAdd, "unloading a server must invalidate it too");
    }
}
