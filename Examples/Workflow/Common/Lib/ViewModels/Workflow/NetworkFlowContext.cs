using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

public sealed class NetworkFlowContext
{
    private readonly object _syncRoot = new();
    private readonly HashSet<int> _scheduledNodes = [];
    private int _executionSequence;

    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<NetworkFlowRecord> History { get; } = [];
    public List<string> ExecutionTrail { get; } = [];

    public static NetworkFlowContext Create(object? seed = default)
    {
        var context = new NetworkFlowContext();
        if (seed is not null)
        {
            context.Variables["seed"] = seed.ToString() ?? string.Empty;
        }

        return context;
    }

    public static NetworkFlowContext From(object? parameter)
        => parameter as NetworkFlowContext ?? Create(parameter);

    public void Set(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        Variables[key] = value ?? string.Empty;
    }

    public void SetNodeResult(string captureKey, string method, string url, int statusCode, string responseBody, string responseSummary)
    {
        Set("last.method", method);
        Set("last.url", url);
        Set("last.status", statusCode.ToString());
        Set("last.body", responseBody);
        Set("last.summary", responseSummary);

        if (!string.IsNullOrWhiteSpace(captureKey))
        {
            Set($"{captureKey}.method", method);
            Set($"{captureKey}.url", url);
            Set($"{captureKey}.status", statusCode.ToString());
            Set($"{captureKey}.body", responseBody);
            Set($"{captureKey}.summary", responseSummary);
        }

        History.Add(new NetworkFlowRecord(captureKey, method, url, statusCode, responseSummary));
    }

    /// <summary>
    /// Returns the next execution sequence number. The demo now delegates the whole tree's numbering to
    /// TreeViewModel.NextExecutionSequence() (globally monotonic; standalone starts no longer reset it);
    /// this method only serves as a fallback when there is no tree.
    /// </summary>
    public int NextOrder() => Interlocked.Increment(ref _executionSequence);

    /// <summary>
    /// Records one execution.
    /// When <paramref name="globalOrder"/> is non-null it is used as the sequence number (the demo passes the
    /// tree's global sequence); otherwise this flow's private counter is used as a fallback.
    /// </summary>
    public string RecordExecution(string nodeTitle, out int order, int? globalOrder = null)
    {
        order = globalOrder ?? Interlocked.Increment(ref _executionSequence);
        var entry = $"{order:00}. {nodeTitle}";

        lock (_syncRoot)
        {
            ExecutionTrail.Add(entry);
        }

        return entry;
    }

    public bool TryScheduleNode(IWorkflowNodeViewModel node)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        lock (_syncRoot)
        {
            return _scheduledNodes.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node));
        }
    }
}

public sealed class NetworkFlowRecord
{
    public NetworkFlowRecord(string captureKey, string method, string url, int statusCode, string summary)
    {
        CaptureKey = captureKey;
        Method = method;
        Url = url;
        StatusCode = statusCode;
        Summary = summary;
    }

    public string CaptureKey { get; }
    public string Method { get; }
    public string Url { get; }
    public int StatusCode { get; }
    public string Summary { get; }
}
