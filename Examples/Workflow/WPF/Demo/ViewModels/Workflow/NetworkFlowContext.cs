using System;
using System.Collections.Generic;
using System.Threading;

namespace Demo.ViewModels;

public sealed class NetworkFlowContext
{
    private readonly object _syncRoot = new();
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

    public string RecordExecution(string nodeTitle, out int order)
    {
        order = Interlocked.Increment(ref _executionSequence);
        var entry = $"{order:00}. {nodeTitle}";

        lock (_syncRoot)
        {
            ExecutionTrail.Add(entry);
        }

        return entry;
    }
}

public sealed record NetworkFlowRecord(string CaptureKey, string Method, string Url, int StatusCode, string Summary);
