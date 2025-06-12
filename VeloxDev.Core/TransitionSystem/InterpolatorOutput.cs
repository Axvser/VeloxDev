using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class InterpolatorOutputBase<TPriority> : IFrameSequence<TPriority>
    {
        public List<Dictionary<PropertyInfo, List<object?>>> Frames { get; protected set; } = [];

        public int Count => Frames.Count;

        public abstract void Update(object target, int frameIndex, bool isUIAccess, TPriority priority);
    }
}
