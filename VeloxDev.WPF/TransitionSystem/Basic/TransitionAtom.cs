namespace VeloxDev.WPF.TransitionSystem.Basic
{
    internal class TransitionAtom
    {
        public event Action? Updated;
        public int Duration { get; set; } = 0;
        public void Invoke()
        {
            Updated?.Invoke();
        }
    }
}
