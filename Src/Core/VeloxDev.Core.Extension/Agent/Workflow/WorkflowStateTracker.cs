using System;
using System.Collections.Generic;
using System.Threading;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

/// <summary>
/// Captures lightweight snapshots of workflow tree state and computes
/// diffs between snapshots, enabling the Agent to track changes with
/// minimal context instead of re-reading the full state each turn.
/// </summary>
public sealed class WorkflowStateTracker(IWorkflowTreeViewModel tree)
{
    private readonly IWorkflowTreeViewModel _tree = tree ?? throw new ArgumentNullException(nameof(tree));
    private VeloxJsonObject? _lastSnapshot;
    private long _version;

    /// <summary>
    /// Current snapshot version number (monotonically increasing).
    /// </summary>
    public long Version => _version;

    /// <summary>
    /// Takes a snapshot of the current tree state and stores it internally.
    /// Returns the snapshot as a JSON string.
    /// </summary>
    public string TakeSnapshot()
    {
        var snapshot = BuildSnapshot();
        _lastSnapshot = snapshot;
        Interlocked.Increment(ref _version);
        return snapshot.ToJson(VeloxJsonFormat.Indented);
    }

    /// <summary>
    /// Computes the diff between the last snapshot and the current state.
    /// Returns a JSON object describing added/removed/modified nodes and links.
    /// If no previous snapshot exists, returns the full current state.
    /// </summary>
    public string GetChangesSinceLastSnapshot()
    {
        var current = BuildSnapshot();

        if (_lastSnapshot == null)
        {
            _lastSnapshot = current;
            Interlocked.Increment(ref _version);
            return new VeloxJsonObject
            {
                ["status"] = "full",
                ["message"] = "No previous snapshot; returning full state.",
                ["version"] = _version,
                ["state"] = current,
            }.ToJson(VeloxJsonFormat.Indented);
        }

        var diff = ComputeDiff(_lastSnapshot, current);
        _lastSnapshot = current;
        Interlocked.Increment(ref _version);

        return new VeloxJsonObject
        {
            ["status"] = "diff",
            ["version"] = _version,
            ["changes"] = diff,
        }.ToJson(VeloxJsonFormat.Indented);
    }

    private VeloxJsonObject BuildSnapshot()
    {
        var nodes = new VeloxJsonArray();
        for (int i = 0; i < _tree.Nodes.Count; i++)
        {
            var node = _tree.Nodes[i];
            var nObj = new VeloxJsonObject
            {
                ["index"] = i,
                ["id"] = GetRuntimeId(node),
                ["type"] = AgentTypeNames.SimpleOf(node),
                ["left"] = node.Anchor.Horizontal,
                ["top"] = node.Anchor.Vertical,
                ["layer"] = node.Anchor.Layer,
                ["width"] = node.Size.Width,
                ["height"] = node.Size.Height,
                ["slotCount"] = node.Slots.Count,
            };

            // Capture scalar properties
            AppendScalarProps(nObj, node);

            // Capture slot IDs
            var slotIds = new VeloxJsonArray();
            foreach (var slot in node.Slots)
                slotIds.Add(GetRuntimeId(slot));
            nObj["slotIds"] = slotIds;

            nodes.Add(nObj);
        }

        var links = new VeloxJsonArray();
        for (int i = 0; i < _tree.Links.Count; i++)
        {
            var link = _tree.Links[i];
            if (!link.IsVisible) continue;
            links.Add(new VeloxJsonObject
            {
                ["id"] = GetRuntimeId(link),
                ["senderId"] = GetRuntimeId(link.Sender),
                ["receiverId"] = GetRuntimeId(link.Receiver),
            });
        }

        return new VeloxJsonObject
        {
            ["nodeCount"] = _tree.Nodes.Count,
            ["linkCount"] = _tree.Links.Count,
            ["nodes"] = nodes,
            ["links"] = links,
        };
    }

