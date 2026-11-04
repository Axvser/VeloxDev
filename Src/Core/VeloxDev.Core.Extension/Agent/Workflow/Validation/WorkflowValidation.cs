using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

/// <summary>
/// A domain validator a host or plugin can register so <c>ValidateWorkflow</c> reports problems the
/// structural lint cannot see.
/// </summary>
/// <remarks>
/// <para>
/// The toolkit owns the structural checks (zero-size nodes, duplicate links, cycles). A domain such as a
/// component library knows its own required fields, so it implements this interface and registers one
/// instance through <see cref="WorkflowValidation.Register"/> from its module initializer. The registry is
/// process-wide and is read once per <c>ValidateWorkflow</c> call.
/// </para>
/// <para>
/// Implementations must be read-only: the validator receives the live tree and must not mutate it.
/// </para>
/// </remarks>
public interface IWorkflowTreeValidator
{
    /// <summary>The name reported beside every issue this validator produces.</summary>
    string Name { get; }

    /// <summary>
    /// Returns the issues found in <paramref name="tree"/>. An empty sequence means the domain checks passed.
    /// </summary>
    /// <param name="tree">The live tree; read only.</param>
    IEnumerable<WorkflowValidationIssue> Validate(IWorkflowTreeViewModel tree);
}

/// <summary>One domain validation finding, reported beside the toolkit's structural records.</summary>
/// <param name="Level">Severity: <c>error</c>, <c>warn</c> or <c>info</c>.</param>
/// <param name="NodeIndex">The node the issue belongs to, or <c>-1</c> for a tree-level issue.</param>
/// <param name="ComponentId">The runtime ID of the component, when the issue belongs to one.</param>
/// <param name="Message">A human-readable description of the problem.</param>
/// <param name="Code">An optional stable code the host can match on.</param>
public sealed record WorkflowValidationIssue(
    string Level,
    int NodeIndex,
    string? ComponentId,
    string Message,
    string? Code = null);

/// <summary>
/// Process-wide registry of <see cref="IWorkflowTreeValidator"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// The registry is intentionally small: the toolkit reads a snapshot at the start of each validation, so a
/// validator registered after the server starts is picked up by the next call. Registration is expected from
/// an assembly's module initializer — loading the assembly is what makes its validation available.
/// </para>
/// <para>
/// <see cref="Clear"/> exists for hosts and tests that need a clean slate between sessions.
/// </para>
/// </remarks>
public static class WorkflowValidation
{
    private static readonly object Gate = new();
    private static readonly List<IWorkflowTreeValidator> Registered = [];

    /// <summary>Registers <paramref name="validator"/>; registering the same instance twice is a no-op.</summary>
    /// <param name="validator">The validator to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="validator"/> is <see langword="null"/>.</exception>
    public static void Register(IWorkflowTreeValidator validator)
    {
        if (validator is null) throw new ArgumentNullException(nameof(validator));
        lock (Gate)
        {
            if (!Registered.Contains(validator)) Registered.Add(validator);
        }
    }

    /// <summary>Removes <paramref name="validator"/> if it is registered.</summary>
    /// <param name="validator">The validator to remove.</param>
    /// <returns><see langword="true"/> when the instance was registered and has been removed.</returns>
    public static bool Unregister(IWorkflowTreeValidator validator)
    {
        if (validator is null) throw new ArgumentNullException(nameof(validator));
        lock (Gate) return Registered.Remove(validator);
    }

    /// <summary>Returns the registered validators as a stable snapshot.</summary>
    public static IReadOnlyList<IWorkflowTreeValidator> Snapshot()
    {
        lock (Gate) return [.. Registered];
    }

    /// <summary>Removes every registered validator. Intended for hosts and tests between sessions.</summary>
    public static void Clear()
    {
        lock (Gate) Registered.Clear();
    }
}
