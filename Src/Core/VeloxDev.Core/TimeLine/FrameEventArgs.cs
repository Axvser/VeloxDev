namespace VeloxDev.Core.TimeLine;

public class FrameEventArgs : TimeLineEventArgs
{
    /// <summary>
    /// the delta time from last frame in milliseconds
    /// </summary>
    public int DeltaTime { get; internal set; } = 0;

    /// <summary>
    /// the total time since the TimeLine started in milliseconds
    /// </summary>
    public int TotalTime { get; internal set; } = 0;

    /// <summary>
    /// the current frames per second
    /// </summary>
    public int CurrentFPS { get; internal set; } = 0;

    /// <summary>
    /// the target frames per second
    /// </summary>
    public int TargetFPS { get; internal set; } = 0;
}