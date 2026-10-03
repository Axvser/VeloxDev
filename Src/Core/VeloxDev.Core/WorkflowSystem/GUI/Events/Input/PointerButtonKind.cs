namespace VeloxDev.WorkflowSystem;

/// <summary>Which pointer button an event refers to.</summary>
/// <remarks>
/// Named <c>…Kind</c> rather than plain <c>PointerButton</c> because <c>Avalonia.Input.PointerButton</c> already
/// exists and every adapter file imports both namespaces — the plain name would make each use site ambiguous.
/// </remarks>
public enum PointerButtonKind
{
    /// <summary>No button — a move, enter or exit.</summary>
    None = 0,

    /// <summary>The primary button.</summary>
    Left = 1,

    /// <summary>The secondary button (opens the link menu).</summary>
    Right = 2,

    /// <summary>The middle button.</summary>
    Middle = 3,
}
