using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Assigns stable, process-lifetime unique ids to workflow components by reference, so Blazor
/// elements can carry a <c>data-*-id</c> attribute that round-trips through JavaScript and
/// resolves back to the same object. The ids are stable across re-renders because the key is the
/// component instance itself (reference equality, held weakly). Public so demo/template link views
/// can stamp their endpoint slots with the same ids the adapter behaviors use for node/slot DOM
/// elements — the zoom JS then re-syncs link polylines from those live slots in the collapse frame.
/// </summary>
public static class WorkflowRuntimeIds
{
    private static readonly ConditionalWeakTable<object, string> Ids = new();

    /// <summary>
    /// Gets the stable id for the specified workflow component, assigning one on first use.
    /// </summary>
    public static string Get(object component)
        => Ids.GetValue(component, static _ => Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Attempts to resolve a component back from a previously assigned id.
    /// </summary>
    public static bool TryFind<T>(string? id, out T? value)
        where T : class
    {
        if (string.IsNullOrEmpty(id))
        {
            value = null;
            return false;
        }

        foreach (var pair in Ids)
        {
            if (pair.Value == id && pair.Key is T typed)
            {
                value = typed;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Enumerates all currently registered ids (used by diagnostics/tests).
    /// </summary>
    public static IEnumerable<KeyValuePair<object, string>> Enumerate()
        => Ids;
}
