namespace VeloxDev.WorkflowSystem;

/// <summary>What happened to a context menu, in the vocabulary every GUI adapter shares.</summary>
public enum ContextMenuPhase
{
    /// <summary>The menu is now on screen.</summary>
    Opened = 0,

    /// <summary>The menu is gone.</summary>
    Closed = 1,
}
