using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace VeloxDev.Cli;

/// <summary>
/// The stdio MCP host. Logging is routed to standard error because standard output carries the protocol.
/// </summary>
internal static class McpHost
{
    public static async Task RunAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        await using var host = WorkflowSessionHost.Create(options);
        await using var jobs = new WorkerJobRegistry();
        var session = host.Current;

        var builder = Host.CreateApplicationBuilder();

        // Hosting's default console logger writes to stdout, which would corrupt the JSON-RPC stream. Every
        // log line, including the SDK's own, goes to stderr instead.
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddConsole(console =>
            console.LogToStandardErrorThreshold = LogLevel.Trace);

        var tools = session.CreateMcpTools(host);
        var version = typeof(McpHost).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        var mcp = builder.Services
            .AddMcpServer(server =>
            {
                server.ServerInfo = new Implementation { Name = "VeloxDev.Cli", Version = version };
                server.ServerInstructions = session.BuildInstructions();
            })
            .WithStdioServerTransport()
            .WithTools(tools)
            .WithTools(new ArchiveTools(host))
            .WithResources(new SessionResources(host))
            .WithPrompts(new SessionPrompts(host));

        if (session.ExecutionEnabled)
            mcp.WithTools(new RedirectTools(host));
        if (session.ExecutionEnabled && !session.InProcessExecution)
            mcp.WithTools(new ExecutionTools(host, jobs));

        await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
