using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Debug-only contract guards for workflow component attachment.
/// In DEBUG builds a violated contract throws InvalidOperationException so the
/// failure surfaces immediately; in Release the call site is removed by
/// [Conditional("DEBUG")], preserving the existing silent behavior.
/// </summary>
internal static class WorkflowGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when called from a DEBUG build.
    /// The entire call site (including the message argument) is compiled out in
    /// Release builds, so the guarded path keeps its existing silent no-op behavior.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Fail(string message, [CallerMemberName] string? method = null)
        => throw new InvalidOperationException($"{method}: {message}");
}
