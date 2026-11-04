using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace VeloxDev.Cli;

/// <summary>
/// The execution surface used by default: each call runs in a child process, so the server survives a node that
/// hangs or takes the runtime down. The tool names and parameters match the library's in-process tools, so an MCP
/// client sees the same vocabulary either way.
/// </summary>
[McpServerToolType]
internal sealed class ExecutionTools(WorkflowSessionHost host, WorkerJobRegistry jobs)
{
    [McpServerTool(Name = "RunCompiledWorkflow", Destructive = true, OpenWorld = true)]
    [Description("Runs the compiled workflow (chain-level execution) from a start node in an isolated worker process. Compiles the reachable sub-graph, drives it with the runtime engine, and returns the session outcome: runStatus, outcome, attempts, data, failures, the log and logFile. A node that hangs, exits or crashes the runtime is reported here instead of taking the server down. A graph edit made earlier in this session is included without saving. Requires --allow-execution.")]
    public async Task<string> RunCompiledWorkflow(
        [Description("Node index of the compile entry point (usually a controller).")] int startNodeIndex,
        [Description("Optional seed payload injected into the runtime session (becomes the session's Data).")] string? seed = null,
        [Description("Optional timeout in milliseconds. Omit to wait for the run to end.")] int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        var slots = host.RunSlots;
        if (slots is not null) await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Held for the whole worker call: a reload retires this session instead of disposing it mid-run.
            using var lease = host.Acquire();
            var result = await new WorkerRunner(lease.Session)
                .RunCompiledAsync(startNodeIndex, seed, timeoutMs, cancellationToken)
                .ConfigureAwait(false);
            return lease.Session.AttachRunCsv("RunCompiledWorkflow", result);
        }
        finally
        {
            slots?.Release();
        }
    }

    [McpServerTool(Name = "GetNodeResult", Destructive = true, OpenWorld = true)]
    [Description("Computes a node's RESULT in isolation (terminal role) in an isolated worker process: compiles the node's ancestor cone, drives it from the cone's own entry frontier, and returns targetReached/data/failures/log. Routers inside the cone keep real branch selection. A node that hangs, exits or crashes the runtime is reported here instead of taking the server down. Requires --allow-execution.")]
    public async Task<string> GetNodeResult(
        [Description("Node index whose result to compute (its output becomes the run's final data).")] int nodeIndex,
        [Description("Optional seed payload injected into the runtime session (becomes the session's Data).")] string? seed = null,
        [Description("Optional timeout in milliseconds. Omit to wait for the run to end.")] int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        var slots = host.RunSlots;
        if (slots is not null) await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var lease = host.Acquire();
            var result = await new WorkerRunner(lease.Session)
                .GetNodeResultAsync(nodeIndex, seed, timeoutMs, cancellationToken)
                .ConfigureAwait(false);
            return lease.Session.AttachRunCsv("GetNodeResult", result);
        }
        finally
        {
            slots?.Release();
        }
    }

    [McpServerTool(Name = "StartWorkflow", Destructive = true, OpenWorld = true)]
    [Description("Starts an isolated run of the compiled workflow from a start node and returns a job id at once. Poll GetWorkflowJob for state and the run result; CancelWorkflowJob stops it. The worker is a child process, so the server survives a crash or hang. Use RunCompiledWorkflow when you want to block for the result.")]
    public string StartWorkflow(
        [Description("Node index of the compile entry point (usually a controller).")] int startNodeIndex,
        [Description("Optional seed payload injected into the runtime session.")] string? seed = null,
        [Description("Optional timeout in milliseconds.")] int? timeoutMs = null)
    {
        var id = jobs.Start(host, nodeResult: false, startNodeIndex, seed, timeoutMs);
        return JsonSerializer.Serialize(new
        {
            status = "ok",
            id,
            message = "Run started. Poll GetWorkflowJob with this id; CancelWorkflowJob stops it.",
        });
    }

    [McpServerTool(Name = "GetWorkflowJob", ReadOnly = true)]
    [Description("Reports one background run: state (Queued / Running / Completed / Cancelled / Failed), the run result once it finished, and the worker error if it failed. A completed job's result has the same shape as RunCompiledWorkflow.")]
    public string GetWorkflowJob(
        [Description("The id returned by StartWorkflow.")] string id)
    {
        var job = jobs.Find(id);
        return job is null
            ? JsonSerializer.Serialize(new { status = "error", message = $"No job '{id}'." })
            : WorkerJobRegistry.Describe(job);
    }

    [McpServerTool(Name = "CancelWorkflowJob", Destructive = true)]
    [Description("Stops a background run by killing its worker process. The job ends as Cancelled; the server is unaffected.")]
    public string CancelWorkflowJob(
        [Description("The id returned by StartWorkflow.")] string id)
    {
        return jobs.Cancel(id)
            ? JsonSerializer.Serialize(new { status = "ok", id, message = "Cancellation requested." })
            : JsonSerializer.Serialize(new { status = "error", message = $"No job '{id}'." });
    }

    [McpServerTool(Name = "ListWorkflowJobs", ReadOnly = true)]
    [Description("Lists background runs this server still holds: id, state, node index and start time. Finished jobs stay until ClearFinishedJobs.")]
    public string ListWorkflowJobs()
    {
        var list = jobs.List().Select(job => new
        {
            id = job.Id,
            state = job.State.ToString(),
            nodeResult = job.NodeResult,
            index = job.Index,
            startedAt = job.StartedAt,
        });

        return JsonSerializer.Serialize(new { status = "ok", jobs = list });
    }

    [McpServerTool(Name = "ClearFinishedJobs", Destructive = true)]
    [Description("Drops completed, cancelled and failed jobs from the registry. Running jobs are left alone.")]
    public string ClearFinishedJobs()
    {
        var removed = jobs.ClearFinished();
        return JsonSerializer.Serialize(new { status = "ok", cleared = removed });
    }
}
