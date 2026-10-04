namespace VeloxDev.TimeLine;

/// <summary>The event arguments a transition reports to its effect's callbacks.</summary>
public sealed class TransitionEventArgs : TimeLineEventArgs
{
    /// <summary>Which callback or stage reported this — "Update", "Finally", "Sampling".</summary>
    public string? Stage { get; init; }

    /// <summary>A human-readable message describing the event.</summary>
    public string? Message { get; init; }

    /// <summary>The exception that caused the event, when there was one.</summary>
    public Exception? Exception { get; init; }
}
