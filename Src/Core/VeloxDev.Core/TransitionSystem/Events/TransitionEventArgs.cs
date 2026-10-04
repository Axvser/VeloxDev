using VeloxDev.TimeLine;

namespace VeloxDev.TransitionSystem;

/// <summary>The event args a transition reports to its effect's callbacks, for the events that carry no payload —
/// the clock readings from <see cref="TimeLineEventArgs"/> plus where the run currently is.</summary>
public class TransitionEventArgs : TimeLineEventArgs
{
    /// <summary>How many times the running segment has played, <c>0</c> on its first pass. Compare against
    /// <see cref="ITransitionEffectCore.LoopTime"/> to tell "which repeat" apart.</summary>
    public int Loop { get; internal set; }

    /// <summary>How many segments this target has played in total, across every chain it has run.</summary>
    public long Cycle { get; internal set; }
}
