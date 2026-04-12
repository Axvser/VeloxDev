using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Agent.Workflow;

public sealed class WorkflowAgentScope(IWorkflowTreeViewModel tree) : IDisposable
{
    private readonly string _protocolSessionId = WorkflowProtocolTools.CreateBoundScopeSession(tree ?? throw new ArgumentNullException(nameof(tree)));
    private bool _disposed;

    public IWorkflowTreeViewModel Tree { get; } = tree;

    public IEnumerable<Delegate> ProvideWorkflowAgentTools()
    {
        ThrowIfDisposed();
        yield return WorkflowContextProvider.GetWorkflowHelper;
        yield return WorkflowContextProvider.GetComponentContext;
        yield return GetWorkflowBootstrap;
        yield return GetWorkflowContextSection;
        yield return GetWorkflowGraphSummary;
        yield return QueryWorkflowGraph;
        yield return GetWorkflowTargetCapabilities;
        yield return GetWorkflowPropertyValue;
        yield return ValidateWorkflowPatch;
        yield return ApplyWorkflowPatch;
        yield return InvokeWorkflowActionAsync;
        yield return InvokeWorkflowCommandAsync;
        yield return InvokeWorkflowMethodAsync;
        yield return GetWorkflowChanges;
        yield return GetWorkflowDiagnostics;
    }

    [Description("Read the compact workflow bootstrap document for the bound workflow tree scope.")]
    public string GetWorkflowBootstrap()
        => WorkflowProtocolTools.GetWorkflowBootstrap();

    [Description("Read one workflow semantic context section for the bound workflow tree scope.")]
    public string GetWorkflowContextSection([Description("A JSON request string containing `section`, optional `languageCode`, and optional `typeName` when section is `type`.")] string requestJson)
        => WorkflowProtocolTools.GetWorkflowContextSection(requestJson);

    [Description("Get the compact summary projection for the bound workflow tree scope.")]
    public string GetWorkflowGraphSummary()
        => StripSessionId(WorkflowProtocolTools.QueryWorkflowGraph(BuildProtocolScopedRequest("{\"queryMode\":\"summary\"}")));

    [Description("Query the bound workflow graph using the compact protocol. Do not provide `sessionId` or `tree`.")]
    public string QueryWorkflowGraph([Description("A JSON request string containing optional `queryMode`, optional stable `id`, and optional flags `includeSlots`, `includeLinks`, `includeConnections`, and `includeContext`. Do not provide `sessionId` or `tree`.")] string requestJson)
        => StripSessionId(WorkflowProtocolTools.QueryWorkflowGraph(BuildProtocolScopedRequest(requestJson)));

    [Description("Get the agent-controllable annotated properties, commands, and methods for one bound workflow target. Do not provide `sessionId` or `tree`.")]
    public string GetWorkflowTargetCapabilities([Description("A JSON request string containing one target selector: `targetId`, `nodeId`, `slotId`, `linkId`, or `targetTree` true. Do not provide `sessionId` or `tree`.")] string requestJson)
        => StripSessionId(WorkflowProtocolTools.GetWorkflowTargetCapabilities(BuildProtocolScopedRequest(requestJson)));

    [Description("Read one agent-controllable property value from a bound workflow target. Do not provide `sessionId` or `tree`.")]
    public string GetWorkflowPropertyValue([Description("A JSON request string containing a target selector and required `propertyPath`. Do not provide `sessionId` or `tree`.")] string requestJson)
        => StripSessionId(WorkflowProtocolTools.GetWorkflowPropertyValue(BuildProtocolScopedRequest(requestJson)));

    [Description("Perform a dry-run validation for a compact workflow patch against the bound workflow tree scope. Do not provide `sessionId` or `tree`.")]
    public string ValidateWorkflowPatch([Description("A JSON request string matching `ApplyWorkflowPatch`. Do not provide `sessionId` or `tree`.")] string requestJson)
        => StripSessionId(WorkflowProtocolTools.ValidateWorkflowPatch(BuildProtocolScopedRequest(requestJson)));