    private static VeloxJsonObject ComputeDiff(VeloxJsonObject previous, VeloxJsonObject current)
    {
        var diff = new VeloxJsonObject();

        // Nodes diff by RuntimeId
        var prevNodes = IndexById(previous["nodes"] as VeloxJsonArray);
        var currNodes = IndexById(current["nodes"] as VeloxJsonArray);

        var addedNodes = new VeloxJsonArray();
        var removedNodes = new VeloxJsonArray();
        var modifiedNodes = new VeloxJsonArray();

        foreach (var kvp in currNodes)
        {
            if (!prevNodes.ContainsKey(kvp.Key))
            {
                addedNodes.Add(kvp.Value);
            }
            else
            {
                var propDiff = DiffProperties(prevNodes[kvp.Key], kvp.Value);
                if (propDiff.Count > 0)
                {
                    propDiff["id"] = kvp.Key;
                    modifiedNodes.Add(propDiff);
                }
            }
        }
        foreach (var kvp in prevNodes)
        {
            if (!currNodes.ContainsKey(kvp.Key))
                removedNodes.Add(new VeloxJsonObject { ["id"] = kvp.Key });
        }

        if (addedNodes.Count > 0) diff["addedNodes"] = addedNodes;
        if (removedNodes.Count > 0) diff["removedNodes"] = removedNodes;
        if (modifiedNodes.Count > 0) diff["modifiedNodes"] = modifiedNodes;

        // Links diff by RuntimeId
        var prevLinks = IndexById(previous["links"] as VeloxJsonArray);
        var currLinks = IndexById(current["links"] as VeloxJsonArray);

        var addedLinks = new VeloxJsonArray();
        var removedLinks = new VeloxJsonArray();

        foreach (var kvp in currLinks)
        {
            if (!prevLinks.ContainsKey(kvp.Key))
                addedLinks.Add(kvp.Value);
        }
        foreach (var kvp in prevLinks)
        {
            if (!currLinks.ContainsKey(kvp.Key))
                removedLinks.Add(new VeloxJsonObject { ["id"] = kvp.Key });
        }

        if (addedLinks.Count > 0) diff["addedLinks"] = addedLinks;
        if (removedLinks.Count > 0) diff["removedLinks"] = removedLinks;

        // Summary counts
        diff["previousNodeCount"] = previous["nodeCount"];
        diff["currentNodeCount"] = current["nodeCount"];
        diff["previousLinkCount"] = previous["linkCount"];
        diff["currentLinkCount"] = current["linkCount"];

        return diff;
    }

    private static Dictionary<string, VeloxJsonObject> IndexById(VeloxJsonArray? arr)
    {
        var dict = new Dictionary<string, VeloxJsonObject>();
        if (arr == null) return dict;
        foreach (var item in arr)
        {
            if (item is VeloxJsonObject obj && obj["id"] is VeloxJsonScalar id && id.AsString() is { } key)
                dict[key] = obj;
        }
        return dict;
    }

    private static VeloxJsonObject DiffProperties(VeloxJsonObject prev, VeloxJsonObject curr)
    {
        var diff = new VeloxJsonObject();
        foreach (var kvp in curr)
        {
            if (kvp.Key == "id") continue;
            var prevVal = prev[kvp.Key];
            if (prevVal == null || !prevVal.DeepEquals(kvp.Value))
            {
                diff[kvp.Key] = new VeloxJsonObject
                {
                    ["from"] = prevVal,
                    ["to"] = kvp.Value,
                };
            }
        }
        return diff;
    }

    private static string GetRuntimeId(object component)
    {
        // Convention: every workflow component's Helper provides a stable RuntimeId (the source
        // generator implements IWorkflowIdentifiable on all workflow components). Falling back to
        // GetHashCode would yield a value that is neither stable across runs nor meaningful — so a
        // missing RuntimeId is an error, not something to paper over.
        if (component is IWorkflowIdentifiable identifiable)
            return identifiable.RuntimeId;
        throw new InvalidOperationException(
            $"'{AgentTypeNames.SimpleOf(component)}' does not implement IWorkflowIdentifiable — a stable RuntimeId (provided by the component Helper) is required.");
    }

    /// <summary>
    /// Captures the scalar and enum properties the context tree records for a component.
    /// </summary>
    /// <remarks>
    /// The member list, their read/write status and their declared types all come from the tree; only the values
    /// are read from the live object, through its generated accessor.
    /// </remarks>
    private static void AppendScalarProps(VeloxJsonObject obj, object target)
    {
        var accessor = AIContextTreeRegistry.FindAccessor(target);
        if (accessor is null) return;

        foreach (var member in AIContextDirectory.Shared.MembersAcross(accessor.TypeName, "Properties"))
        {
            if (!member.Has(AIContextFlags.CanRead)) continue;
            if (accessor.MemberType(member.Name) is not { } memberType) continue;

            if (memberType == typeof(string) || memberType == typeof(int) || memberType == typeof(double) ||
                memberType == typeof(bool) || memberType == typeof(long) || memberType == typeof(float) ||
                memberType == typeof(decimal))
            {
                if (accessor.TryGet(target, member.Name, out var val))
                    obj[member.Name] = val != null ? VeloxJsonValue.From(val) : VeloxJsonValue.Null;
            }
            else if (memberType.IsEnum)
            {
                // Enum-typed properties (e.g. selector/routing state) are captured as their
                // name string so diff output stays human-readable and detects changes.
                if (accessor.TryGet(target, member.Name, out var val))
                    obj[member.Name] = val != null ? VeloxJsonValue.From(val.ToString()) : VeloxJsonValue.Null;
            }
        }
    }
}
