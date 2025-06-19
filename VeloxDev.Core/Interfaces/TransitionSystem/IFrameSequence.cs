using System.Reflection;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameSequence<TPriorityCore> : IFrameSequenceCore
    {
        public void Update(object target, int frameIndex, bool isUIAccess, TPriorityCore priority);
    }

    public interface IFrameSequence : IFrameSequenceCore
    {
        public void Update(object target, int frameIndex, bool isUIAccess);
    }

    public interface IFrameSequenceCore
    {
        public int Count { get; }
        public void Update(object target, int frameIndex);
        public void AddPropertyInterpolations(PropertyInfo propertyInfo, List<object?> objects);
        public void SetCount(int count);
    }
}
