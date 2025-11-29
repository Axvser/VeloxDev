namespace VeloxDev.Core.TimeLine
{
    public class ThreadSafeFrameEventArgs : FrameEventArgs
    {
        private readonly object _lockObject = new();
        private bool _handled;

        public new bool Handled
        {
            get { lock (_lockObject) return _handled; }
            set { lock (_lockObject) _handled = value; }
        }
    }
}
