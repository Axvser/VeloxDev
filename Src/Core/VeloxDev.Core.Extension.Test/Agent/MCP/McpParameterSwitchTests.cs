using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.MCP;

namespace VeloxDev.Core.Extension.Test.Agent.MCP;

/// <summary>
/// Changing a server's launch parameters — a filesystem server's allowed directory, say — without the Agent
/// noticing anything but the result.
/// </summary>
/// <remarks>
/// <para>
/// The tools the Agent holds are proxies owned by the scope, so their identity outlives the connection. A
/// configuration change marks the connection stale; the next call connects with the new configuration and only
/// then releases the old client. Everything here runs on <see cref="McpScope.ConnectOverride"/>, which replaces
/// the connect step: the rebuild path is otherwise unreachable without a live MCP server, and a test that could
/// not reach it would assert against an empty tool set and pass whatever the rebuild did.
/// </para>
/// <para>
/// What is asserted about a connection is what it was made with — the fake connector stamps its arguments into
/// every result — so "the call went to the new connection" is a fact, not an inference.
/// </para>
/// </remarks>
[TestClass]
public class McpParameterSwitchTests
{
    private const string Server = "filesystem";
    private const string Package = "@modelcontextprotocol/server-filesystem";

    // ── Tests ───────────────────────────────────────────────────────────────

    /// <summary>A configuration change is carried out by the next call, not by the change itself.</summary>
    [TestMethod]
    public async Task AChangedConfiguration_RebuildsOnTheNextCall()
    {
        var connector = new FakeConnector();
        var scope = Scope(connector);

        await scope.AddAsync(Config("C:/first"));
        var tool = Tool(scope, "read_file");

        scope.WithServers(Config("C:/second"));

        Assert.AreEqual(0, connector.Clients[0].Disposals,
            "changing the configuration releases nothing — a server nobody calls must not be reconnected");

        var result = await tool.InvokeAsync(Args("a.txt"));

        Assert.AreEqual("C:/second:read_file:a.txt", result?.ToString(),
            "the call must be answered by the connection the new configuration built");
        Assert.AreEqual(1, connector.Clients[0].Disposals, "and the connection it replaced must be released once");
        Assert.AreEqual(2, connector.Clients.Count, "exactly one rebuild");
    }

    /// <summary>The proxy is the same object afterwards, and a change the model cannot see leaves the version alone.</summary>
    [TestMethod]
    public async Task ARebuiltServer_KeepsTheToolObjectsItAlreadyHandedOut()
    {
        var connector = new FakeConnector();
        var scope = Scope(connector);

        await scope.AddAsync(Config("C:/first"));
        var before = Tool(scope, "read_file");

        scope.WithServers(Config("C:/second"));
        var afterTheChange = scope.Version;

        await before.InvokeAsync(Args("a.txt"));

        Assert.AreSame(before, Tool(scope, "read_file"),
            "the same tool name must resolve to the same instance across a rebuild — that is what makes the "
            + "change invisible to the model and to anything that cached the tool");
        Assert.AreEqual(afterTheChange, scope.Version,
            "a rebuild that offers the same tools is not something the model can see, so the cached render stands");
    }

    /// <summary>A configuration that cannot be reached leaves the working connection serving.</summary>
    [TestMethod]
    public async Task AConfigurationThatCannotBeReached_LeavesTheWorkingConnectionInPlace()
    {
        var connector = new FakeConnector();
        var scope = Scope(connector);

        await scope.AddAsync(Config("C:/first"));
        var tool = Tool(scope, "read_file");

        connector.Refuse.Add("C:/broken");
        scope.WithServers(Config("C:/broken"));

        var result = await tool.InvokeAsync(Args("a.txt"));

        Assert.AreEqual("C:/first:read_file:a.txt", result?.ToString(),
            "a call after a failed reconfigure is served by the connection that still works");
        Assert.AreEqual(0, connector.Clients[0].Disposals, "the working connection must survive a bad configuration");

        var row = scope.Status.Snapshot.Single(s => s.Name == Server);
        Assert.AreEqual(McpServerStatus.Error, row.State, "and the failure is reported on the status row");
        StringAssert.Contains(row.Error ?? string.Empty, "C:/broken");
        Assert.AreEqual(1, row.ToolCount, "the row reports what is still on offer, not the zero of a torn-down server");

        await tool.InvokeAsync(Args("a.txt"));
        Assert.AreEqual(1, connector.AttemptsFor("C:/broken"),
            "one configuration is attempted once — a bad one must not turn every later call into another connect");

        connector.Refuse.Clear();
        scope.WithServers(Config("C:/third"));

        Assert.AreEqual("C:/third:read_file:a.txt", (await tool.InvokeAsync(Args("a.txt")))?.ToString(),
            "and a further change is a fresh instruction, tried again");
    }

