namespace VeloxDev.TimeLine;

/// <summary>The base event args a timeline reports: whether the line should be killed, and the clock readings
/// every subscriber shares.</summary>
public abstract class TimeLineEventArgs
{
    /// <summary>
    /// False : default | True : kill the time line
    /// </summary>
    public virtual bool Handled { get; set; } = false;

    /// <summary>Time since the previous frame.</summary>
    public TimeSpan DeltaTime { get; internal set; } = TimeSpan.Zero;

    /// <summary>Time since this line's clock started.</summary>
    public TimeSpan TotalTime { get; internal set; } = TimeSpan.Zero;
}
