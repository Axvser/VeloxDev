namespace VeloxDev.TransitionSystem
{
    /// <summary>
    /// Deliberately without a frame pacer: Blazor has no timer that fires on the renderer's own thread.
    /// </summary>
    /// <remarks>
    /// On WebAssembly the host is single-threaded, so the loop already runs where the callbacks belong and there is
    /// nothing to move. On the server the loop resumes wherever the timer fired, and the property writes are still
    /// safe because <see cref="UIThreadInspector"/> marshals them; posting the frame itself would allocate a
    /// dispatch per frame, which is the one thing the sampling path is built to avoid. So the thread-pool pacer
    /// stays in place, and the effect's callbacks are not guaranteed a particular thread here.
    /// </remarks>
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect>
    {
    }
}
