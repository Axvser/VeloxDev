using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

public static class WorkflowProtocolTools
{
    private const string SessionRequestJsonDescription = "A JSON request string containing `sessionId`.";
    private const string ContextSectionRequestJsonDescription = "A JSON request string containing `section`, optional `languageCode`, and optional `typeName` when section is `type`.";
    private const string SessionOpenRequestJsonDescription = "A JSON request string containing required `tree`, optional `sessionId`, and optional `languageCode`.";
    private const string SessionBoundRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, and optional `languageCode`. If both `sessionId` and `tree` are provided, the runtime session is refreshed from the latest tree snapshot.";
    private const string QueryRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, optional `languageCode`, optional `queryMode` (`summary`, `tree`, `node`, `slot`, `link`), optional stable `id`, and optional flags `includeSlots`, `includeLinks`, `includeConnections`, `includeContext`, and `includeJson`.";
    private const string PatchRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, optional `expectedRevision`, optional `returnMode` (`delta`, `affected`, `snapshot`), and required `operations` array. Supported operations include `createNode`, `deleteNode`, `moveNode`, `setNodeAnchor`, `setNodeSize`, `createSlot`, `deleteSlot`, `setSlotChannel`, `connectSlots`, `deleteLink`, `setLinkVisibility`, `setPointer`, `resetVirtualLink`, `setProperty`, `setProperties`, and `replaceObject`. Prefer stable ids such as `nodeId`, `slotId`, `linkId`, and `targetId` instead of index-based addressing.";
    private const string ValidatePatchRequestJsonDescription = "A JSON request string matching `ApplyWorkflowPatch`. Validation performs a dry-run on a detached workflow clone, returns detailed diagnostics, and never mutates the live session or advances `revision`.";
    private const string ActionRequestJsonDescription = "A JSON request string containing `sessionId`, required `action`, and optional stable ids such as `nodeId`, `slotId`, `linkId`, plus optional `parameter`. Supported actions include `work`, `broadcast`, `close`, `undo`, `redo`, and `clearHistory`.";
    private const string ChangesRequestJsonDescription = "A JSON request string containing `sessionId` and optional `sinceRevision`.";
    private const string TargetRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, optional `languageCode`, and one target selector: `targetId`, `nodeId`, `slotId`, `linkId`, or `targetTree` true.";
    private const string PropertyReadRequestJsonDescription = "A JSON request string containing a workflow target selector and required `propertyPath`. Use this to read one agent-controllable property precisely without fetching the whole object JSON.";
    private const string CommandInvokeRequestJsonDescription = "A JSON request string containing a workflow target selector, required `commandName`, and optional `parameter`. The command must resolve to an `ICommand` or `IVeloxCommand` property that is agent-controllable through `AgentContext`.";
    private const string MethodInvokeRequestJsonDescription = "A JSON request string containing a workflow target selector, required `methodName`, and optional `arguments` array. The method must be agent-controllable through `AgentContext`. Reflection is used to bind JSON arguments to the method signature.";

    private static readonly object SessionSyncRoot = new();
    private static readonly Dictionary<string, WorkflowProtocolSession> Sessions = [];

    [Description("Read this first. Returns a compact bootstrap document in English that explains the recommended workflow agent protocol, available high-value tools, context entry points, and token-saving strategy.")]
    public static string GetWorkflowBootstrap()
        => CreateBootstrapDocument(AgentLanguages.English);

    [Description("Returns the compact bootstrap document in the requested language code, such as `en`, `zh`, or `ja`.")]
    public static string GetWorkflowBootstrapInLanguage([Description("The target language code, such as `en`, `zh`, or `ja`.")] string languageCode)
        => CreateBootstrapDocument(ParseLanguage(languageCode));

