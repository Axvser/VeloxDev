using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class InterpolatorOutputCore<TPriorityCore> : IFrameSequence<TPriorityCore>
    {
        public virtual Dictionary<PropertyInfo, List<object?>> Frames { get; protected set; } = [];
        public virtual int Count { get; protected set; } = 0;
        public virtual void Update(object target, int frameIndex)
        {
            foreach (var kvp in Frames)
            {
                kvp.Key.SetValue(target, kvp.Value[frameIndex]);
            }
        }
        public abstract void Update(object target, int frameIndex, bool isUIAccess, TPriorityCore priority);
        public virtual void AddPropertyInterpolations(PropertyInfo propertyInfo, List<object?> objects)
        {
            if (Frames.TryGetValue(propertyInfo, out _))
            {
                Frames[propertyInfo] = objects;
            }
            else
            {
                Frames.Add(propertyInfo, objects);
            }
        }
        public virtual void SetCount(int count)
        {
            Count = count;
        }
    }

    public abstract class InterpolatorOutputCore : IFrameSequence
    {
        public virtual Dictionary<PropertyInfo, List<object?>> Frames { get; protected set; } = [];
        public virtual int Count { get; protected set; } = 0;
        public virtual void Update(object target, int frameIndex)
        {
            foreach (var kvp in Frames)
            {
                kvp.Key.SetValue(target, kvp.Value[frameIndex]);
            }
        }
        public abstract void Update(object target, int frameIndex, bool isUIAccess);
        public virtual void AddPropertyInterpolations(PropertyInfo propertyInfo, List<object?> objects)
        {
            if (Frames.TryGetValue(propertyInfo, out _))
            {
                Frames[propertyInfo] = objects;
            }
            else
            {
                Frames.Add(propertyInfo, objects);
            }
        }
        public virtual void SetCount(int count)
        {
            Count = count;
        }
    }
}