    /// <summary>A server that stops offering a tool retires that proxy without breaking the ones that remain.</summary>
    [TestMethod]
    public async Task ARebuiltServerWithADifferentToolSet_RetiresWhatItNoLongerOffers()
    {
        var connector = new FakeConnector { Tools = _ => ["read_file", "write_file"] };
        var scope = Scope(connector);

        await scope.AddAsync(Config("C:/first"));
        var removed = Tool(scope, "write_file");
        var kept = Tool(scope, "read_file");

        connector.Tools = _ => ["read_file"];
        scope.WithServers(Config("C:/second"));
        var before = scope.Version;

        await kept.InvokeAsync(Args("a.txt"));

        Assert.IsTrue(scope.Version > before,
            "a tool appearing or disappearing IS something the model can see, so the cached render is invalidated");
        Assert.IsFalse(scope.LoadedTools.Any(t => t.Name == "write_file"),
            "a tool the server no longer offers must leave the offered set");
        Assert.AreSame(kept, Tool(scope, "read_file"), "the tool that survived keeps its identity");

        var result = await removed.InvokeAsync(Args("a.txt"));
        StringAssert.Contains(result?.ToString() ?? string.Empty, "no longer offers",
            "and the retired proxy says so plainly rather than failing obscurely or reaching the wrong server");
    }

    /// <summary>Calls that arrive while a rebuild is owed queue behind it instead of each starting one.</summary>
    [TestMethod]
    public async Task CallsThatRaceARebuild_ProduceOneConnection()
    {
        var connector = new FakeConnector();
        var scope = Scope(connector);

        await scope.AddAsync(Config("C:/first"));
        var tool = Tool(scope, "read_file");

        connector.Held = "C:/second";
        scope.WithServers(Config("C:/second"));

        var calls = Enumerable.Range(0, 4)
            .Select(_ => tool.InvokeAsync(Args("a.txt")).AsTask())
            .ToArray();

        connector.WaitUntilConnecting("C:/second");
        connector.Release("C:/second");

        foreach (var call in calls)
            Assert.AreEqual("C:/second:read_file:a.txt", (await call)?.ToString());

        Assert.AreEqual(2, connector.Clients.Count, "four calls that race one rebuild connect once between them");
        Assert.AreEqual(1, connector.Clients[0].Disposals, "and release the replaced connection once");
    }

    /// <summary>The lock is per server: one server's rebuild does not hold up another's calls.</summary>
    [TestMethod]
    public async Task OneServersRebuild_DoesNotHoldUpAnotherServer()
    {
        var connector = new FakeConnector { Tools = c => [c.Name == "b" ? "b_ping" : "a_ping"] };
        var scope = Scope(connector);

        await scope.AddAsync(Named("a", "C:/a"));
        await scope.AddAsync(Named("b", "C:/b"));

        // Point server a at a configuration whose connect blocks, then start the call that must trigger it.
        connector.Held = "C:/a/held";
        scope.WithServers(Named("a", "C:/a/held"));
        var blocked = Tool(scope, "a_ping").InvokeAsync(Args("x")).AsTask();

        connector.WaitUntilConnecting("C:/a/held");

        var other = Tool(scope, "b_ping").InvokeAsync(Args("x")).AsTask();
        Assert.IsTrue(other.Wait(TimeSpan.FromSeconds(5)),
            "the other server's call must not queue behind a rebuild it has nothing to do with");
        Assert.AreEqual("C:/b:b_ping:x", (await other)?.ToString());

        connector.Release("C:/a/held");
        Assert.AreEqual("C:/a/held:a_ping:x", (await blocked)?.ToString());
    }