    [Description("Apply a compact batch patch to the bound workflow tree scope. Do not provide `sessionId` or `tree`.")]
    public string ApplyWorkflowPatch([Description("A JSON request string containing optional `expectedRevision`, optional `returnMode`, and required `operations` array. Do not provide `sessionId` or `tree`.")] string requestJson)
        => StripSessionId(WorkflowProtocolTools.ApplyWorkflowPatch(BuildProtocolScopedRequest(requestJson)));

    [Description("Invoke one runtime workflow action inside the bound workflow tree scope. Do not provide `sessionId` or `tree`.")]
    public Task<string> InvokeWorkflowActionAsync([Description("A JSON request string containing required `action`, optional stable ids such as `nodeId`, `slotId`, or `linkId`, and optional `parameter`. Do not provide `sessionId` or `tree`.")] string requestJson)
        => ExecuteProtocolAsync(WorkflowProtocolTools.InvokeWorkflowActionAsync, requestJson);

    [Description("Invoke one agent-controllable command on a bound workflow target. Do not provide `sessionId` or `tree`.")]
    public Task<string> InvokeWorkflowCommandAsync([Description("A JSON request string containing a target selector, required `commandName`, and optional `parameter`. Do not provide `sessionId` or `tree`.")] string requestJson)
        => ExecuteProtocolAsync(WorkflowProtocolTools.InvokeWorkflowCommandAsync, requestJson);

    [Description("Invoke one agent-controllable method on a bound workflow target. Do not provide `sessionId` or `tree`.")]
    public Task<string> InvokeWorkflowMethodAsync([Description("A JSON request string containing a target selector, required `methodName`, and optional `arguments` array. Do not provide `sessionId` or `tree`.")] string requestJson)
        => ExecuteProtocolAsync(WorkflowProtocolTools.InvokeWorkflowMethodAsync, requestJson);

    [Description("Get compact protocol change records for the bound workflow tree scope.")]
    public string GetWorkflowChanges([Description("A JSON request string containing optional `sinceRevision`. Do not provide `sessionId` or `tree`.")] string requestJson)
        => StripSessionId(WorkflowProtocolTools.GetWorkflowChanges(BuildProtocolScopedRequest(requestJson)));

    [Description("Get compact workflow diagnostics for the bound workflow tree scope.")]
    public string GetWorkflowDiagnostics()
        => StripSessionId(WorkflowProtocolTools.GetWorkflowDiagnostics(BuildProtocolScopedRequest(null)));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        WorkflowProtocolTools.ReleaseBoundScopeSession(_protocolSessionId);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async Task<string> ExecuteProtocolAsync(Func<string, Task<string>> tool, string requestJson)
        => StripSessionId(await tool.Invoke(BuildProtocolScopedRequest(requestJson)).ConfigureAwait(false));

    private string BuildProtocolScopedRequest(string? requestJson)
    {
        ThrowIfDisposed();
        var request = string.IsNullOrWhiteSpace(requestJson)
            ? new JObject()
            : JObject.Parse(requestJson!);

        if (request.Property("sessionId", StringComparison.OrdinalIgnoreCase) is not null)
        {
            throw new ArgumentException("Scoped workflow protocol requests must not contain `sessionId`.", nameof(requestJson));
        }

        if (request.Property("tree", StringComparison.OrdinalIgnoreCase) is not null)
        {
            throw new ArgumentException("Scoped workflow protocol requests must not contain `tree`.", nameof(requestJson));
        }

        request["sessionId"] = _protocolSessionId;
        return request.ToString(Formatting.None);
    }

    private static string StripSessionId(string responseJson)
    {
        var response = JObject.Parse(responseJson);
        response.Remove("sessionId");
        return response.ToString(Formatting.Indented);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WorkflowAgentScope));
        }
    }
}
