namespace VeloxDev.TransitionSystem
{
    /// <summary>
    /// Deliberately without a frame pacer: Blazor has no timer that fires on the renderer's own thread.
    /// </summary>
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect>
    {
    }
}
