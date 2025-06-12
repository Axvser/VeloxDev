namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameState<TOutput, TPriority> where TOutput : IFrameSequence<TPriority>
    {
        public void SetInterpolator(string propertyName, IFrameInterpolator<TOutput, TPriority> interpolator);
        public bool TryGetInterpolator(string propertyName, out IFrameInterpolator<TOutput, TPriority>? interpolator);
        public void SetValue(string propertyName, object? value);
        public bool TryGetValue(string propertyName, out object? value);
    }
}