    [Description("Returns one workflow context section on demand so the agent can read only the necessary semantic context instead of the full document.")]
    public static string GetWorkflowContextSection([Description(ContextSectionRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowContextSectionRequest>(requestJson);
        var language = ParseLanguage(request.LanguageCode);
        var section = request.Section?.Trim();
        if (string.IsNullOrWhiteSpace(section))
        {
            throw new ArgumentException("section cannot be null or empty.", nameof(requestJson));
        }

        var normalizedSection = section!.ToLowerInvariant();

        return normalizedSection switch
        {
            "bootstrap" => CreateBootstrapDocument(language),
            "document" => WorkflowAgentContextProvider.ProvideWorkflowAgentContextDocument(language),
            "framework" => WorkflowAgentContextProvider.ProvideWorkflowFrameworkContext(language),
            "enums" => WorkflowAgentContextProvider.ProvideWorkflowEnumContext(language),
            "valuetypes" => WorkflowAgentContextProvider.ProvideWorkflowValueTypeContext(language),
            "registeredcomponents" => WorkflowAgentContextProvider.ProvideRegisteredWorkflowComponentContext(language),
            "othermembers" => WorkflowAgentContextProvider.ProvideWorkflowOtherAnnotatedMemberContext(language),
            "registeredtypes" => WorkflowAgentTools.ListRegisteredWorkflowComponentTypes(),
            "type" => WorkflowAgentTools.GetWorkflowTypeAgentContextInLanguage(
                request.TypeName ?? throw new ArgumentException("typeName is required when section is `type`.", nameof(requestJson)),
                language.ToLanguageCode()),
            _ => throw new ArgumentException($"Unsupported context section: {section}", nameof(requestJson))
        };
    }

    [Description("Create or refresh a compact workflow protocol session from a workflow tree JSON payload. The response includes a stable session id, current revision, graph summary, context hashes, and a compact component catalog.")]
    public static string OpenWorkflowSession([Description(SessionOpenRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSessionOpenRequest>(requestJson);
        var tree = DeserializeTree(request.Tree, nameof(request.Tree));
        var language = ParseLanguage(request.LanguageCode);
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString("N")
            : request.SessionId!;

        var session = GetOrCreateSession(sessionId, tree, language);
        session.Language = language;
        session.RefreshTree(tree);
        return CreateSessionEnvelope(session, includeSummary: true, includeCatalog: true, includeChanges: false).ToString(Formatting.Indented);
    }

    [Description("Query the workflow graph with stable ids and compact projections. Use `queryMode` `summary` for the cheapest overview, or `tree`, `node`, `slot`, or `link` to fetch a specific object.")]
    public static string QueryWorkflowGraph([Description(QueryRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowQueryRequest>(requestJson);
        var session = ResolveSession(request, ParseLanguage(request.LanguageCode));
        var queryMode = string.IsNullOrWhiteSpace(request.QueryMode) ? "summary" : request.QueryMode!.Trim().ToLowerInvariant();
        var response = new JObject
        {
            ["sessionId"] = session.SessionId,
            ["revision"] = session.Revision,
            ["queryMode"] = queryMode,
            ["summary"] = CreateGraphSummary(session)
        };

        switch (queryMode)
        {
            case "summary":
                break;
            case "tree":
                response["tree"] = CreateTreeProjection(session, request.IncludeSlots, request.IncludeLinks, request.IncludeConnections, request.IncludeContext, request.IncludeJson);
                break;
            case "node":
                response["node"] = CreateNodeProjection(session, GetNodeById(session, request.Id), request.IncludeSlots, request.IncludeConnections, request.IncludeContext, request.IncludeJson);
                break;
            case "slot":
                response["slot"] = CreateSlotProjection(session, GetSlotById(session, request.Id), request.IncludeConnections, request.IncludeContext, request.IncludeJson);
                break;
            case "link":
                response["link"] = CreateLinkProjection(session, GetLinkById(session, request.Id), request.IncludeContext, request.IncludeJson);
                break;
            default:
                throw new ArgumentException($"Unsupported queryMode: {request.QueryMode}", nameof(requestJson));
        }

        return response.ToString(Formatting.Indented);
    }

    [Description("Return the compact agent-controllable capability list for one workflow target, including annotated properties, commands, and methods that can be taken over reflectively.")]
    public static string GetWorkflowTargetCapabilities([Description(TargetRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowTargetRequest>(requestJson);
        var session = ResolveSession(request, ParseLanguage(request.LanguageCode));
        var target = ResolvePatchTarget(session, request.ToTargetSelector());
        return new JObject
        {
            ["sessionId"] = session.SessionId,
            ["revision"] = session.Revision,
            ["targetId"] = target.Id,
            ["type"] = target.Instance.GetType().FullName ?? target.Instance.GetType().Name,
            ["properties"] = CreateAnnotatedPropertyCatalog(target.Instance.GetType(), session.Language),
            ["commands"] = CreateAnnotatedCommandCatalog(target.Instance.GetType(), session.Language),
            ["methods"] = CreateAnnotatedMethodCatalog(target.Instance.GetType(), session.Language)
        }.ToString(Formatting.Indented);
    }

    [Description("Read one agent-controllable property value from a workflow target by `propertyPath`.")]
    public static string GetWorkflowPropertyValue([Description(PropertyReadRequestJsonDescription)] string requestJson)
    {
        try
        {
            var request = DeserializeRequest<WorkflowPropertyReadRequest>(requestJson);
            var session = ResolveSession(request, ParseLanguage(request.LanguageCode));
            var target = ResolvePatchTarget(session, request.ToTargetSelector());
            var propertyPath = request.PropertyPath?.Trim();
            if (string.IsNullOrWhiteSpace(propertyPath))
            {
                throw new ArgumentException("propertyPath cannot be null or empty.", nameof(requestJson));
            }

            var value = GetPropertyPathValue(target.Instance, propertyPath!);
            return new JObject
            {
                ["sessionId"] = session.SessionId,
                ["revision"] = session.Revision,
                ["targetId"] = target.Id,
                ["propertyPath"] = propertyPath,
                ["value"] = SerializeArbitraryValue(value)
            }.ToString(Formatting.Indented);
        }
        catch (Exception ex)
        {
            return CreateStandaloneErrorEnvelope(MapExceptionToErrorCode(ex), ex.Message);
        }
    }

    [Description("Perform a dry-run validation for a workflow patch request. This never mutates the live workflow session and returns detailed error codes, warnings, and the predicted affected targets.")]
    public static string ValidateWorkflowPatch([Description(ValidatePatchRequestJsonDescription)] string requestJson)
    {
        try
        {
            var request = DeserializeRequest<WorkflowPatchRequest>(requestJson);
            var session = ResolveSession(request, ParseLanguage(request.LanguageCode));
            return ValidateWorkflowPatchCore(session, request).ToString(Formatting.Indented);
        }
        catch (Exception ex)
        {
            return CreateStandaloneErrorEnvelope(MapExceptionToErrorCode(ex), ex.Message);
        }
    }

    [Description("Apply a batch patch to a workflow session. This is the recommended low-token editing entry point because multiple graph edits can be committed atomically and the default response only returns a compact delta.")]
    public static string ApplyWorkflowPatch([Description(PatchRequestJsonDescription)] string requestJson)
    {
        try
        {
            var request = DeserializeRequest<WorkflowPatchRequest>(requestJson);
            var session = ResolveSession(request, ParseLanguage(request.LanguageCode));
            var validation = ValidateWorkflowPatchCore(session, request);
            if (!(validation.Value<bool?>("valid") ?? false))
            {
                return validation.ToString(Formatting.Indented);
            }

            var patchRecord = new JObject
            {
                ["revision"] = session.Revision + 1,
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["operations"] = new JArray()
            };
            var affected = new HashSet<string>(StringComparer.Ordinal);
            var deleted = new HashSet<string>(StringComparer.Ordinal);
            var warnings = new JArray();

            foreach (var operationToken in request.Operations!)
            {
                var operation = (JObject)operationToken;
                var applied = ApplyOperation(session, operation, affected, deleted, warnings);
                ((JArray)patchRecord["operations"]!).Add(applied);
            }

            session.IncrementRevision(patchRecord);
            var returnMode = string.IsNullOrWhiteSpace(request.ReturnMode) ? "delta" : request.ReturnMode!.Trim().ToLowerInvariant();
            var response = new JObject
            {
                ["sessionId"] = session.SessionId,
                ["revision"] = session.Revision,
                ["returnMode"] = returnMode,
                ["changes"] = CreateDelta(session, patchRecord, affected, deleted),
                ["warnings"] = warnings,
                ["summary"] = CreateGraphSummary(session)
            };

            if (returnMode == "affected")
            {
                response["affectedObjects"] = CreateAffectedObjects(session, affected);
            }
            else if (returnMode == "snapshot")
            {
                response["tree"] = CreateTreeProjection(session, includeSlots: true, includeLinks: true, includeConnections: true, includeContext: false, includeJson: false);
            }

            return response.ToString(Formatting.Indented);
        }
        catch (Exception ex)
        {
            return CreateStandaloneErrorEnvelope(MapExceptionToErrorCode(ex), ex.Message);
        }
    }

    [Description("Invoke one runtime workflow action on a live protocol session. Use actions such as `work`, `broadcast`, `close`, `undo`, `redo`, or `clearHistory`.")]
    public static async Task<string> InvokeWorkflowActionAsync([Description(ActionRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowActionRequest>(requestJson);
        var session = ResolveSession(request, ParseLanguage(request.LanguageCode));
        var action = request.Action?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("action cannot be null or empty.", nameof(requestJson));
        }

        object? parameter = ConvertParameter(request.Parameter);
        string? affectedId = null;

        switch (action)
        {
            case "work":
                var workNode = GetNodeById(session, request.NodeId);
                affectedId = session.EnsureNodeId(workNode);
                await workNode.GetHelper().WorkAsync(parameter, default).ConfigureAwait(false);
                break;
            case "broadcast":
                var broadcastNode = GetNodeById(session, request.NodeId);
                affectedId = session.EnsureNodeId(broadcastNode);
                await broadcastNode.GetHelper().BroadcastAsync(parameter, default).ConfigureAwait(false);
                break;
            case "close":
                await session.Tree.GetHelper().CloseAsync().ConfigureAwait(false);
                affectedId = session.TreeId;
                break;
            case "undo":
                session.Tree.GetHelper().Undo();
                affectedId = session.TreeId;
                break;
            case "redo":
                session.Tree.GetHelper().Redo();
                affectedId = session.TreeId;
                break;
            case "clearhistory":
                session.Tree.GetHelper().ClearHistory();
                affectedId = session.TreeId;
                break;
            default:
                throw new ArgumentException($"Unsupported action: {request.Action}", nameof(requestJson));
        }

        var record = new JObject
        {
            ["revision"] = session.Revision + 1,
            ["timestampUtc"] = DateTimeOffset.UtcNow,
            ["action"] = action,
            ["affectedId"] = affectedId
        };
        session.IncrementRevision(record);

        return new JObject
        {
            ["sessionId"] = session.SessionId,
            ["revision"] = session.Revision,
            ["action"] = action,
            ["affectedId"] = affectedId,
            ["summary"] = CreateGraphSummary(session)
        }.ToString(Formatting.Indented);
    }

    [Description("Invoke one agent-controllable command property on a workflow target. This supports standard workflow commands and user-defined command properties as long as they are reachable through `AgentContext`.")]
    public static async Task<string> InvokeWorkflowCommandAsync([Description(CommandInvokeRequestJsonDescription)] string requestJson)
    {
        try
        {
            var request = DeserializeRequest<WorkflowCommandInvokeRequest>(requestJson);
            var session = ResolveSession(request, ParseLanguage(request.LanguageCode));
            var target = ResolvePatchTarget(session, request.ToTargetSelector());
            var commandName = request.CommandName?.Trim();
            if (string.IsNullOrWhiteSpace(commandName))
            {
                throw new ArgumentException("commandName cannot be null or empty.", nameof(requestJson));
            }

            var commandProperty = FindAnnotatedCommandProperty(target.Instance.GetType(), commandName!);
            var command = commandProperty.GetValue(target.Instance) as ICommand ?? throw new InvalidOperationException($"Command '{commandProperty.Name}' is null or does not implement ICommand on type '{target.Instance.GetType().FullName}'.");
            var parameter = ConvertParameter(request.Parameter);
            if (!command.CanExecute(parameter))
            {
                return CreateErrorEnvelope(session, WorkflowProtocolErrorCodes.CommandNotExecutable, $"Command '{commandProperty.Name}' rejected the provided parameter.");
            }

            if (command is IVeloxCommand veloxCommand)
            {
                await veloxCommand.ExecuteAsync(parameter).ConfigureAwait(false);
            }
            else
            {
                command.Execute(parameter);
            }

            var record = new JObject
            {
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["commandName"] = commandProperty.Name,
                ["targetId"] = target.Id,
                ["kind"] = "commandInvocation"
            };
            session.IncrementRevision(record);

            return new JObject
            {
                ["sessionId"] = session.SessionId,
                ["revision"] = session.Revision,
                ["targetId"] = target.Id,
                ["commandName"] = commandProperty.Name,
                ["summary"] = CreateGraphSummary(session)
            }.ToString(Formatting.Indented);
        }
        catch (Exception ex)
        {
            return CreateStandaloneErrorEnvelope(MapExceptionToErrorCode(ex), ex.Message);
        }
    }

    [Description("Invoke one agent-controllable method on a workflow target by reflection. JSON arguments are bound to the method signature, and asynchronous methods are awaited automatically.")]
    public static async Task<string> InvokeWorkflowMethodAsync([Description(MethodInvokeRequestJsonDescription)] string requestJson)
    {
        try
        {
            var request = DeserializeRequest<WorkflowMethodInvokeRequest>(requestJson);
            var session = ResolveSession(request, ParseLanguage(request.LanguageCode));
            var target = ResolvePatchTarget(session, request.ToTargetSelector());
            var methodName = request.MethodName?.Trim();
            if (string.IsNullOrWhiteSpace(methodName))
            {
                throw new ArgumentException("methodName cannot be null or empty.", nameof(requestJson));
            }

            var arguments = request.Arguments ?? [];
            var method = FindAnnotatedMethod(target.Instance.GetType(), methodName!, arguments);
            var invocationArguments = BindMethodArguments(method, arguments);
            var invocationResult = method.Invoke(target.Instance, invocationArguments);
            var result = await AwaitMethodResultAsync(invocationResult, method.ReturnType).ConfigureAwait(false);

            var record = new JObject
            {
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["methodName"] = method.Name,
                ["targetId"] = target.Id,
                ["kind"] = "methodInvocation"
            };
            session.IncrementRevision(record);

            return new JObject
            {
                ["sessionId"] = session.SessionId,
                ["revision"] = session.Revision,
                ["targetId"] = target.Id,
                ["methodName"] = method.Name,
                ["result"] = SerializeArbitraryValue(result),
                ["summary"] = CreateGraphSummary(session)
            }.ToString(Formatting.Indented);
        }
        catch (Exception ex)
        {
            return CreateStandaloneErrorEnvelope(MapExceptionToErrorCode(ex), ex.Message);
        }
    }

    [Description("Get compact protocol change records since a revision. Use this instead of repeatedly requesting the whole workflow tree.")]
    public static string GetWorkflowChanges([Description(ChangesRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowChangesRequest>(requestJson);
        var session = GetSession(RequireSessionId(request.SessionId));
        var sinceRevision = Math.Max(0, request.SinceRevision ?? 0);
        var changes = new JArray(session.ChangeLog.Where(change => change.Value<int>("revision") > sinceRevision));
        return new JObject
        {
            ["sessionId"] = session.SessionId,
            ["currentRevision"] = session.Revision,
            ["sinceRevision"] = sinceRevision,
            ["changes"] = changes
        }.ToString(Formatting.Indented);
    }

    [Description("Return compact workflow diagnostics for the current graph, including parent consistency, broken link endpoints, and counts by workflow role.")]
    public static string GetWorkflowDiagnostics([Description(SessionBoundRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSessionBoundRequest>(requestJson);
        var session = ResolveSession(request, ParseLanguage(request.LanguageCode));
        return new JObject
        {
            ["sessionId"] = session.SessionId,
            ["revision"] = session.Revision,
            ["summary"] = CreateGraphSummary(session),
            ["diagnostics"] = CreateDiagnostics(session)
        }.ToString(Formatting.Indented);
    }

    [Description("Release a workflow protocol session and report whether it was removed.")]
    public static string ReleaseWorkflowProtocolSession([Description(SessionRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowChangesRequest>(requestJson);
        var sessionId = RequireSessionId(request.SessionId);
        return new JObject
        {
            ["sessionId"] = sessionId,
            ["released"] = RemoveSession(sessionId)
        }.ToString(Formatting.Indented);
    }

    internal static string CreateBoundScopeSession(IWorkflowTreeViewModel tree)
    {
        if (tree is null)
        {
            throw new ArgumentNullException(nameof(tree));
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var session = GetOrCreateSession(sessionId, tree, AgentLanguages.English);
        session.RefreshTree(tree);
        return sessionId;
    }

    internal static bool ReleaseBoundScopeSession(string sessionId)
        => RemoveSession(sessionId);

    private static WorkflowProtocolSession ResolveSession(WorkflowSessionBoundRequest request, AgentLanguages language)
    {
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId;
        if (request.Tree is not null)
        {
            var tree = DeserializeTree(request.Tree, nameof(request.Tree));
            var resolvedSessionId = sessionId ?? Guid.NewGuid().ToString("N");
            var session = GetOrCreateSession(resolvedSessionId, tree, language);
            session.Language = language;
            session.RefreshTree(tree);
            return session;
        }

        if (sessionId is null)
        {
            throw new ArgumentException("Request must contain either sessionId or tree.", nameof(request));
        }

        var existing = GetSession(sessionId);
        existing.Language = language;
        existing.SyncIds();
        return existing;
    }

    private static WorkflowProtocolSession GetOrCreateSession(string sessionId, IWorkflowTreeViewModel tree, AgentLanguages language)
    {
        lock (SessionSyncRoot)
        {
            if (!Sessions.TryGetValue(sessionId, out var session))
            {
                session = new WorkflowProtocolSession(sessionId, tree, language);
                Sessions[sessionId] = session;
            }
            else
            {
                session.Language = language;
                session.RefreshTree(tree);
            }

            return session;
        }
    }

    private static WorkflowProtocolSession GetSession(string sessionId)
    {
        lock (SessionSyncRoot)
        {
            if (Sessions.TryGetValue(sessionId, out var session))
            {
                return session;
            }
        }

        throw new KeyNotFoundException($"Workflow protocol session '{sessionId}' was not found.");
    }

    private static bool RemoveSession(string sessionId)
    {
        lock (SessionSyncRoot)
        {
            return Sessions.Remove(sessionId);
        }
    }

    private static JObject ValidateWorkflowPatchCore(WorkflowProtocolSession session, WorkflowPatchRequest request)
    {
        if (request.ExpectedRevision.HasValue && request.ExpectedRevision.Value != session.Revision)
        {
            return new JObject
            {
                ["sessionId"] = session.SessionId,
                ["revision"] = session.Revision,
                ["expectedRevision"] = request.ExpectedRevision.Value,
                ["valid"] = false,
                ["wouldAffect"] = new JArray(),
                ["wouldDelete"] = new JArray(),
                ["warnings"] = new JArray(),
                ["errors"] = new JArray(CreateErrorEntry(WorkflowProtocolErrorCodes.RevisionConflict, $"Expected revision {request.ExpectedRevision.Value}, but actual revision is {session.Revision}."))
            };
        }

        if (request.Operations is null || request.Operations.Count == 0)
        {
            return new JObject
            {
                ["sessionId"] = session.SessionId,
                ["revision"] = session.Revision,
                ["valid"] = false,
                ["wouldAffect"] = new JArray(),
                ["wouldDelete"] = new JArray(),
                ["warnings"] = new JArray(),
                ["errors"] = new JArray(CreateErrorEntry(WorkflowProtocolErrorCodes.InvalidPatchRequest, "operations cannot be null or empty."))
            };
        }

        var validationSession = CreateValidationSession(session);
        var wouldAffect = new HashSet<string>(StringComparer.Ordinal);
        var wouldDelete = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new JArray();
        var errors = new JArray();
        var operations = new JArray();

        for (var index = 0; index < request.Operations.Count; index++)
        {
            var operationToken = request.Operations[index];
            if (operationToken is not JObject operation)
            {
                errors.Add(CreateErrorEntry(WorkflowProtocolErrorCodes.InvalidPatchRequest, $"Operation at index {index} must be a JSON object.", index));
                continue;
            }

            try
            {
                operations.Add(ApplyOperation(validationSession, operation, wouldAffect, wouldDelete, warnings));
            }
            catch (Exception ex)
            {
                errors.Add(CreateErrorEntry(MapExceptionToErrorCode(ex), ex.Message, index));
            }
        }

        return new JObject
        {
            ["sessionId"] = session.SessionId,
            ["revision"] = session.Revision,
            ["expectedRevision"] = request.ExpectedRevision,
            ["valid"] = errors.Count == 0,
            ["wouldAffect"] = new JArray(wouldAffect.OrderBy(static id => id, StringComparer.Ordinal)),
            ["wouldDelete"] = new JArray(wouldDelete.OrderBy(static id => id, StringComparer.Ordinal)),
            ["warnings"] = warnings,
            ["errors"] = errors,
            ["operations"] = operations,
            ["predictedSummary"] = CreateGraphSummary(validationSession)
        };
    }

    private static WorkflowProtocolSession CreateValidationSession(WorkflowProtocolSession source)
    {
        var clonedTree = DeserializeTree(ParseJson(source.Tree.Serialize()), nameof(source.Tree));
        var clone = new WorkflowProtocolSession($"{source.SessionId}-validation", clonedTree, source.Language);
        clone.CopyStateFrom(source);
        clone.IdMap.Clear();
        clone.IdMap[clone.Tree] = source.TreeId;

        for (var nodeIndex = 0; nodeIndex < source.Tree.Nodes.Count && nodeIndex < clone.Tree.Nodes.Count; nodeIndex++)
        {
            var sourceNode = source.Tree.Nodes[nodeIndex];
            var cloneNode = clone.Tree.Nodes[nodeIndex];
            clone.IdMap[cloneNode] = source.EnsureNodeId(sourceNode);

            for (var slotIndex = 0; slotIndex < sourceNode.Slots.Count && slotIndex < cloneNode.Slots.Count; slotIndex++)
            {
                clone.IdMap[cloneNode.Slots[slotIndex]] = source.EnsureSlotId(sourceNode.Slots[slotIndex]);
            }
        }

        for (var linkIndex = 0; linkIndex < source.Tree.Links.Count && linkIndex < clone.Tree.Links.Count; linkIndex++)
        {
            clone.IdMap[clone.Tree.Links[linkIndex]] = source.EnsureLinkId(source.Tree.Links[linkIndex]);
        }

        clone.ChangeLog.Clear();
        clone.ChangeLog.AddRange(source.ChangeLog.Select(static change => (JObject)change.DeepClone()));
        clone.SyncIds();
        return clone;
    }

    private static JObject ApplyOperation(WorkflowProtocolSession session, JObject operation, HashSet<string> affected, HashSet<string> deleted, JArray warnings)
    {
        var op = operation.Value<string>("op")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(op))
        {
            throw new ArgumentException("Each operation must contain a non-empty `op` field.", nameof(operation));
        }

        switch (op)
        {
            case "createnode":
                {
                    var node = DeserializeNode(operation["node"], "node");
                    session.Tree.GetHelper().CreateNode(node);
                    var nodeId = session.EnsureNodeId(node);
                    affected.Add(nodeId);
                    return new JObject { ["op"] = op, ["createdNodeId"] = nodeId };
                }
            case "deletenode":
                {
                    var node = GetNodeById(session, operation.Value<string>("nodeId"));
                    var nodeId = session.EnsureNodeId(node);
                    node.GetHelper().Delete();
                    deleted.Add(nodeId);
                    return new JObject { ["op"] = op, ["deletedNodeId"] = nodeId };
                }
            case "movenode":
                {
                    var node = GetNodeById(session, operation.Value<string>("nodeId"));
                    var nodeId = session.EnsureNodeId(node);
                    node.GetHelper().Move(DeserializeValue<Offset>(operation["offset"], "offset"));
                    affected.Add(nodeId);
                    return new JObject { ["op"] = op, ["nodeId"] = nodeId };
                }
            case "setnodeanchor":
                {
                    var node = GetNodeById(session, operation.Value<string>("nodeId"));
                    var nodeId = session.EnsureNodeId(node);
                    node.GetHelper().SetAnchor(DeserializeValue<Anchor>(operation["anchor"], "anchor"));
                    affected.Add(nodeId);
                    return new JObject { ["op"] = op, ["nodeId"] = nodeId };
                }
            case "setnodesize":
                {
                    var node = GetNodeById(session, operation.Value<string>("nodeId"));
                    var nodeId = session.EnsureNodeId(node);
                    node.GetHelper().SetSize(DeserializeValue<Size>(operation["size"], "size"));
                    affected.Add(nodeId);
                    return new JObject { ["op"] = op, ["nodeId"] = nodeId };
                }
            case "createslot":
                {
                    var node = GetNodeById(session, operation.Value<string>("nodeId"));
                    var nodeId = session.EnsureNodeId(node);
                    var slot = DeserializeSlot(operation["slot"], "slot");
                    node.GetHelper().CreateSlot(slot);
                    var slotId = session.EnsureSlotId(slot);
                    affected.Add(nodeId);
                    affected.Add(slotId);
                    return new JObject { ["op"] = op, ["nodeId"] = nodeId, ["createdSlotId"] = slotId };
                }
            case "deleteslot":
                {
                    var slot = GetSlotById(session, operation.Value<string>("slotId"));
                    var slotId = session.EnsureSlotId(slot);
                    slot.GetHelper().Delete();
                    deleted.Add(slotId);
                    return new JObject { ["op"] = op, ["deletedSlotId"] = slotId };
                }
            case "setslotchannel":
                {
                    var slot = GetSlotById(session, operation.Value<string>("slotId"));
                    var slotId = session.EnsureSlotId(slot);
                    slot.GetHelper().SetChannel(DeserializeEnum<SlotChannel>(operation["channel"], "channel"));
                    affected.Add(slotId);
                    return new JObject { ["op"] = op, ["slotId"] = slotId };
                }
            case "connectslots":
                {
                    var sender = GetSlotById(session, operation.Value<string>("senderSlotId"));
                    var receiver = GetSlotById(session, operation.Value<string>("receiverSlotId"));
                    if (!session.Tree.GetHelper().ValidateConnection(sender, receiver))
                    {
                        warnings.Add(CreateWarning(WorkflowProtocolErrorCodes.InvalidConnection, "The current tree helper rejected the connection request."));
                        return new JObject { ["op"] = op, ["applied"] = false };
                    }

                    session.Tree.GetHelper().SendConnection(sender);
                    session.Tree.GetHelper().ReceiveConnection(receiver);
                    affected.Add(session.EnsureSlotId(sender));
                    affected.Add(session.EnsureSlotId(receiver));
                    return new JObject { ["op"] = op, ["applied"] = true };
                }
            case "deletelink":
                {
                    var link = GetLinkById(session, operation.Value<string>("linkId"));
                    var linkId = session.EnsureLinkId(link);
                    link.GetHelper().Delete();
                    deleted.Add(linkId);
                    return new JObject { ["op"] = op, ["deletedLinkId"] = linkId };
                }
            case "setlinkvisibility":
                {
                    var link = GetLinkById(session, operation.Value<string>("linkId"));
                    var linkId = session.EnsureLinkId(link);
                    link.IsVisible = operation.Value<bool?>("isVisible") ?? throw new ArgumentException("isVisible is required.", nameof(operation));
                    affected.Add(linkId);
                    return new JObject { ["op"] = op, ["linkId"] = linkId };
                }
            case "setpointer":
                {
                    session.Tree.GetHelper().SetPointer(DeserializeValue<Anchor>(operation["anchor"], "anchor"));
                    affected.Add(session.TreeId);
                    return new JObject { ["op"] = op, ["treeId"] = session.TreeId };
                }
            case "resetvirtuallink":
                {
                    session.Tree.GetHelper().ResetVirtualLink();
                    affected.Add(session.TreeId);
                    return new JObject { ["op"] = op, ["treeId"] = session.TreeId };
                }
            case "setproperty":
                {
                    var target = ResolvePatchTarget(session, operation);
                    var propertyPath = operation.Value<string>("propertyPath");
                    if (string.IsNullOrWhiteSpace(propertyPath))
                    {
                        throw new ArgumentException("propertyPath is required for `setProperty`.", nameof(operation));
                    }

                    SetPropertyPathValue(target.Instance, propertyPath!, operation["value"]);
                    affected.Add(target.Id);
                    return new JObject { ["op"] = op, ["targetId"] = target.Id, ["propertyPath"] = propertyPath };
                }
            case "setproperties":
                {
                    var target = ResolvePatchTarget(session, operation);
                    if (operation["properties"] is not JObject properties)
                    {
                        throw new ArgumentException("properties must be a JSON object for `setProperties`.", nameof(operation));
                    }

                    var changed = new JArray();
                    foreach (var property in properties.Properties())
                    {
                        SetPropertyPathValue(target.Instance, property.Name, property.Value);
                        changed.Add(property.Name);
                    }

                    affected.Add(target.Id);
                    return new JObject { ["op"] = op, ["targetId"] = target.Id, ["propertyPaths"] = changed };
                }
            case "replaceobject":
                {
                    var target = ResolvePatchTarget(session, operation);
                    MergeObjectSnapshot(target.Instance, operation["object"] ?? throw new ArgumentException("object is required for `replaceObject`.", nameof(operation)));
                    affected.Add(target.Id);
                    return new JObject { ["op"] = op, ["targetId"] = target.Id };
                }
            default:
                throw new ArgumentException($"Unsupported patch operation: {op}", nameof(operation));
        }
    }

    private static JObject CreateSessionEnvelope(WorkflowProtocolSession session, bool includeSummary, bool includeCatalog, bool includeChanges)
    {
        var response = new JObject
        {
            ["sessionId"] = session.SessionId,
            ["revision"] = session.Revision,
            ["languageCode"] = session.Language.ToLanguageCode(),
            ["schemaHash"] = ComputeSha256(WorkflowAgentContextProvider.ProvideWorkflowFrameworkContext(AgentLanguages.English)),
            ["contextHash"] = ComputeSha256(WorkflowAgentContextProvider.ProvideWorkflowAgentContextDocument(session.Language))
        };

        if (includeSummary)
        {
            response["summary"] = CreateGraphSummary(session);
        }

        if (includeCatalog)
        {
            response["componentCatalog"] = CreateComponentCatalog(session);
        }

        if (includeChanges)
        {
            response["changes"] = new JArray(session.ChangeLog);
        }

        return response;
    }

    private static JObject CreateGraphSummary(WorkflowProtocolSession session)
    {
        session.SyncIds();
        return new JObject
        {
            ["treeId"] = session.TreeId,
            ["treeType"] = session.Tree.GetType().FullName ?? session.Tree.GetType().Name,
            ["nodeCount"] = session.Tree.Nodes.Count,
            ["slotCount"] = session.Tree.Nodes.Sum(static node => node.Slots.Count),
            ["linkCount"] = session.Tree.Links.Count,
            ["nodeTypes"] = new JArray(session.Tree.Nodes.Select(static node => node.GetType().FullName ?? node.GetType().Name).Distinct().OrderBy(static name => name, StringComparer.Ordinal)),
            ["slotTypes"] = new JArray(session.Tree.Nodes.SelectMany(static node => node.Slots).Select(static slot => slot.GetType().FullName ?? slot.GetType().Name).Distinct().OrderBy(static name => name, StringComparer.Ordinal)),
            ["linkTypes"] = new JArray(session.Tree.Links.Select(static link => link.GetType().FullName ?? link.GetType().Name).Distinct().OrderBy(static name => name, StringComparer.Ordinal))
        };
    }

    private static JArray CreateComponentCatalog(WorkflowProtocolSession session)
        => new(
            session.Tree.Nodes
                .Select(node => new JObject
                {
                    ["nodeId"] = session.EnsureNodeId(node),
                    ["type"] = node.GetType().FullName ?? node.GetType().Name,
                    ["slotCount"] = node.Slots.Count,
                    ["slots"] = new JArray(node.Slots.Select(slot => new JObject
                    {
                        ["slotId"] = session.EnsureSlotId(slot),
                        ["type"] = slot.GetType().FullName ?? slot.GetType().Name,
                        ["channel"] = slot.Channel.ToString(),
                        ["state"] = slot.State.ToString()
                    }))
                }));

    private static JObject CreateTreeProjection(WorkflowProtocolSession session, bool includeSlots, bool includeLinks, bool includeConnections, bool includeContext, bool includeJson)
        => new()
        {
            ["treeId"] = session.TreeId,
            ["type"] = session.Tree.GetType().FullName ?? session.Tree.GetType().Name,
            ["context"] = includeContext ? CreateTypeContextToken(session.Tree.GetType(), session.Language) : null,
            ["json"] = includeJson ? ParseJson(session.Tree.Serialize()) : null,
            ["nodes"] = new JArray(session.Tree.Nodes.Select(node => CreateNodeProjection(session, node, includeSlots, includeConnections, includeContext, includeJson))),
            ["links"] = includeLinks
                ? new JArray(session.Tree.Links.Select(link => CreateLinkProjection(session, link, includeContext, includeJson)))
                : new JArray()
        };

    private static JObject CreateNodeProjection(WorkflowProtocolSession session, IWorkflowNodeViewModel node, bool includeSlots, bool includeConnections, bool includeContext, bool includeJson)
        => new()
        {
            ["id"] = session.EnsureNodeId(node),
            ["type"] = node.GetType().FullName ?? node.GetType().Name,
            ["anchor"] = JToken.FromObject(node.Anchor),
            ["size"] = JToken.FromObject(node.Size),
            ["slotCount"] = node.Slots.Count,
            ["context"] = includeContext ? CreateTypeContextToken(node.GetType(), session.Language) : null,
            ["json"] = includeJson ? ParseJson(node.Serialize()) : null,
            ["slots"] = includeSlots
                ? new JArray(node.Slots.Select(slot => CreateSlotProjection(session, slot, includeConnections, includeContext, includeJson)))
                : new JArray()
        };

    private static JObject CreateSlotProjection(WorkflowProtocolSession session, IWorkflowSlotViewModel slot, bool includeConnections, bool includeContext, bool includeJson)
        => new()
        {
            ["id"] = session.EnsureSlotId(slot),
            ["type"] = slot.GetType().FullName ?? slot.GetType().Name,
            ["channel"] = slot.Channel.ToString(),
            ["state"] = slot.State.ToString(),
            ["anchor"] = JToken.FromObject(slot.Anchor),
            ["parentNodeId"] = slot.Parent is null ? null : session.EnsureNodeId(slot.Parent),
            ["context"] = includeContext ? CreateTypeContextToken(slot.GetType(), session.Language) : null,
            ["json"] = includeJson ? ParseJson(slot.Serialize()) : null,
            ["targets"] = includeConnections ? new JArray(slot.Targets.Select(target => session.EnsureSlotId(target))) : new JArray(),
            ["sources"] = includeConnections ? new JArray(slot.Sources.Select(source => session.EnsureSlotId(source))) : new JArray()
        };

    private static JObject CreateLinkProjection(WorkflowProtocolSession session, IWorkflowLinkViewModel link, bool includeContext, bool includeJson)
        => new()
        {
            ["id"] = session.EnsureLinkId(link),
            ["type"] = link.GetType().FullName ?? link.GetType().Name,
            ["senderSlotId"] = session.EnsureSlotId(link.Sender),
            ["receiverSlotId"] = session.EnsureSlotId(link.Receiver),
            ["isVisible"] = link.IsVisible,
            ["context"] = includeContext ? CreateTypeContextToken(link.GetType(), session.Language) : null,
            ["json"] = includeJson ? ParseJson(link.Serialize()) : null
        };

    private static JObject CreateDiagnostics(WorkflowProtocolSession session)
    {
        var issues = new JArray();
        foreach (var node in session.Tree.Nodes)
        {
            if (!ReferenceEquals(node.Parent, session.Tree))
            {
                issues.Add(CreateWarning("NodeParentMismatch", $"Node '{session.EnsureNodeId(node)}' does not point back to the current tree as Parent."));
            }

            foreach (var slot in node.Slots)
            {
                if (!ReferenceEquals(slot.Parent, node))
                {
                    issues.Add(CreateWarning("SlotParentMismatch", $"Slot '{session.EnsureSlotId(slot)}' does not point back to the current node as Parent."));
                }
            }
        }

        foreach (var link in session.Tree.Links)
        {
            if (link.Sender is null || link.Receiver is null)
            {
                issues.Add(CreateWarning("BrokenLinkEndpoint", $"Link '{session.EnsureLinkId(link)}' has a null sender or receiver."));
                continue;
            }

            if (!ContainsSlot(session.Tree, link.Sender) || !ContainsSlot(session.Tree, link.Receiver))
            {
                issues.Add(CreateWarning("DetachedLinkEndpoint", $"Link '{session.EnsureLinkId(link)}' refers to a slot that is not currently attached to the workflow tree."));
            }
        }

        return new JObject
        {
            ["issueCount"] = issues.Count,
            ["issues"] = issues
        };
    }

    private static JObject CreateDelta(WorkflowProtocolSession session, JObject patchRecord, HashSet<string> affected, HashSet<string> deleted)
        => new()
        {
            ["record"] = patchRecord,
            ["affectedIds"] = new JArray(affected.OrderBy(static id => id, StringComparer.Ordinal)),
            ["deletedIds"] = new JArray(deleted.OrderBy(static id => id, StringComparer.Ordinal))
        };

    private static JArray CreateAffectedObjects(WorkflowProtocolSession session, HashSet<string> affected)
    {
        var results = new JArray();
        foreach (var id in affected.OrderBy(static item => item, StringComparer.Ordinal))
        {
            var token = TryCreateProjectionById(session, id);
            if (token is not null)
            {
                results.Add(token);
            }
        }

        return results;
    }

    private static JToken? TryCreateProjectionById(WorkflowProtocolSession session, string id)
    {
        foreach (var node in session.Tree.Nodes)
        {
            if (string.Equals(session.EnsureNodeId(node), id, StringComparison.Ordinal))
            {
                return CreateNodeProjection(session, node, includeSlots: true, includeConnections: true, includeContext: false, includeJson: false);
            }

            foreach (var slot in node.Slots)
            {
                if (string.Equals(session.EnsureSlotId(slot), id, StringComparison.Ordinal))
                {
                    return CreateSlotProjection(session, slot, includeConnections: true, includeContext: false, includeJson: false);
                }
            }
        }

        foreach (var link in session.Tree.Links)
        {
            if (string.Equals(session.EnsureLinkId(link), id, StringComparison.Ordinal))
            {
                return CreateLinkProjection(session, link, includeContext: false, includeJson: false);
            }
        }

        if (string.Equals(session.TreeId, id, StringComparison.Ordinal))
        {
            return CreateTreeProjection(session, includeSlots: true, includeLinks: true, includeConnections: true, includeContext: false, includeJson: false);
        }

        return null;
    }

    private static JValue CreateTypeContextToken(Type type, AgentLanguages language)
        => new(WorkflowAgentContextProvider.ProvideTypeAgentContext(type, language));

    private static IWorkflowNodeViewModel GetNodeById(WorkflowProtocolSession session, string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("nodeId cannot be null or empty.", nameof(nodeId));
        }

        var node = session.Tree.Nodes.FirstOrDefault(candidate => string.Equals(session.EnsureNodeId(candidate), nodeId, StringComparison.Ordinal));
        return node ?? throw new KeyNotFoundException($"Workflow node '{nodeId}' was not found.");
    }

    private static IWorkflowSlotViewModel GetSlotById(WorkflowProtocolSession session, string? slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException("slotId cannot be null or empty.", nameof(slotId));
        }

        var slot = session.Tree.Nodes.SelectMany(static node => node.Slots).FirstOrDefault(candidate => string.Equals(session.EnsureSlotId(candidate), slotId, StringComparison.Ordinal));
        return slot ?? throw new KeyNotFoundException($"Workflow slot '{slotId}' was not found.");
    }

    private static IWorkflowLinkViewModel GetLinkById(WorkflowProtocolSession session, string? linkId)
    {
        if (string.IsNullOrWhiteSpace(linkId))
        {
            throw new ArgumentException("linkId cannot be null or empty.", nameof(linkId));
        }

        var link = session.Tree.Links.FirstOrDefault(candidate => string.Equals(session.EnsureLinkId(candidate), linkId, StringComparison.Ordinal));
        return link ?? throw new KeyNotFoundException($"Workflow link '{linkId}' was not found.");
    }

    private static PatchTarget ResolvePatchTarget(WorkflowProtocolSession session, JObject operation)
    {
        if (operation.TryGetValue("targetId", StringComparison.OrdinalIgnoreCase, out var targetIdToken))
        {
            var targetId = targetIdToken?.Value<string>();
            if (string.Equals(targetId, session.TreeId, StringComparison.Ordinal))
            {
                return new PatchTarget(session.TreeId, session.Tree);
            }

            foreach (var node in session.Tree.Nodes)
            {
                var nodeId = session.EnsureNodeId(node);
                if (string.Equals(nodeId, targetId, StringComparison.Ordinal))
                {
                    return new PatchTarget(nodeId, node);
                }

                foreach (var slot in node.Slots)
                {
                    var slotId = session.EnsureSlotId(slot);
                    if (string.Equals(slotId, targetId, StringComparison.Ordinal))
                    {
                        return new PatchTarget(slotId, slot);
                    }
                }
            }

            foreach (var link in session.Tree.Links)
            {
                var linkId = session.EnsureLinkId(link);
                if (string.Equals(linkId, targetId, StringComparison.Ordinal))
                {
                    return new PatchTarget(linkId, link);
                }
            }

            throw new KeyNotFoundException($"Workflow target '{targetId}' was not found.");
        }

        if (operation.Property("nodeId", StringComparison.OrdinalIgnoreCase) is not null)
        {
            var node = GetNodeById(session, operation.Value<string>("nodeId"));
            return new PatchTarget(session.EnsureNodeId(node), node);
        }

        if (operation.Property("slotId", StringComparison.OrdinalIgnoreCase) is not null)
        {
            var slot = GetSlotById(session, operation.Value<string>("slotId"));
            return new PatchTarget(session.EnsureSlotId(slot), slot);
        }

        if (operation.Property("linkId", StringComparison.OrdinalIgnoreCase) is not null)
        {
            var link = GetLinkById(session, operation.Value<string>("linkId"));
            return new PatchTarget(session.EnsureLinkId(link), link);
        }

        if (operation.Value<bool?>("targetTree") == true)
        {
            return new PatchTarget(session.TreeId, session.Tree);
        }

        throw new ArgumentException("A patch target must specify `targetId`, `nodeId`, `slotId`, `linkId`, or `targetTree`.", nameof(operation));
    }

    private static PatchTarget ResolvePatchTarget(WorkflowProtocolSession session, WorkflowTargetSelector selector)
    {
        var operation = new JObject();
        if (!string.IsNullOrWhiteSpace(selector.TargetId)) operation["targetId"] = selector.TargetId;
        if (!string.IsNullOrWhiteSpace(selector.NodeId)) operation["nodeId"] = selector.NodeId;
        if (!string.IsNullOrWhiteSpace(selector.SlotId)) operation["slotId"] = selector.SlotId;
        if (!string.IsNullOrWhiteSpace(selector.LinkId)) operation["linkId"] = selector.LinkId;
        if (selector.TargetTree) operation["targetTree"] = true;
        return ResolvePatchTarget(session, operation);
    }

    private static JArray CreateAnnotatedPropertyCatalog(Type type, AgentLanguages language)
        => new(GetAnnotatedPropertyEntries(type, language).Select(entry => new JObject
        {
            ["name"] = entry.Property.Name,
            ["valueType"] = entry.Property.PropertyType.FullName ?? entry.Property.PropertyType.Name,
            ["context"] = entry.Context
        }));

    private static JArray CreateAnnotatedCommandCatalog(Type type, AgentLanguages language)
        => new(GetAnnotatedPropertyEntries(type, language)
            .Where(static entry => typeof(ICommand).IsAssignableFrom(entry.Property.PropertyType))
            .Select(entry => new JObject
            {
                ["name"] = entry.Property.Name,
                ["valueType"] = entry.Property.PropertyType.FullName ?? entry.Property.PropertyType.Name,
                ["context"] = entry.Context
            }));

    private static JArray CreateAnnotatedMethodCatalog(Type type, AgentLanguages language)
        => new(GetAnnotatedMethodEntries(type, language).Select(entry => new JObject
        {
            ["name"] = entry.Method.Name,
            ["returnType"] = entry.Method.ReturnType.FullName ?? entry.Method.ReturnType.Name,
            ["context"] = entry.Context,
            ["parameters"] = new JArray(entry.Method.GetParameters().Select(parameter => new JObject
            {
                ["name"] = parameter.Name,
                ["type"] = parameter.ParameterType.FullName ?? parameter.ParameterType.Name,
                ["isOptional"] = parameter.IsOptional
            }))
        }));

    private static object? GetPropertyPathValue(object target, string propertyPath)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            throw new ArgumentException("propertyPath cannot be null or empty.", nameof(propertyPath));
        }

        object? current = target;
        foreach (var segment in propertyPath.Split(['.'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is null)
            {
                return null;
            }

            var property = GetAnnotatedReadableProperty(current.GetType(), segment);
            current = property.GetValue(current);
        }

        return current;
    }

    private static PropertyInfo FindAnnotatedCommandProperty(Type type, string commandName)
        => GetAnnotatedPropertyEntries(type, AgentLanguages.English)
            .Where(static entry => typeof(ICommand).IsAssignableFrom(entry.Property.PropertyType))
            .Select(static entry => entry.Property)
            .FirstOrDefault(property => string.Equals(property.Name, commandName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Agent-controllable command '{commandName}' was not found on type '{type.FullName}'.");

    private static MethodInfo FindAnnotatedMethod(Type type, string methodName, IReadOnlyList<JToken> arguments)
    {
        var candidates = GetAnnotatedMethodEntries(type, AgentLanguages.English)
            .Select(static entry => entry.Method)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
            .Where(method => CanBindMethodArguments(method, arguments))
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new KeyNotFoundException($"Agent-controllable method '{methodName}' was not found on type '{type.FullName}'.");
        }

        var exact = candidates.FirstOrDefault(method => CountExplicitBindableParameters(method) == arguments.Count);
        if (exact is not null)
        {
            return exact;
        }

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        throw new InvalidOperationException($"Method '{methodName}' on type '{type.FullName}' is ambiguous for the provided JSON arguments.");
    }

    private static object?[] BindMethodArguments(MethodInfo method, IReadOnlyList<JToken> arguments)
    {
        var parameters = method.GetParameters();
        var bound = new object?[parameters.Length];
        var jsonArgumentIndex = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.ParameterType == typeof(System.Threading.CancellationToken))
            {
                bound[i] = default(System.Threading.CancellationToken);
                continue;
            }

            if (jsonArgumentIndex < arguments.Count)
            {
                bound[i] = DeserializeTokenToType(arguments[jsonArgumentIndex], parameter.ParameterType);
                jsonArgumentIndex++;
                continue;
            }

            if (parameter.IsOptional)
            {
                bound[i] = parameter.DefaultValue;
                continue;
            }

            throw new ArgumentException($"Method '{method.Name}' requires parameter '{parameter.Name}'.", nameof(arguments));
        }

        return bound;
    }

    private static bool CanBindMethodArguments(MethodInfo method, IReadOnlyList<JToken> arguments)
    {
        var parameters = method.GetParameters();
        var requiredCount = parameters.Count(parameter => parameter.ParameterType != typeof(System.Threading.CancellationToken) && !parameter.IsOptional);
        var supportedCount = parameters.Count(parameter => parameter.ParameterType != typeof(System.Threading.CancellationToken));
        return arguments.Count >= requiredCount && arguments.Count <= supportedCount;
    }

    private static int CountExplicitBindableParameters(MethodInfo method)
        => method.GetParameters().Count(parameter => parameter.ParameterType != typeof(System.Threading.CancellationToken));

    private static async Task<object?> AwaitMethodResultAsync(object? invocationResult, Type returnType)
    {
        if (!typeof(Task).IsAssignableFrom(returnType))
        {
            return invocationResult;
        }

        if (invocationResult is not Task task)
        {
            return null;
        }

        await task.ConfigureAwait(false);
        if (!returnType.IsGenericType)
        {
            return null;
        }

        return returnType.GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(task);
    }

    private static JToken SerializeArbitraryValue(object? value)
    {
        if (value is null)
        {
            return JValue.CreateNull();
        }

        return JToken.FromObject(value, ComponentModelEx.CreateJsonSerializer());
    }

    private static PropertyInfo GetAnnotatedReadableProperty(Type type, string propertyName)
        => GetAnnotatedPropertyEntries(type, AgentLanguages.English)
            .Select(static entry => entry.Property)
            .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Agent-controllable property '{propertyName}' was not found on type '{type.FullName}'.");

    private static IEnumerable<AnnotatedPropertyEntry> GetAnnotatedPropertyEntries(Type type, AgentLanguages language)
        => type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.GetIndexParameters().Length == 0 && property.CanRead)
            .Select(property => CreateAnnotatedPropertyEntry(type, property, language))
            .Where(static entry => entry is not null)
            .Select(static entry => entry!);

    private static AnnotatedPropertyEntry? CreateAnnotatedPropertyEntry(Type runtimeType, PropertyInfo property, AgentLanguages language)
    {
        var memberKind = typeof(ICommand).IsAssignableFrom(property.PropertyType)
            ? WorkflowAgentMemberKind.Command
            : WorkflowAgentMemberKind.Property;
        if (!WorkflowAgentMemberWhitelist.IsMemberAllowed(runtimeType, memberKind, property.Name))
        {
            return null;
        }

        var context = TryGetAgentContext(property, language)
            ?? TryGetInheritedPropertyContext(runtimeType, property, language)
            ?? TryGetFieldBackedPropertyContext(runtimeType, property.Name, language);

        return context is null ? null : new AnnotatedPropertyEntry(property, context);
    }

    private static IEnumerable<AnnotatedMethodEntry> GetAnnotatedMethodEntries(Type type, AgentLanguages language)
        => type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static method => !method.IsSpecialName)
            .Select(method => CreateAnnotatedMethodEntry(type, method, language))
            .Where(static entry => entry is not null)
            .Select(static entry => entry!);

    private static AnnotatedMethodEntry? CreateAnnotatedMethodEntry(Type runtimeType, MethodInfo method, AgentLanguages language)
    {
        if (!WorkflowAgentMemberWhitelist.IsMemberAllowed(runtimeType, WorkflowAgentMemberKind.Method, method.Name))
        {
            return null;
        }

        var context = TryGetAgentContext(method, language)
            ?? TryGetInheritedMethodContext(runtimeType, method, language);

        return context is null ? null : new AnnotatedMethodEntry(method, context);
    }

    private static string? TryGetInheritedPropertyContext(Type runtimeType, PropertyInfo runtimeProperty, AgentLanguages language)
    {
        foreach (var interfaceType in runtimeType.GetInterfaces())
        {
            var interfaceProperty = interfaceType.GetProperty(runtimeProperty.Name, BindingFlags.Instance | BindingFlags.Public);
            var context = interfaceProperty is null ? null : TryGetAgentContext(interfaceProperty, language);
            if (!string.IsNullOrWhiteSpace(context))
            {
                return context;
            }
        }

        for (var current = runtimeType.BaseType; current is not null; current = current.BaseType)
        {
            var baseProperty = current.GetProperty(runtimeProperty.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var context = baseProperty is null ? null : TryGetAgentContext(baseProperty, language);
            if (!string.IsNullOrWhiteSpace(context))
            {
                return context;
            }
        }

        return null;
    }

    private static string? TryGetInheritedMethodContext(Type runtimeType, MethodInfo runtimeMethod, AgentLanguages language)
    {
        foreach (var interfaceType in runtimeType.GetInterfaces())
        {
            var interfaceMethod = interfaceType.GetMethods().FirstOrDefault(candidate => MethodsMatch(candidate, runtimeMethod));
            var context = interfaceMethod is null ? null : TryGetAgentContext(interfaceMethod, language);
            if (!string.IsNullOrWhiteSpace(context))
            {
                return context;
            }
        }

        for (var current = runtimeType.BaseType; current is not null; current = current.BaseType)
        {
            var baseMethod = current.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(candidate => MethodsMatch(candidate, runtimeMethod));
            var context = baseMethod is null ? null : TryGetAgentContext(baseMethod, language);
            if (!string.IsNullOrWhiteSpace(context))
            {
                return context;
            }
        }

        return null;
    }

    private static bool MethodsMatch(MethodInfo left, MethodInfo right)
    {
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal))
        {
            return false;
        }

        var leftParameters = left.GetParameters();
        var rightParameters = right.GetParameters();
        if (leftParameters.Length != rightParameters.Length)
        {
            return false;
        }

        for (var i = 0; i < leftParameters.Length; i++)
        {
            if (leftParameters[i].ParameterType != rightParameters[i].ParameterType)
            {
                return false;
            }
        }

        return true;
    }

    private static string? TryGetFieldBackedPropertyContext(Type runtimeType, string propertyName, AgentLanguages language)
    {
        for (var current = runtimeType; current is not null; current = current.BaseType)
        {
            var field = current
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate => string.Equals(NormalizeFieldName(candidate.Name), propertyName, StringComparison.OrdinalIgnoreCase));
            var context = field is null ? null : TryGetAgentContext(field, language);
            if (!string.IsNullOrWhiteSpace(context))
            {
                return context;
            }
        }

        return null;
    }

    private static string NormalizeFieldName(string fieldName)
    {
        var normalized = fieldName;
        if (normalized.StartsWith("m_", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(2);
        }

        normalized = normalized.TrimStart('_');
        return normalized.Length == 0
            ? fieldName
            : char.ToUpperInvariant(normalized[0]) + normalized.Substring(1);
    }

    private static string? TryGetAgentContext(MemberInfo member, AgentLanguages language)
        => member.GetCustomAttributes(typeof(AgentContextAttribute), false)
            .OfType<AgentContextAttribute>()
            .FirstOrDefault(attribute => attribute.Language == language)?.Context
            ?? member.GetCustomAttributes(typeof(AgentContextAttribute), false)
                .OfType<AgentContextAttribute>()
                .FirstOrDefault(attribute => attribute.Language == AgentLanguages.English)?.Context
            ?? member.GetCustomAttributes(typeof(AgentContextAttribute), false)
                .OfType<AgentContextAttribute>()
                .FirstOrDefault()?.Context;

    private static void SetPropertyPathValue(object target, string propertyPath, JToken? value)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            throw new ArgumentException("propertyPath cannot be null or empty.", nameof(propertyPath));
        }

        var segments = propertyPath.Split(['.'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException("propertyPath must contain at least one property segment.", nameof(propertyPath));
        }

        ApplyPropertyPathValue(target, segments, 0, value);
    }

    private static object ApplyPropertyPathValue(object target, string[] segments, int index, JToken? value)
    {
        var property = GetAnnotatedWritableProperty(target.GetType(), segments[index]);
        if (index == segments.Length - 1)
        {
            property.SetValue(target, DeserializeTokenToType(value, property.PropertyType));
            return target;
        }

        var nextValue = property.GetValue(target);
        if (nextValue is null)
        {
            nextValue = CreateDefaultInstance(property.PropertyType);
            property.SetValue(target, nextValue);
        }

        if (nextValue is null)
        {
            throw new InvalidOperationException($"Property '{property.Name}' on type '{target.GetType().FullName}' cannot be traversed because it is null and cannot be instantiated automatically.");
        }

        if (property.PropertyType.IsValueType)
        {
            var boxed = nextValue;
            var updated = ApplyPropertyPathValue(boxed, segments, index + 1, value);
            property.SetValue(target, updated);
            return target;
        }

        ApplyPropertyPathValue(nextValue, segments, index + 1, value);
        return target;
    }

    private static void MergeObjectSnapshot(object target, JToken snapshot)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var deserialized = DeserializeTokenToType(snapshot, target.GetType()) ?? throw new JsonSerializationException($"Failed to materialize a snapshot for type '{target.GetType().FullName}'.");
        foreach (var property in GetAnnotatedWritableProperties(target.GetType(), AgentLanguages.English))
        {
            property.SetValue(target, property.GetValue(deserialized));
        }
    }

    private static IEnumerable<PropertyInfo> GetAnnotatedWritableProperties(Type type, AgentLanguages language)
        => GetAnnotatedPropertyEntries(type, language)
            .Select(static entry => entry.Property)
            .Where(static property => property.CanWrite);

    private static PropertyInfo GetAnnotatedWritableProperty(Type type, string propertyName)
        => GetAnnotatedWritableProperties(type, AgentLanguages.English)
            .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Agent-controllable writable property '{propertyName}' was not found on type '{type.FullName}'.", nameof(propertyName));

    private static object? DeserializeTokenToType(JToken? token, Type targetType)
    {
        if (token is null)
        {
            throw new ArgumentException($"A JSON value is required for type '{targetType.FullName}'.", nameof(token));
        }

        if (token.Type == JTokenType.String
            && targetType != typeof(string)
            && LooksLikeJson(token.Value<string>()))
        {
            var text = token.Value<string>()!;
            var parsed = JToken.Parse(text);
            return parsed.DeserializeToType(targetType);
        }

        return token.DeserializeToType(targetType);
    }

    private static bool LooksLikeJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text!.TrimStart();
        return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
    }

    private static object? CreateDefaultInstance(Type type)
    {
        var targetType = Nullable.GetUnderlyingType(type) ?? type;
        if (targetType == typeof(string))
        {
            return string.Empty;
        }

        if (targetType.IsAbstract || targetType.IsInterface)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(targetType);
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsSlot(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel slot)
        => tree.Nodes.Any(node => node.Slots.Any(candidate => ReferenceEquals(candidate, slot)));

    private static JObject CreateWarning(string code, string message)
        => new()
        {
            ["code"] = code,
            ["message"] = message
        };

    private static JObject CreateErrorEntry(string code, string message, int? operationIndex = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (operationIndex.HasValue)
        {
            error["operationIndex"] = operationIndex.Value;
        }

        return error;
    }

    private static string CreateErrorEnvelope(WorkflowProtocolSession session, string code, string message)
        => new JObject
        {
            ["sessionId"] = session.SessionId,
            ["revision"] = session.Revision,
            ["error"] = CreateErrorEntry(code, message)
        }.ToString(Formatting.Indented);

    private static string CreateStandaloneErrorEnvelope(string code, string message)
        => new JObject
        {
            ["error"] = CreateErrorEntry(code, message)
        }.ToString(Formatting.Indented);

    private static string MapExceptionToErrorCode(Exception exception)
        => exception switch
        {
            JsonSerializationException => WorkflowProtocolErrorCodes.JsonSerializationFailed,
            KeyNotFoundException => WorkflowProtocolErrorCodes.TargetNotFound,
            ArgumentOutOfRangeException => WorkflowProtocolErrorCodes.IndexOutOfRange,
            MissingMethodException => WorkflowProtocolErrorCodes.MethodNotFound,
            TargetInvocationException targetInvocationException when targetInvocationException.InnerException is not null => MapExceptionToErrorCode(targetInvocationException.InnerException),
            InvalidOperationException invalidOperation when ContainsText(invalidOperation.Message, "does not implement ICommand") => WorkflowProtocolErrorCodes.CommandNotFound,
            InvalidOperationException invalidOperation when ContainsText(invalidOperation.Message, "ambiguous") => WorkflowProtocolErrorCodes.MethodAmbiguous,
            InvalidOperationException => WorkflowProtocolErrorCodes.InvalidOperation,
            ArgumentException argumentException when ContainsText(argumentException.Message, "propertyPath") => WorkflowProtocolErrorCodes.PropertyPathInvalid,
            ArgumentException argumentException when ContainsText(argumentException.Message, "commandName") => WorkflowProtocolErrorCodes.CommandNotFound,
            ArgumentException argumentException when ContainsText(argumentException.Message, "methodName") => WorkflowProtocolErrorCodes.MethodNotFound,
            ArgumentException argumentException when ContainsText(argumentException.Message, "target") => WorkflowProtocolErrorCodes.TargetNotFound,
            ArgumentException => WorkflowProtocolErrorCodes.InvalidPatchRequest,
            _ => WorkflowProtocolErrorCodes.UnhandledError
        };

    private static bool ContainsText(string? text, string value)
        => text?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string CreateBootstrapDocument(AgentLanguages language)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Workflow Agent Bootstrap");
        builder.AppendLine();
        builder.AppendLine("Use the compact protocol tools below to minimize token consumption while still taking over workflow operations.");
        builder.AppendLine();
        builder.AppendLine("## Recommended loop");
        builder.AppendLine("1. Read `GetWorkflowBootstrap` once.");
        builder.AppendLine("2. Read only the required semantic section through `GetWorkflowContextSection`.");
        builder.AppendLine("3. Create or refresh a live session through `OpenWorkflowSession`.");
        builder.AppendLine("4. Read a compact graph projection through `QueryWorkflowGraph`.");
        builder.AppendLine("5. Validate edits through `ValidateWorkflowPatch` when safety is more important than latency.");
        builder.AppendLine("6. Edit the graph through `ApplyWorkflowPatch`.");
        builder.AppendLine("7. Invoke annotated commands or methods when graph mutation alone is insufficient.");
        builder.AppendLine("8. Run runtime behaviors through `InvokeWorkflowActionAsync`.");
        builder.AppendLine("9. Synchronize incrementally through `GetWorkflowChanges` and `GetWorkflowDiagnostics`.");
        builder.AppendLine();
        builder.AppendLine("## High-value tools");
        builder.AppendLine("- `GetWorkflowContextSection`");
        builder.AppendLine("- `OpenWorkflowSession`");
        builder.AppendLine("- `QueryWorkflowGraph`");
        builder.AppendLine("- `GetWorkflowTargetCapabilities`");
        builder.AppendLine("- `GetWorkflowPropertyValue`");
        builder.AppendLine("- `ValidateWorkflowPatch`");
        builder.AppendLine("- `ApplyWorkflowPatch`");
        builder.AppendLine("- `InvokeWorkflowActionAsync`");
        builder.AppendLine("- `InvokeWorkflowCommandAsync`");
        builder.AppendLine("- `InvokeWorkflowMethodAsync`");
        builder.AppendLine("- `GetWorkflowChanges`");
        builder.AppendLine("- `GetWorkflowDiagnostics`");
        builder.AppendLine("- `ReleaseWorkflowProtocolSession`");
        builder.AppendLine();
        builder.AppendLine("## Token strategy");
        builder.AppendLine("- Prefer `queryMode: summary` for the first read.");
        builder.AppendLine("- Prefer stable ids (`nodeId`, `slotId`, `linkId`) over index-based addressing.");
        builder.AppendLine("- Prefer `ValidateWorkflowPatch` before `ApplyWorkflowPatch` for multi-step or destructive edits.");
        builder.AppendLine("- Prefer `ApplyWorkflowPatch` over multiple single-action tools.");
        builder.AppendLine("- Prefer `returnMode: delta` unless a full snapshot is strictly necessary.");
        builder.AppendLine("- Prefer `GetWorkflowChanges` over repeatedly fetching the full tree.");
        builder.AppendLine("- Use `includeJson: true` only when the agent truly needs a live object JSON example for property editing.");
        builder.AppendLine();
        builder.AppendLine("## Context entry points");
        builder.AppendLine("- `section: framework`");
        builder.AppendLine("- `section: enums`");
        builder.AppendLine("- `section: valueTypes`");
        builder.AppendLine("- `section: registeredComponents`");
        builder.AppendLine("- `section: type` with `typeName`");
        builder.AppendLine();
        builder.AppendLine("## Generic property editing");
        builder.AppendLine("- Use `setProperty` to update one writable public property by `propertyPath`.");
        builder.AppendLine("- Use `setProperties` to update multiple writable public properties in one patch operation.");
        builder.AppendLine("- Use `replaceObject` to merge a full object snapshot into an existing workflow object.");
        builder.AppendLine();
        builder.AppendLine("## Whitelist strategy");
        builder.AppendLine("- `AgentContext` is the first gate. Unannotated members are never controllable.");
        builder.AppendLine("- Optional host-side whitelists can further restrict which annotated properties, commands, and methods remain callable.");
        builder.AppendLine("- When no whitelist exists for a member kind, all annotated members of that kind remain available.");
        builder.AppendLine();
        builder.AppendLine($"Default language code: `{language.ToLanguageCode()}`");
        builder.AppendLine($"Framework context hash: `{ComputeSha256(WorkflowAgentContextProvider.ProvideWorkflowFrameworkContext(AgentLanguages.English))}`");
        builder.AppendLine($"Document context hash: `{ComputeSha256(WorkflowAgentContextProvider.ProvideWorkflowAgentContextDocument(language))}`");
        return builder.ToString().TrimEnd();
    }

    private static string ComputeSha256(string text)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        var hashBytes = sha256.ComputeHash(bytes);
        var builder = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            builder.Append(b.ToString("x2"));
        }
        return builder.ToString();
    }

    private static JToken ParseJson(string json)
        => JToken.Parse(json);

    private static AgentLanguages ParseLanguage(string? languageCode)
        => string.IsNullOrWhiteSpace(languageCode)
            ? AgentLanguages.English
            : AgentLanguagesExtensions.ParseLanguageCode(languageCode!);

    private static TRequest DeserializeRequest<TRequest>(string requestJson)
        where TRequest : class
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            throw new ArgumentException("Request JSON cannot be null or empty.", nameof(requestJson));
        }

        var request = JsonConvert.DeserializeObject<TRequest>(requestJson);
        return request ?? throw new JsonSerializationException($"Failed to deserialize request JSON to {typeof(TRequest).Name}.");
    }

    private static IWorkflowTreeViewModel DeserializeTree(JToken? token, string propertyName)
    {
        var json = ExtractJson(token, propertyName);
        return json.TryDeserialize<IWorkflowTreeViewModel>(out var tree) && tree is not null
            ? tree
            : throw new JsonSerializationException("Failed to deserialize workflow tree JSON.");
    }

    private static IWorkflowNodeViewModel DeserializeNode(JToken? token, string propertyName)
    {
        var json = ExtractJson(token, propertyName);
        return json.TryDeserialize<IWorkflowNodeViewModel>(out var node) && node is not null
            ? node
            : throw new JsonSerializationException("Failed to deserialize workflow node JSON.");
    }

    private static IWorkflowSlotViewModel DeserializeSlot(JToken? token, string propertyName)
    {
        var json = ExtractJson(token, propertyName);
        return json.TryDeserialize<IWorkflowSlotViewModel>(out var slot) && slot is not null
            ? slot
            : throw new JsonSerializationException("Failed to deserialize workflow slot JSON.");
    }

    private static T DeserializeValue<T>(JToken? token, string propertyName)
    {
        var json = ExtractJson(token, propertyName);
        var value = JsonConvert.DeserializeObject<T>(json);
        return value is not null
            ? value
            : throw new JsonSerializationException($"Failed to deserialize '{propertyName}' to {typeof(T).Name}.");
    }

    private static TEnum DeserializeEnum<TEnum>(JToken? token, string propertyName)
        where TEnum : struct
    {
        if (token is null)
        {
            throw new ArgumentException($"{propertyName} cannot be null.", propertyName);
        }

        if (token.Type == JTokenType.String)
        {
            var text = token.Value<string>();
            if (Enum.TryParse(text, true, out TEnum enumValue))
            {
                return enumValue;
            }
        }

        if (token.Type == JTokenType.Integer)
        {
            var numericValue = token.Value<int>();
            if (Enum.IsDefined(typeof(TEnum), numericValue))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
            }
        }

        throw new ArgumentException($"{propertyName} contains an invalid enum value for {typeof(TEnum).Name}.", propertyName);
    }

