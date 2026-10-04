namespace VeloxDev.TimeLine;

/// <summary>The base event args a timeline reports, carrying the handled flag.</summary>
public abstract class TimeLineEventArgs
{
    /// <summary>
    /// False : default | True : kill the time line
    /// </summary>
    public virtual bool Handled { get; set; } = false;
}
