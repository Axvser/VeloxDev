namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The keys the link interaction layer acts on. Only these cross the adapter boundary as a
/// <see cref="KeyEvent"/>; a host that needs the rest keeps handling them itself.
/// </summary>
/// <remarks>
/// Named <c>InputKey</c> rather than plain <c>Key</c> because <c>Avalonia.Input.Key</c> already exists and every
/// adapter file imports both namespaces — the plain name would make each use site ambiguous.
/// </remarks>
public enum InputKey
{
    /// <summary>Delete the hovered link.</summary>
    Delete = 0,

    /// <summary>Cancel the current interaction.</summary>
    Escape = 1,

    /// <summary>Confirm the current interaction.</summary>
    Enter = 2,
}