    private static object? ConvertParameter(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        return token.ToObject<object>();
    }

    private static string ExtractJson(JToken? token, string propertyName)
    {
        if (token is null)
        {
            throw new ArgumentException($"{propertyName} cannot be null.", propertyName);
        }

        return token.Type == JTokenType.String
            ? token.Value<string>() ?? throw new ArgumentException($"{propertyName} cannot be an empty JSON string.", propertyName)
            : token.ToString(Formatting.None);
    }

    private static string RequireSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId cannot be null or empty.", nameof(sessionId));
        }

        return sessionId!;
    }

    private sealed class WorkflowProtocolSession
    {
        private int _sequence;

        public WorkflowProtocolSession(string sessionId, IWorkflowTreeViewModel tree, AgentLanguages language)
        {
            SessionId = sessionId;
            Tree = tree;
            Language = language;
            IdMap = new Dictionary<object, string>(ReferenceObjectComparer.Instance);
            ChangeLog = [];
            TreeId = EnsureTreeId(tree);
            ChangeLog.Add(new JObject
            {
                ["revision"] = 0,
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["event"] = "sessionOpened"
            });
            SyncIds();
        }

        public string SessionId { get; }
        public IWorkflowTreeViewModel Tree { get; private set; }
        public string TreeId { get; private set; }
        public int Revision { get; private set; }
        public AgentLanguages Language { get; set; }
        public Dictionary<object, string> IdMap { get; }
        public List<JObject> ChangeLog { get; }

        public void RefreshTree(IWorkflowTreeViewModel tree)
        {
            Tree = tree ?? throw new ArgumentNullException(nameof(tree));
            TreeId = EnsureTreeId(tree);
            SyncIds();
        }

        public void IncrementRevision(JObject record)
        {
            SyncIds();
            Revision++;
            record["revision"] = Revision;
            ChangeLog.Add(record);
        }

        public void CopyStateFrom(WorkflowProtocolSession source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Revision = source.Revision;
            TreeId = source.TreeId;
        }

        public void SyncIds()
        {
            EnsureTreeId(Tree);
            foreach (var node in Tree.Nodes)
            {
                EnsureNodeId(node);
                foreach (var slot in node.Slots)
                {
                    EnsureSlotId(slot);
                }
            }

            foreach (var link in Tree.Links)
            {
                EnsureLinkId(link);
            }
        }

        public string EnsureTreeId(IWorkflowTreeViewModel tree)
            => TreeId = EnsureId(tree, "tree");

        public string EnsureNodeId(IWorkflowNodeViewModel node)
            => EnsureId(node, "node");

        public string EnsureSlotId(IWorkflowSlotViewModel slot)
            => EnsureId(slot, "slot");

        public string EnsureLinkId(IWorkflowLinkViewModel link)
            => EnsureId(link, "link");

        private string EnsureId(object instance, string prefix)
        {
            if (IdMap.TryGetValue(instance, out var existing))
            {
                return existing;
            }

            _sequence++;
            var created = $"{prefix}-{_sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            IdMap[instance] = created;
            return created;
        }
    }

    private sealed class ReferenceObjectComparer : IEqualityComparer<object>
    {
        public static ReferenceObjectComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private class WorkflowSessionBoundRequest
    {
        public string? SessionId { get; set; }
        public JToken? Tree { get; set; }
        public string? LanguageCode { get; set; }
    }

    private sealed class WorkflowSessionOpenRequest : WorkflowSessionBoundRequest
    {
    }

    private sealed class WorkflowContextSectionRequest
    {
        public string? Section { get; set; }
        public string? TypeName { get; set; }
        public string? LanguageCode { get; set; }
    }

    private sealed class WorkflowQueryRequest : WorkflowSessionBoundRequest
    {
        public string? QueryMode { get; set; }
        public string? Id { get; set; }
        public bool IncludeSlots { get; set; }
        public bool IncludeLinks { get; set; }
        public bool IncludeConnections { get; set; }
        public bool IncludeContext { get; set; }
        public bool IncludeJson { get; set; }
    }

    private class WorkflowTargetRequest : WorkflowSessionBoundRequest
    {
        public string? TargetId { get; set; }
        public string? NodeId { get; set; }
        public string? SlotId { get; set; }
        public string? LinkId { get; set; }
        public bool TargetTree { get; set; }

        public WorkflowTargetSelector ToTargetSelector()
            => new(TargetId, NodeId, SlotId, LinkId, TargetTree);
    }

    private sealed class WorkflowPropertyReadRequest : WorkflowTargetRequest
    {
        public string? PropertyPath { get; set; }
    }

    private sealed class WorkflowPatchRequest : WorkflowSessionBoundRequest
    {
        public int? ExpectedRevision { get; set; }
        public string? ReturnMode { get; set; }
        public List<JToken>? Operations { get; set; }
    }

    private sealed class WorkflowActionRequest : WorkflowSessionBoundRequest
    {
        public string? Action { get; set; }
        public string? NodeId { get; set; }
        public string? SlotId { get; set; }
        public string? LinkId { get; set; }
        public JToken? Parameter { get; set; }

        public WorkflowTargetSelector ToTargetSelector()
            => new(null, NodeId, SlotId, LinkId, false);
    }

    private sealed class WorkflowCommandInvokeRequest : WorkflowTargetRequest
    {
        public string? CommandName { get; set; }
        public JToken? Parameter { get; set; }
    }

    private sealed class WorkflowMethodInvokeRequest : WorkflowTargetRequest
    {
        public string? MethodName { get; set; }
        public List<JToken>? Arguments { get; set; }
    }

    private sealed class WorkflowChangesRequest
    {
        public string? SessionId { get; set; }
        public int? SinceRevision { get; set; }
    }

    private sealed class PatchTarget(string id, object instance)
    {
        public string Id { get; } = id;
        public object Instance { get; } = instance;
    }

    private sealed class WorkflowTargetSelector(string? targetId, string? nodeId, string? slotId, string? linkId, bool targetTree)
    {
        public string? TargetId { get; } = targetId;
        public string? NodeId { get; } = nodeId;
        public string? SlotId { get; } = slotId;
        public string? LinkId { get; } = linkId;
        public bool TargetTree { get; } = targetTree;
    }

    private sealed class AnnotatedPropertyEntry(PropertyInfo property, string context)
    {
        public PropertyInfo Property { get; } = property;
        public string Context { get; } = context;
    }

    private sealed class AnnotatedMethodEntry(MethodInfo method, string context)
    {
        public MethodInfo Method { get; } = method;
        public string Context { get; } = context;
    }
}
