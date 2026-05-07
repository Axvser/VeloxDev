using System;

namespace VeloxDev.TimeLine;

public class FrameEventArgs : TimeLineEventArgs
{
    /// <summary>
    /// the delta time from last frame
    /// </summary>
    public TimeSpan DeltaTime { get; internal set; } = TimeSpan.Zero;

    /// <summary>
    /// the total time since the TimeLine started
    /// </summary>
    public TimeSpan TotalTime { get; internal set; } = TimeSpan.Zero;

    /// <summary>
    /// the current frames per second
    /// </summary>
    public int CurrentFPS { get; internal set; } = 0;

    /// <summary>
    /// the target frames per second
    /// </summary>
    public int TargetFPS { get; internal set; } = 0;
}