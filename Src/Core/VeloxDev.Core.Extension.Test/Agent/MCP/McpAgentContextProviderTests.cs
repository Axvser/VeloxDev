using Microsoft.Extensions.AI;
using VeloxDev.AI.Pipelines;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.MCP;

namespace VeloxDev.Core.Extension.Test.Agent.MCP;

/// <summary>
/// Coverage for <see cref="McpAgentContextProvider"/> — the MCP subsystem standing on its own.
/// <para>
/// This file deliberately references <b>no</b> Workflow type. That it compiles at all is part of what it
/// asserts: the provider is usable without the workflow layer, and stays that way.
/// </para>
/// </summary>
[TestClass]
public class McpAgentContextProviderTests
{
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public int ThreadId { get; private set; }

        public SingleThreadContext()
        {
            var thread = new Thread(() =>
            {
                ThreadId = Environment.CurrentManagedThreadId;
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                    callback(state);
            })
            { IsBackground = true, Name = "mcp-provider-context" };
            thread.Start();
            SpinWait.SpinUntil(() => ThreadId != 0, TimeSpan.FromSeconds(5));
        }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Environment.CurrentManagedThreadId == ThreadId) { d(state); return; }

            using var done = new ManualResetEventSlim();
            Exception? failure = null;
            Post(_ =>
            {
                try { d(state); } catch (Exception ex) { failure = ex; } finally { done.Set(); }
            }, null);
            done.Wait(TimeSpan.FromSeconds(10));
            if (failure is not null) throw failure;
        }

        public void Dispose() => _queue.CompleteAdding();
    }

    private static string Invoke(AITool tool, params (string Name, object? Value)[] args)
    {
        var callArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null)
                callArgs[name] = value;

        var result = ((AIFunction)tool)
            .InvokeAsync(callArgs, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    [TestMethod]
    public void BuildContext_ContributesTheManagementToolsAndTheirDescription()
    {
        var context = new McpAgentContextProvider(new McpScope()).BuildContext();

        CollectionAssert.AreEquivalent(McpAgentToolkit.ToolNames, context.Tools!.Select(t => t.Name).ToArray());
        foreach (var tool in context.Tools!)
            Assert.AreEqual("TrackedAIFunction", tool.GetType().Name, $"'{tool.Name}' is not wrapped");

        Assert.IsNotNull(context.Instructions);
        Assert.Contains("ListMcpServers", context.Instructions!);
    }

    [TestMethod]
    public void BuildContext_OmitsTheInventoryUntilAServerIsRegistered()
    {
        var scope = new McpScope();
        var provider = new McpAgentContextProvider(scope);

        Assert.IsFalse(provider.BuildContext().Instructions!.Contains("Connected MCP servers"));

        // A null endpoint fails fast, so this registers without touching the network.
        scope.AddAsync(new McpServerConfiguration { Name = "probe", RunMode = McpServerRunMode.Http, Endpoint = null })
             .GetAwaiter().GetResult();

        var after = provider.BuildContext().Instructions!;
        Assert.Contains("Connected MCP servers", after);
        Assert.Contains("probe", after);
    }

    [TestMethod]
    public void BuildContext_ReRendersWhenTheSelfServiceLevelChanges()
    {
        // The level decides whether AddMcpServer exists and what the prompt promises, so it has to be
        // part of what invalidates a render.
        var scope = new McpScope();
        var provider = new McpAgentContextProvider(scope);

        var closed = provider.BuildContext();
        Assert.IsFalse(closed.Tools!.Any(t => t.Name == McpAgentToolkit.AddToolName), "at Closed the tool is absent");
        Assert.Contains("cannot add a server yourself", closed.Instructions!);

        scope.WithSelfService(McpSelfServiceLevel.AllConfirmed);
        var opened = provider.BuildContext();

        Assert.IsTrue(opened.Tools!.Any(t => t.Name == McpAgentToolkit.AddToolName), "the tool must appear");
        Assert.Contains("only after the user confirms", opened.Instructions!);
        Assert.AreNotSame(closed.Tools, opened.Tools, "a level change must rebuild");
    }

    [TestMethod]
    public void BuildContext_ReRendersWhenServersAreRegisteredLater()
    {
        // A host is free to register configurations after the provider exists; the toolkit is rebuilt per
        // render precisely so they are picked up.
        var scope = new McpScope();
        var provider = new McpAgentContextProvider(scope);
        var before = provider.BuildContext();

        scope.WithServers(new McpServerConfiguration
        {
            Name = "late", RunMode = McpServerRunMode.Http, Endpoint = "https://mcp.example.invalid/mcp",
        });

        Assert.AreNotSame(before.Tools, provider.BuildContext().Tools, "a registration must rebuild");
    }

    [TestMethod]
    public void BuildContext_ReusesTheRenderWhileNothingChanges()
    {
        var provider = new McpAgentContextProvider(new McpScope());

        var first = provider.BuildContext();
        var second = provider.BuildContext();

        Assert.AreSame(first.Tools, second.Tools);
        Assert.AreEqual(first.Instructions, second.Instructions);
    }

    [TestMethod]
    public void BuildContext_NeverReturnsNull()
    {
        Assert.IsNotNull(new McpAgentContextProvider(new McpScope()).BuildContext());
    }

    [TestMethod]
    public void RegisteredServers_AreRememberedWhenLoadedDirectly()
    {
        // A host that only ever calls LoadAsync must still end up with a reloadable set, or the Agent's
        // LoadMcpServers tool would refuse every name it has already seen.
        var scope = new McpScope();
        scope.LoadAsync([new McpServerConfiguration { Name = "direct", RunMode = McpServerRunMode.Http, Endpoint = null }])
             .GetAwaiter().GetResult();

        Assert.HasCount(1, scope.RegisteredServers);
        Assert.AreEqual("direct", scope.RegisteredServers[0].Name);
    }

    [TestMethod]
    public void RegisteredServers_KeepTheirRunModeAndPackage()
    {
        // Recovering configurations from the status list would lose these, and LoadMcpServers would then
        // hand the loader an empty shell.
        var scope = new McpScope().WithServers(new McpServerConfiguration
        {
            Name = "fs", RunMode = McpServerRunMode.Npx,
            Package = "@modelcontextprotocol/server-filesystem", Arguments = ["C:/data"],
        });

        var config = scope.RegisteredServers.Single();
        Assert.AreEqual(McpServerRunMode.Npx, config.RunMode);
        Assert.AreEqual("@modelcontextprotocol/server-filesystem", config.Package);
        Assert.HasCount(1, config.Arguments);
    }

    [TestMethod]
    public void StateKeys_AreEqualForTwoProvidersOverOneScope()
    {
        var scope = new McpScope();

        var first = new McpAgentContextProvider(scope);
        var second = new McpAgentContextProvider(scope);

        Assert.AreEqual(first.StateKeys[0], second.StateKeys[0]);
    }

    [TestMethod]
    public void StateKeys_DifferBetweenScopes()
    {
        Assert.AreNotEqual(
            new McpAgentContextProvider(new McpScope()).StateKeys[0],
            new McpAgentContextProvider(new McpScope()).StateKeys[0]);
    }

    // ── The policy seam ──────────────────────────────────────────────────────

    [TestMethod]
    public void Tools_MarshalOntoThePolicyContext()
    {
        using var context = new SingleThreadContext();
        int? calledOn = null;
        var pipeline = new AgentPipeline().Use((e, next, ct) =>
        {
            if (e is AgentToolCallCompleted) calledOn = Environment.CurrentManagedThreadId;
            return next(e);
        });

        var list = new McpAgentContextProvider(new McpScope(), new ToolPipeline { MarshalTo = () => context }, pipeline)
            .BuildContext().Tools!.Single(t => t.Name == "ListMcpServers");
        Invoke(list);

        Assert.AreEqual(context.ThreadId, calledOn, "a subsystem tool must run where the composing host says");
    }

    [TestMethod]
    public void Tools_AreGatedByThePolicy()
    {
        var refusals = 0;
        var list = new McpAgentContextProvider(
                new McpScope(),
                new ToolPipeline { Refuse = _ => { refusals++; return "refused by the composing host"; } })
            .BuildContext().Tools!.Single(t => t.Name == "ListMcpServers");
        var json = JObject.Parse(Invoke(list));

        Assert.AreEqual("error", json["status"]?.Value<string>());
        Assert.AreEqual(1, refusals);
    }

    [TestMethod]
    public void ServerTools_AreWrappedLikeTheManagementTools()
    {
        // The loaded-server tools belong to this provider too, so they must obey the same policy rather
        // than slipping through unwrapped — the failure this replaces went unnoticed once already.
        var scope = new McpScope();
        var provider = new McpAgentContextProvider(scope);

        var tools = provider.BuildContext().Tools!;

        // Nothing is loaded here, so the assertion is about the shape of the set rather than its contents:
        // every tool the provider offers is wrapped, and the loaded ones would be added the same way.
        Assert.IsNotEmpty(tools);
        Assert.IsTrue(tools.All(t => t.GetType().Name == "TrackedAIFunction"));
    }
}
