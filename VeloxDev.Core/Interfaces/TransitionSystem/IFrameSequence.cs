using System.Reflection;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameSequence<TPriorityCore> : IFrameSequenceCore
    {
        public int Count { get; }
        public void Update(object target, int frameIndex, bool isUIAccess, TPriorityCore priority);
        public void AddPropertyInterpolations(PropertyInfo propertyInfo, List<object?> objects);
        public void SetCount(int count);
    }

    public interface IFrameSequence : IFrameSequenceCore
    {
        public int Count { get; }
        public void Update(object target, int frameIndex, bool isUIAccess);
        public void AddPropertyInterpolations(PropertyInfo propertyInfo, List<object?> objects);
        public void SetCount(int count);
    }

    public interface IFrameSequenceCore
    {

    }
}
