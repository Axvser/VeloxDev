namespace VeloxDev.TimeLine
{
    /// <summary>Frame event args whose <see cref="Handled"/> flag is safe to touch from any thread.</summary>
    public class ThreadSafeFrameEventArgs : FrameEventArgs
    {
        private readonly object _lockObject = new();
        private bool _handled;

        /// <summary>Whether a handler has taken the frame; synchronized across threads.</summary>
        public new bool Handled
        {
            get { lock (_lockObject) return _handled; }
            set { lock (_lockObject) _handled = value; }
        }
    }
}
