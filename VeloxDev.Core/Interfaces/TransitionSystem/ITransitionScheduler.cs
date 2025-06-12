namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionScheduler<IOutput, TPriority> where IOutput : IFrameSequence<TPriority>
    {
        public void Execute(IFrameInterpolator<IOutput, TPriority> interpolator, ITransitionEffect<TPriority> effect);
        public void Exit();
    }
}
