namespace VeloxDev.TransitionSystem
{
    public interface IFrameSequence<TPriorityCore> : IFrameSequenceCore
    {
        public void Update(object target, int frameIndex, TPriorityCore priority);
    }

    public interface IFrameSequence : IFrameSequenceCore
    {
        public void Update(object target, int frameIndex);
    }

    public interface IFrameSequenceCore
    {
        public int Count { get; }
        public void SetValues(object target, int frameIndex);
        public void Update(object target, int frameIndex, object? priority = default);
        public void AddPropertyInterpolations(ITransitionProperty property, List<object?> objects);
        public void SetCount(int count);
    }
}
