namespace VeloxDev.WorkflowSystem;

/// <summary>What a pointer just did, in the vocabulary every GUI adapter shares.</summary>
public enum PointerPhase
{
    /// <summary>The pointer arrived over the surface.</summary>
    Entered = 0,

    /// <summary>The pointer moved while over the surface.</summary>
    Moved = 1,

    /// <summary>The pointer left the surface.</summary>
    Exited = 2,

    /// <summary>A button went down.</summary>
    Pressed = 3,

    /// <summary>A button came up.</summary>
    Released = 4,
}
