using System.Reflection;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameSequence<TPriority> : IFrameSequence
    {
        public void Update(object target, int frameIndex, bool isUIAccess, TPriority priority);
    }

    public interface IFrameSequence
    {
        public int Count { get; }
        public Dictionary<PropertyInfo, List<object?>> Frames { get; }
        public void AddPropertyInterpolations(PropertyInfo propertyInfo, List<object?> objects);
        public void SetCount(int count);
    }
}
