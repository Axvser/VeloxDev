namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionBoard : IDisposable
    {
        public void Execute();
        public void Pause();
        public void Resume();
        public void Exit();
    }
}
