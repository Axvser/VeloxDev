using System.Reflection;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameSequence<TPriority>
    {
        public int Count { get; }
        public List<Dictionary<PropertyInfo, List<object?>>> Frames { get; }
        public void Update(object target, int frameIndex, bool isUIAccess, TPriority priority);
    }
}
