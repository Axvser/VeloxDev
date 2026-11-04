using System.Text.Json;
using System.Text.Json.Nodes;

namespace VeloxDev.Cli;

internal enum WorkerJobState
{
    Queued,
    Running,
    Completed,
    Cancelled,
    Failed,
}

/// <summary>One isolated run started in the background.</summary>
internal sealed class WorkerJob(string id, bool nodeResult, int index, SessionLease lease)
{
    private readonly object _gate = new();
    private WorkerJobState _state = WorkerJobState.Queued;
    private string? _result;
    private string? _error;

    public string Id { get; } = id;

    public bool NodeResult { get; } = nodeResult;

    public int Index { get; } = index;

    /// <summary>The session this job captured when it was started; held until the worker exits.</summary>
    public SessionLease Lease { get; } = lease;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public CancellationTokenSource Cancellation { get; } = new();

    public Task? Task { get; set; }

    public WorkerJobState State
    {
        get { lock (_gate) return _state; }
    }

    public string? Result
    {
        get { lock (_gate) return _result; }
    }

    public string? Error
    {
        get { lock (_gate) return _error; }
    }

    public void MarkRunning() => SetState(WorkerJobState.Running);

    public void Complete(string result, bool cancelled)
    {
        lock (_gate)
        {
            _result = result;
            _state = cancelled ? WorkerJobState.Cancelled : WorkerJobState.Completed;
        }
    }

    public void Fail(string error)
    {
        lock (_gate)
        {
            _error = error;
            _state = WorkerJobState.Failed;
        }
    }

    public void MarkCancelled() => SetState(WorkerJobState.Cancelled);

    private void SetState(WorkerJobState state)
    {
        lock (_gate) _state = state;
    }
}

/// <summary>
/// Background isolated runs. A job holds a worker slot and a session lease for its whole life, so reloads retire
/// the session instead of pulling it out from under a running worker.
/// </summary>
internal sealed class WorkerJobRegistry : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WorkerJob> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public string Start(WorkflowSessionHost host, bool nodeResult, int index, string? seed, int? timeoutMs)
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var lease = host.Acquire();
        var job = new WorkerJob(id, nodeResult, index, lease);

        lock (_gate)
        {
            if (_disposed)
            {
                lease.Dispose();
                throw new ObjectDisposedException(nameof(WorkerJobRegistry));
            }

            _jobs[id] = job;
        }

        job.Task = RunAsync(host, job, seed, timeoutMs);
        return id;
    }

    public WorkerJob? Find(string id)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(id, out var job) ? job : null;
        }
    }

    public IReadOnlyList<WorkerJob> List()
    {
        lock (_gate)
        {
            return [.. _jobs.Values.OrderBy(j => j.StartedAt)];
        }
    }

    public bool Cancel(string id)
    {
        var job = Find(id);
        if (job is null) return false;

        try { job.Cancellation.Cancel(); }
        catch (ObjectDisposedException) { return false; }

        // The worker may still be queued for a slot; its own cancellation path marks it.
        return true;
    }

    public int ClearFinished()
    {
        List<WorkerJob> removed = [];
        lock (_gate)
        {
            foreach (var job in _jobs.Values.ToList())
            {
                if (job.State is WorkerJobState.Completed or WorkerJobState.Cancelled or WorkerJobState.Failed)
                {
                    _jobs.Remove(job.Id);
                    removed.Add(job);
                }
            }
        }

        foreach (var job in removed) job.Cancellation.Dispose();
        return removed.Count;
    }

    private static async Task RunAsync(
        WorkflowSessionHost host, WorkerJob job, string? seed, int? timeoutMs)
    {
        var slots = host.RunSlots;

        try
        {
            if (slots is not null) await slots.WaitAsync(job.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            job.MarkCancelled();
            job.Lease.Dispose();
            return;
        }

        try
        {
            job.MarkRunning();

            // The lease was taken when the job started, so a reload retires that session rather than swapping a
            // different archive in behind a queued job. It is released when the worker exits.
            var runner = new WorkerRunner(job.Lease.Session);

            var result = job.NodeResult
                ? await runner.GetNodeResultAsync(job.Index, seed, timeoutMs, job.Cancellation.Token).ConfigureAwait(false)
                : await runner.RunCompiledAsync(job.Index, seed, timeoutMs, job.Cancellation.Token).ConfigureAwait(false);

            result = job.Lease.Session.AttachRunCsv(job.NodeResult ? "GetNodeResult" : "RunCompiledWorkflow", result);
            job.Complete(result, job.Cancellation.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            job.MarkCancelled();
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
        }
        finally
        {
            slots?.Release();
            job.Lease.Dispose();
        }
    }

    /// <summary>The job as JSON. A parsed run result is embedded as an object; anything else stays a string.</summary>
    public static string Describe(WorkerJob job)
    {
        object? result = job.Result;
        if (result is string text)
        {
            try
            {
                if (JsonNode.Parse(text) is JsonNode node) result = node;
            }
            catch (JsonException)
            {
                // Keep the raw text.
            }
        }

        return JsonSerializer.Serialize(new
        {
            status = "ok",
            id = job.Id,
            state = job.State.ToString(),
            nodeResult = job.NodeResult,
            index = job.Index,
            startedAt = job.StartedAt,
            error = job.Error,
            result,
        });
    }

    public async ValueTask DisposeAsync()
    {
        List<WorkerJob> jobs;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            jobs = [.. _jobs.Values];
        }

        foreach (var job in jobs)
        {
            try { job.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { /* already finished and cleared */ }
        }

        foreach (var job in jobs)
        {
            if (job.Task is { } task)
            {
                try { await task.ConfigureAwait(false); }
                catch { /* the job records its own failure */ }
            }

            job.Cancellation.Dispose();
        }
    }
}
