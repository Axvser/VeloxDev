namespace VeloxDev.TimeLine;

public sealed class TransitionEventArgs : TimeLineEventArgs
{
    /// <summary>Which callback or stage reported this — "Update", "Finally", "Sampling".</summary>
    public string? Stage { get; init; }

    public string? Message { get; init; }

    public Exception? Exception { get; init; }
}
