using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
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
    private JObject? _lastSnapshot;
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
        _version++;
        return snapshot.ToString(Formatting.Indented);
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
            _version++;
            return JsonConvert.SerializeObject(new
            {
                status = "full",
                message = "No previous snapshot; returning full state.",
                version = _version,
                state = current
            }, Formatting.Indented);
        }

        var diff = ComputeDiff(_lastSnapshot, current);
        _lastSnapshot = current;
        _version++;

        return JsonConvert.SerializeObject(new
        {
            status = "diff",
            version = _version,
            changes = diff
        }, Formatting.Indented);
    }

    private JObject BuildSnapshot()
    {
        var nodes = new JArray();
        for (int i = 0; i < _tree.Nodes.Count; i++)
        {
            var node = _tree.Nodes[i];
            var nObj = new JObject
            {
                ["index"] = i,
                ["id"] = GetRuntimeId(node),
                ["type"] = node.GetType().Name,
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
            var slotIds = new JArray();
            foreach (var slot in node.Slots)
                slotIds.Add(GetRuntimeId(slot));
            nObj["slotIds"] = slotIds;

            nodes.Add(nObj);
        }

        var links = new JArray();
        for (int i = 0; i < _tree.Links.Count; i++)
        {
            var link = _tree.Links[i];
            if (!link.IsVisible) continue;
            links.Add(new JObject
            {
                ["id"] = GetRuntimeId(link),
                ["senderId"] = GetRuntimeId(link.Sender),
                ["receiverId"] = GetRuntimeId(link.Receiver),
            });
        }

        return new JObject
        {
            ["nodeCount"] = _tree.Nodes.Count,
            ["linkCount"] = _tree.Links.Count,
            ["nodes"] = nodes,
            ["links"] = links,
        };
    }

    private static JObject ComputeDiff(JObject previous, JObject current)
    {
        var diff = new JObject();

        // Nodes diff by RuntimeId
        var prevNodes = IndexById(previous["nodes"] as JArray);
        var currNodes = IndexById(current["nodes"] as JArray);

        var addedNodes = new JArray();
        var removedNodes = new JArray();
        var modifiedNodes = new JArray();

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
                removedNodes.Add(new JObject { ["id"] = kvp.Key });
        }

        if (addedNodes.Count > 0) diff["addedNodes"] = addedNodes;
        if (removedNodes.Count > 0) diff["removedNodes"] = removedNodes;
        if (modifiedNodes.Count > 0) diff["modifiedNodes"] = modifiedNodes;

        // Links diff by RuntimeId
        var prevLinks = IndexById(previous["links"] as JArray);
        var currLinks = IndexById(current["links"] as JArray);

        var addedLinks = new JArray();
        var removedLinks = new JArray();

        foreach (var kvp in currLinks)
        {
            if (!prevLinks.ContainsKey(kvp.Key))
                addedLinks.Add(kvp.Value);
        }
        foreach (var kvp in prevLinks)
        {
            if (!currLinks.ContainsKey(kvp.Key))
                removedLinks.Add(new JObject { ["id"] = kvp.Key });
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

    private static Dictionary<string, JObject> IndexById(JArray? arr)
    {
        var dict = new Dictionary<string, JObject>();
        if (arr == null) return dict;
        foreach (var item in arr)
        {
            if (item is JObject obj && obj["id"] != null)
                dict[obj["id"]!.ToString()] = obj;
        }
        return dict;
    }

    private static JObject DiffProperties(JObject prev, JObject curr)
    {
        var diff = new JObject();
        foreach (var kvp in curr)
        {
            if (kvp.Key == "id") continue;
            var prevVal = prev[kvp.Key];
            if (prevVal == null || !JToken.DeepEquals(prevVal, kvp.Value))
            {
                diff[kvp.Key] = new JObject
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
        if (component is IWorkflowIdentifiable identifiable)
            return identifiable.RuntimeId;
        return component.GetHashCode().ToString("x8");
    }

    private static void AppendScalarProps(JObject obj, object target)
    {
        foreach (var prop in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            var pt = prop.PropertyType;
            if (pt == typeof(string) || pt == typeof(int) || pt == typeof(double) || pt == typeof(bool) ||
                pt == typeof(long) || pt == typeof(float) || pt == typeof(decimal))
            {
                try
                {
                    var val = prop.GetValue(target);
                    obj[prop.Name] = val != null ? JToken.FromObject(val) : JValue.CreateNull();
                }
                catch { }
            }
        }
    }
}