    /// <summary>Unloading really does take the proxies away, and a later load hands out new ones.</summary>
    [TestMethod]
    public async Task UnloadingAThatServer_RetiresItsProxies()
    {
        var connector = new FakeConnector();
        var scope = Scope(connector);

        await scope.AddAsync(Config("C:/first"));
        var first = Tool(scope, "read_file");

        Assert.IsTrue(await scope.UnloadServerAsync(Server), "the server was loaded");

        Assert.IsEmpty(scope.LoadedTools, "its tools are gone from what the Agent is offered");
        Assert.AreEqual(1, connector.Clients[0].Disposals, "and its connection is released");

        await scope.AddAsync(Config("C:/first"));
        var second = Tool(scope, "read_file");

        Assert.AreNotSame(first, second,
            "a fresh load is a fresh server: the old proxy stays retired, which is what unload means");
    }

    /// <summary>
    /// A sub-agent's granted view shares the parent's proxies, so a call through it rebuilds the one connection
    /// rather than a second one nobody owns.
    /// </summary>
    [TestMethod]
    public async Task AGrantedView_SharesTheParentsProxies()
    {
        var connector = new FakeConnector();
        var scope = Scope(connector);

        await scope.AddAsync(Config("C:/first"));

        var view = McpScope.CreateGrantedView(scope, [Server]);
        var childTool = view.LoadedTools.OfType<AIFunction>().Single(t => t.Name == "read_file");

        Assert.AreSame(Tool(scope, "read_file"), childTool, "the view offers the parent's own proxy instance");

        scope.WithServers(Config("C:/second"));

        Assert.AreEqual("C:/second:read_file:a.txt", (await childTool.InvokeAsync(Args("a.txt")))?.ToString(),
            "a call through the view reaches the connection the parent just built");
        Assert.AreEqual(1, connector.Clients[0].Disposals);
        Assert.AreEqual(2, connector.Clients.Count,
            "and there is still exactly one connection for that server — the view does not own one");
    }

    /// <summary>
    /// Reconfiguring through the Agent's own tool releases the connection it replaces.
    /// </summary>
    /// <remarks>
    /// The regression this exists for: a same-named add used to overwrite the map entry and leave the previous
    /// client — and, for stdio modes, its child process — running with nobody holding a reference to release it.
    /// </remarks>
    [TestMethod]
    public async Task ReconfiguringThroughTheAgentTool_ReleasesTheConnectionItReplaces()
    {
        var connector = new FakeConnector();
        var scope = Scope(connector)
            .WithSelfService(McpSelfServiceLevel.AllConfirmed)
            .WithConfirmationHandler((_, _) => Task.FromResult(true));

        await scope.AddAsync(Config("C:/first"));

        var toolkit = new McpAgentToolkit(scope, []);
        var add = toolkit.CreateTools().Single(t => t.Name == "AddMcpServer");

        var result = Invoke(add,
            ("name", Server),
            ("runMode", "Npx"),
            ("package", Package),
            ("argumentsJson", "[\"C:/second\"]"));

        Assert.AreEqual("ok", JObject.Parse(result)["status"]?.Value<string>(), result);
        Assert.AreEqual(1, connector.Clients[0].Disposals,
            "the replaced connection must be released — this is the step that used to be skipped");
        Assert.AreEqual(2, connector.Clients.Count);
        Assert.AreEqual("C:/second:read_file:a.txt", (await Tool(scope, "read_file").InvokeAsync(Args("a.txt")))?.ToString());
    }

    // ── Reading and changing the arguments through the Agent's tools ─────────

    /// <summary>The model can see what a server was launched with, not just whether it is alive.</summary>
    [TestMethod]
    public async Task ListMcpServers_ReportsWhatTheServerWasLaunchedWith()
    {
        var connector = new FakeConnector();
        var scope = Scope(connector);

        var config = Config("C:/data");
        config.Description = "Filesystem access for the workspace.";
        await scope.AddAsync(config);

        var listed = JObject.Parse(List(scope));
        var server = ((JArray)listed["servers"]!).Single();

        Assert.AreEqual(Package, server["package"]?.Value<string>());
        Assert.AreEqual("C:/data", ((JArray)server["arguments"]!).Single().Value<string>());
        Assert.AreEqual("Filesystem access for the workspace.", server["description"]?.Value<string>());
        Assert.IsNull(server["endpoint"]?.Value<string>(),
            "a locally launched server has no endpoint, and the shape stays the same rather than dropping fields");
    }

