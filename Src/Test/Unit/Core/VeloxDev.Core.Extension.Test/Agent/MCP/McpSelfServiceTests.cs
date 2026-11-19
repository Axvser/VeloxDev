using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Safety;

namespace VeloxDev.Core.Extension.Test.Agent.MCP;

/// <summary>
/// The Agent's own two ways into the server list — add one, change one — and who decides whether either runs.
/// </summary>
/// <remarks>
/// <para>
/// Both tools are always registered now. What may run is decided at call time by the session's permission mode
/// and the host's rules, and a refusal names them; the ladder of rungs this replaces kept the tools off the
/// surface instead, which left a model able to say only that it had no such tool.
/// </para>
/// <para>
/// The one question a rule about a <i>tool</i> cannot answer is which <i>kind</i> of server: a local package
/// installs and runs software, an Http endpoint only sends data out. So the tools ask the same policy about a
/// synthesised call naming the kind, and a host writes <c>Deny("mcp-add:local")</c> in its own vocabulary.
/// </para>
/// </remarks>
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

    private static AITool? SetArgumentsTool(McpScope scope)
        => new McpAgentToolkit(scope, []).CreateTools().FirstOrDefault(t => t.Name == "SetMcpServerArguments");

    // ── The tools exist; the policy decides ─────────────────────────────────

    [TestMethod]
    public void BothWriteTools_AreRegisteredOnAPlainScope()
    {
        // There is no rung to climb any more. A tool the model can see and the host has not allowed is a call
        // that comes back refused with the mode's name in it — which is something the user can act on.
        var scope = new McpScope();

        Assert.IsNotNull(AddTool(scope));
        Assert.IsNotNull(SetArgumentsTool(scope));
        Assert.IsTrue(new McpAgentToolkit(scope, []).CreateTools().Any(t => t.Name == "ListMcpServers"));
    }

    // ── Standalone: nothing wraps these tools, so they ask for themselves ───

    [TestMethod]
    public void Standalone_AsksBeforeAdding()
    {
        var asked = 0;
        var scope = new McpScope().WithConfirmationHandler((_, _) => { asked++; return Task.FromResult(true); });

        var json = JObject.Parse(Invoke(AddTool(scope)!,
            ("name", "remote"), ("runMode", "Http"), ("endpoint", "https://mcp.example.invalid/mcp")));

        Assert.AreEqual(1, asked, "nothing else wraps these tools, so the tool is what puts the call to the user");
        Assert.AreEqual("error", json["status"]?.Value<string>(),
            "and what it reports afterwards is the connection's failure, not a refusal: " + json);
    }

    [TestMethod]
    public void Standalone_WithoutAConfirmationHandler_Denies()
    {
        // An unanswerable prompt must deny, not quietly allow.
        var scope = new McpScope();

        var json = JObject.Parse(Invoke(AddTool(scope)!,
            ("name", "unanswerable"), ("runMode", "Http"), ("endpoint", "https://mcp.example.invalid/mcp")));

        Assert.AreEqual("denied", json["status"]?.Value<string>());
        Assert.IsEmpty(scope.LoadedTools);
        Assert.IsEmpty(scope.Status.Servers, "a denied server must not even be tracked");
    }

    [TestMethod]
    public void Standalone_AsksBeforeReconfiguring()
    {
        var asked = 0;
        var scope = new McpScope().WithConfirmationHandler((_, _) => { asked++; return Task.FromResult(false); });
        scope.WithServers(new McpServerConfiguration
        {
            Name = "filesystem",
            RunMode = McpServerRunMode.Npx,
            Package = "@modelcontextprotocol/server-filesystem",
            Arguments = ["C:/data"],
        });

        var json = JObject.Parse(Invoke(SetArgumentsTool(scope)!, ("name", "filesystem"), ("argumentsJson", "[\"C:/other\"]")));

        Assert.AreEqual(1, asked);
        Assert.AreEqual("denied", json["status"]?.Value<string>());
    }

    // ── A rule about the kind ───────────────────────────────────────────────

    /// <summary>A host can forbid installing software without forbidding remote servers.</summary>
    [TestMethod]
    public void ADenyRuleOnTheLocalKind_RefusesALocalInstallAndLeavesRemoteAlone()
    {
        var asked = 0;
        var scope = new McpScope { PermissionCheck = call => call.Name == McpAgentToolkit.LocalKind
            ? PermissionDecision.Deny
            : PermissionDecision.Allow };
        scope.WithConfirmationHandler((_, _) => { asked++; return Task.FromResult(true); });

        var refused = JObject.Parse(Invoke(AddTool(scope)!,
            ("name", "local-thing"), ("runMode", "Npx"), ("package", "@modelcontextprotocol/server-filesystem")));

        Assert.AreEqual("error", refused["status"]?.Value<string>(), refused.ToString());
        StringAssert.Contains(refused["message"]!.Value<string>()!, McpAgentToolkit.LocalKind,
            "the refusal names the rule the host has to remove");
        Assert.AreEqual(0, asked, "a policy refusal must not prompt the user");
        Assert.IsEmpty(scope.LoadedTools);

        // The same rule says nothing about Http: the kind is the whole point of it.
        Assert.AreEqual(PermissionDecision.Allow, scope.Judge(McpAgentToolkit.HttpKind, new Dictionary<string, object?>()));
    }

    /// <summary>And a host can make the local kind ask even where the wrapper would not.</summary>
    /// <remarks>
    /// The wrapper sees the tool's name and not the run mode inside its arguments, so a rule about the kind is
    /// invisible to it. That is why this one question is asked by the tool instead.
    /// </remarks>
    [TestMethod]
    public void APlainScope_IsNotComposed_SoTheToolsAskForThemselves()
    {
        var scope = new McpScope();
        Assert.IsFalse(scope.IsComposed, "nothing attached a policy, so nothing wraps its tools either");

        var composed = new McpScope { PermissionCheck = _ => PermissionDecision.Allow };
        Assert.IsTrue(composed.IsComposed, "the workflow scope sets this when it attaches an MCP scope");
    }

    // ── Validation happens before anything is asked or launched ─────────────

    [TestMethod]
    public void AddServer_MissingEndpointOrPackage_IsAnActionableError()
    {
        var scope = new McpScope();

        var noEndpoint = JObject.Parse(Invoke(AddTool(scope)!, ("name", "x"), ("runMode", "Http")));
        Assert.AreEqual("error", noEndpoint["status"]?.Value<string>());
        Assert.Contains("endpoint", noEndpoint["message"]?.Value<string>() ?? string.Empty);

        var noPackage = JObject.Parse(Invoke(AddTool(scope)!, ("name", "y"), ("runMode", "Npx")));
        Assert.AreEqual("error", noPackage["status"]?.Value<string>());
        Assert.Contains("package", noPackage["message"]?.Value<string>() ?? string.Empty);

        Assert.IsEmpty(scope.Status.Servers, "a malformed request must not even create a status row");
    }

    [TestMethod]
    public void AddServer_UnknownRunMode_IsRefused()
    {
        var scope = new McpScope();

        var json = JObject.Parse(Invoke(AddTool(scope)!, ("name", "x"), ("runMode", "Carrier-Pigeon")));

        Assert.AreEqual("error", json["status"]?.Value<string>());
        StringAssert.Contains(json["message"]!.Value<string>()!, "Npx", "the refusal lists what is valid");
        Assert.IsEmpty(scope.Status.Servers);
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
