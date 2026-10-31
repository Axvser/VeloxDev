using System;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.MCP;

/// <summary>
/// The disposing half of the scope contract. A host that closes its session must not leave stdio server
/// processes behind, and the propagation is the part no host can see for itself.
/// </summary>
[TestClass]
public class McpDisposeTests
{
    private sealed class TrackedClient : IAsyncDisposable
    {
        private int _disposed;
        public int Disposed => Volatile.Read(ref _disposed);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposed);
            return default;
        }
    }

    private static McpServerConfiguration Config() => new()
    {
        Name = "probe",
        RunMode = McpServerRunMode.Http,
        Endpoint = "https://example.invalid/mcp",
    };

    [TestMethod]
    public async Task DisposingTheWorkflowScope_ReleasesTheMcpConnection()
    {
        var client = new TrackedClient();
        var mcp = new McpScope
        {
            ConnectOverride = (_, _, _) => Task.FromResult(new McpConnection(client, [])),
        };
        await mcp.LoadAsync([Config()]);

        Assert.AreEqual(0, client.Disposed, "precondition: the connection is live");

        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);
        await scope.DisposeAsync();

        Assert.AreEqual(1, client.Disposed, "the scope owns the connection and must tear it down");
    }

    [TestMethod]
    public async Task DisposingTwice_IsSafe()
    {
        var client = new TrackedClient();
        var mcp = new McpScope
        {
            ConnectOverride = (_, _, _) => Task.FromResult(new McpConnection(client, [])),
        };
        await mcp.LoadAsync([Config()]);
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);

        await scope.DisposeAsync();
        await scope.DisposeAsync();

        Assert.AreEqual(1, client.Disposed, "the second dispose finds no host and releases nothing twice");
    }
}
