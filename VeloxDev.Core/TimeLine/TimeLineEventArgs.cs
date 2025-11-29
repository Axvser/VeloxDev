namespace VeloxDev.Core.TimeLine;

public abstract class TimeLineEventArgs
{
    /// <summary>
    /// False : default | True : kill the time line
    /// </summary>
    public virtual bool Handled { get; set; } = false;
}
