namespace VeloxDev.Core.TimeLine;

public sealed class FrameEventArgs : TimeLineEventArgs
{
    /// <summary>
    /// the delta time from last frame in milliseconds
    /// </summary>
    public int DeltaTime { get; internal set; } = 0;
}