    /// <summary>
    /// The host's connection options never reach the model — they carry credentials.
    /// </summary>
    /// <remarks>
    /// The option bag holds authorization headers, an OAuth client secret and environment variables. It is
    /// part of the configuration the toolkit can read, which is exactly why this is asserted on the raw JSON
    /// rather than field by field: the guard has to hold for fields nobody has thought of yet.
    /// </remarks>
    [TestMethod]
    public async Task ListMcpServers_NeverCarriesTheHostsOptionBag()
    {
        var connector = new FakeConnector();
        var scope = Scope(connector);

        var config = Config("C:/data");
        config.Options = new Dictionary<string, object?>
        {
            ["headers"] = new Dictionary<string, object?> { ["Authorization"] = "Bearer super-secret-token" },
            ["oauth"] = new Dictionary<string, object?> { ["clientSecret"] = "super-secret-client" },
            ["env"] = new Dictionary<string, object?> { ["API_KEY"] = "super-secret-env" },
        };
        await scope.AddAsync(config);

        var json = List(scope);

        foreach (var secret in new[] { "super-secret-token", "super-secret-client", "super-secret-env", "Authorization", "oauth" })
            Assert.IsFalse(json.Contains(secret, StringComparison.Ordinal), $"the option bag leaked '{secret}': {json}");
    }

    /// <summary>Changing the arguments keeps everything else about the server, and applies now.</summary>
    [TestMethod]
    public async Task SetMcpServerArguments_AppliesItAndKeepsEverythingElse()
    {
        var connector = new FakeConnector();
        var scope = Gated(connector);

        var config = Config("C:/first");
        var options = new Dictionary<string, object?> { ["env"] = new Dictionary<string, object?> { ["ROOT"] = "C:/first" } };
        config.Options = options;
        await scope.AddAsync(config);

        var result = JObject.Parse(SetArguments(scope, Server, """["C:/second"]"""));

        Assert.AreEqual("ok", result["status"]?.Value<string>(), result.ToString());
        Assert.IsTrue(result["applied"]!.Value<bool>(), "a connected server is reconfigured there and then");
        Assert.AreEqual("C:/second", ((JArray)result["arguments"]!).Single().Value<string>());

        var attempt = connector.LastAttempt!;
        Assert.AreEqual(Package, attempt.Package, "the package is carried over, not restated by the model");
        Assert.AreEqual(McpServerRunMode.Npx, attempt.RunMode);
        Assert.AreSame(options, attempt.Options,
            "and the host's connection options travel with it — a reconfigure must not drop credentials");

        Assert.AreEqual("C:/second:read_file:a.txt", (await Tool(scope, "read_file").InvokeAsync(Args("a.txt")))?.ToString());
    }

    /// <summary>Arguments that cannot be reached cost nothing: the working connection stays.</summary>
    [TestMethod]
    public async Task SetMcpServerArguments_KeepsTheOldConnectionWhenTheNewOnesCannotBeReached()
    {
        var connector = new FakeConnector();
        var scope = Gated(connector);

        await scope.AddAsync(Config("C:/first"));
        connector.Refuse.Add("C:/broken");

        var result = JObject.Parse(SetArguments(scope, Server, """["C:/broken"]"""));

        Assert.AreEqual("error", result["status"]?.Value<string>(), result.ToString());
        Assert.IsFalse(result["applied"]!.Value<bool>());
        StringAssert.Contains(result["error"]?.Value<string>() ?? string.Empty, "C:/broken");

        Assert.AreEqual(0, connector.Clients[0].Disposals, "the connection that works is not torn down");
        Assert.AreEqual("C:/first:read_file:a.txt", (await Tool(scope, "read_file").InvokeAsync(Args("a.txt")))?.ToString(),
            "and calls keep being served by it");
    }

    /// <summary>A remote server is launched by the host; it takes no launch arguments.</summary>
    [TestMethod]
    public async Task SetMcpServerArguments_RefusesARemoteServer()
    {
        var connector = new FakeConnector();
        var scope = Gated(connector);

        await scope.AddAsync(new McpServerConfiguration
        {
            Name = "remote",
            RunMode = McpServerRunMode.Http,
            Endpoint = null,
        });

        var result = JObject.Parse(SetArguments(scope, "remote", """["C:/data"]"""));

        Assert.AreEqual("error", result["status"]?.Value<string>(), result.ToString());
        StringAssert.Contains(result["message"]?.Value<string>() ?? string.Empty, "Http");
    }

