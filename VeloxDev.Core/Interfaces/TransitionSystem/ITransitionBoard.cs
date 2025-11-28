namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionBoard : ITransitionBoardCore
    {
        public void Add(object target, IFrameState state, ITransitionEffectCore effect);
    }

    public interface ITransitionBoard<TPriority> : ITransitionBoardCore
    {
        public void Add(object target, IFrameState state, ITransitionEffect<TPriority> effect);
    }

    public interface ITransitionBoardCore
    {
        public bool Remove(object target);
        public void Execute();
        public void Exit();
    }
}
