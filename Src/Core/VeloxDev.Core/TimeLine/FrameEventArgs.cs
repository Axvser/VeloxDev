namespace VeloxDev.TimeLine;

/// <summary>The event args a tick pump hands to a tickable's hooks, carrying the two clock readings and the
/// frame-rate pair. One instance is reused for every hook of a frame, so a hook must not hold on to it.</summary>
public class FrameEventArgs : TimeLineEventArgs
{
    /// <summary>Frames the pump actually produced per second.</summary>
    public int CurrentFPS { get; internal set; } = 0;

    /// <summary>Frames per second the channel was asked for.</summary>
    public int TargetFPS { get; internal set; } = 0;
}