    /// <summary>An unknown name is answered with the ones that do exist.</summary>
    [TestMethod]
    public async Task SetMcpServerArguments_OnAnUnknownName_ListsTheKnownOnes()
    {
        var connector = new FakeConnector();
        var scope = Gated(connector);
        await scope.AddAsync(Config("C:/first"));

        var result = JObject.Parse(SetArguments(scope, "no-such-server", """["C:/data"]"""));

        Assert.AreEqual("error", result["status"]?.Value<string>(), result.ToString());
        StringAssert.Contains(result["message"]?.Value<string>() ?? string.Empty, Server);
    }

    /// <summary>
    /// A server that is registered but not connected has nothing to restart, so the change is banked.
    /// </summary>
    [TestMethod]
    public async Task SetMcpServerArguments_OnARegisteredButUnloadedServer_OnlyRegistersIt()
    {
        var connector = new FakeConnector();
        var scope = Gated(connector);
        scope.WithServers(Config("C:/first"));

        var result = JObject.Parse(SetArguments(scope, Server, """["C:/second"]"""));

        Assert.AreEqual("ok", result["status"]?.Value<string>(), result.ToString());
        Assert.IsFalse(result["applied"]!.Value<bool>(), "nothing is running to apply it to");
        StringAssert.Contains(result["message"]?.Value<string>() ?? string.Empty, "next loaded");
        Assert.IsEmpty(connector.Clients, "and nothing was launched to find that out");
    }

    /// <summary>The tool appears only on the rung where it can actually do something.</summary>
    /// <remarks>
    /// One rung above adding, and not the same rung: a remote server takes no launch arguments, and a local one is
    /// not reconfigurable below <see cref="McpSelfServiceLevel.AllConfirmed"/>. Registering it at
    /// <see cref="McpSelfServiceLevel.RemoteConfirmed"/> would put a tool in front of the model whose every call
    /// is refused — the thing this toolkit is otherwise careful never to ship.
    /// </remarks>
    [TestMethod]
    public void SetMcpServerArguments_AppearsOnlyWhereItCanSucceed()
    {
        var scope = new McpScope();

        Assert.IsFalse(HasSetArgumentsTool(scope), "at Closed no write tool exists at all");

        scope.WithSelfService(McpSelfServiceLevel.RemoteConfirmed);
        Assert.IsTrue(HasAddServerTool(scope), "the adding tool opens a rung earlier, for remote servers");
        Assert.IsFalse(HasSetArgumentsTool(scope),
            "on this rung every call would be refused — Http servers take no arguments and local ones are not reconfigurable yet");

        scope.WithSelfService(McpSelfServiceLevel.AllConfirmed);
        Assert.IsTrue(HasSetArgumentsTool(scope));
    }

    private static bool HasSetArgumentsTool(McpScope scope)
        => new McpAgentToolkit(scope, []).CreateTools().Any(t => t.Name == McpAgentToolkit.SetArgumentsName);

    private static bool HasAddServerTool(McpScope scope)
        => new McpAgentToolkit(scope, []).CreateTools().Any(t => t.Name == McpAgentToolkit.AddToolName);

    // ── Fixtures ────────────────────────────────────────────────────────────

    /// <summary>A scope whose gate is open and whose user says yes — enough to reach the changing tools.</summary>
    private static McpScope Gated(FakeConnector connector)
        => Scope(connector)
            .WithSelfService(McpSelfServiceLevel.AllConfirmed)
            .WithConfirmationHandler((_, _) => Task.FromResult(true));

    private static string List(McpScope scope)
        => Invoke(new McpAgentToolkit(scope, []).CreateTools().Single(t => t.Name == McpAgentToolkit.ListName));

    private static string SetArguments(McpScope scope, string name, string argumentsJson)
        => Invoke(
            new McpAgentToolkit(scope, []).CreateTools().Single(t => t.Name == McpAgentToolkit.SetArgumentsName),
            ("name", name),
            ("argumentsJson", argumentsJson));

    private static McpScope Scope(FakeConnector connector)
    {
        var scope = new McpScope();
        scope.ConnectOverride = connector.ConnectAsync;
        return scope;
    }

    private static McpServerConfiguration Config(params string[] arguments) => Named(Server, arguments);

    private static McpServerConfiguration Named(string name, params string[] arguments)
        => new()
        {
            Name = name,
            RunMode = McpServerRunMode.Npx,
            Package = Package,
            Arguments = arguments,
        };

    private static AIFunctionArguments Args(string path) => new() { ["path"] = path };

    /// <summary>The tool of that name as the Agent would find it — the proxy, not whatever is behind it.</summary>
    private static AIFunction Tool(McpScope scope, string name)
        => scope.LoadedTools.OfType<AIFunction>().Single(t => t.Name == name);

    private static string Invoke(AITool tool, params (string Name, object? Value)[] args)
    {
        var callArgs = new AIFunctionArguments();
        foreach (var (name, value) in args) callArgs[name] = value;
        var result = ((AIFunction)tool).InvokeAsync(callArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    /// <summary>A client that records whether and how often it was released.</summary>
    private sealed class RecordingClient : IAsyncDisposable
    {
        private int _disposals;

        internal int Disposals => Volatile.Read(ref _disposals);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposals);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Answers a connect with tools it invents, stamped with the arguments it was asked for — so a result says
    /// which configuration produced it.
    /// </summary>
    private sealed class FakeConnector
    {
        private readonly object _sync = new();
        private readonly List<RecordingClient> _clients = [];
        private readonly List<McpServerConfiguration> _attempts = [];
        private readonly Dictionary<string, ManualResetEventSlim> _open = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ManualResetEventSlim> _entered = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The tool names a configuration answers with.</summary>
        internal Func<McpServerConfiguration, IReadOnlyList<string>> Tools { get; set; } = _ => ["read_file"];

        /// <summary>Configurations to refuse, named by their first argument.</summary>
        internal HashSet<string> Refuse { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A configuration whose connect is held open until <see cref="Release"/>, by first argument.</summary>
        internal string? Held { get; set; }

        internal IReadOnlyList<RecordingClient> Clients { get { lock (_sync) return [.. _clients]; } }

        /// <summary>The last configuration a connect was asked for, whether or not it succeeded.</summary>
        internal McpServerConfiguration? LastAttempt
        {
            get { lock (_sync) return _attempts.Count > 0 ? _attempts[^1] : null; }
        }

        internal int AttemptsFor(string argument)
        {
            lock (_sync) return _attempts.Count(c => c.Arguments.Length > 0 && c.Arguments[0] == argument);
        }

        /// <summary>Blocks until a connect for that argument is inside the connector.</summary>
        internal void WaitUntilConnecting(string argument)
        {
            ManualResetEventSlim entered;
            lock (_sync)
            {
                if (!_entered.TryGetValue(argument, out entered!)) _entered[argument] = entered = new(false);
            }

            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)),
                $"no connect for '{argument}' was started within the budget");
        }

        internal void Release(string argument)
        {
            ManualResetEventSlim open;
            lock (_sync)
            {
                if (!_open.TryGetValue(argument, out open!)) _open[argument] = open = new(false);
            }

            open.Set();
        }

        internal async Task<McpConnection> ConnectAsync(
            McpServerConfiguration config, string mcpRoot, CancellationToken ct)
        {
            var argument = config.Arguments.Length > 0 ? config.Arguments[0] : string.Empty;
            lock (_sync) _attempts.Add(config);

            if (Held == argument)
            {
                ManualResetEventSlim entered, open;
                lock (_sync)
                {
                    if (!_entered.TryGetValue(argument, out entered!)) _entered[argument] = entered = new(false);
                    if (!_open.TryGetValue(argument, out open!)) _open[argument] = open = new(false);
                }

                entered.Set();
                await Task.Run(() => open.Wait(TimeSpan.FromSeconds(5)), ct);
            }

            if (Refuse.Contains(argument))
                throw new InvalidOperationException($"MCP server '{config.Name}' cannot be reached at '{argument}'.");

            var client = new RecordingClient();
            lock (_sync) _clients.Add(client);

            var tools = Tools(config)
                .Select(name => (AIFunction)AIFunctionFactory.Create(
                    (string? path) => $"{argument}:{name}:{path}", name))
                .ToArray();

            return new McpConnection(client, tools);
        }
    }
}
