using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class InterpolatorOutputCore<TUIThreadInspectorCore, TPriorityCore> : InterpolatorOutputBase, IFrameSequence<TPriorityCore>
        where TUIThreadInspectorCore : IUIThreadInspectorCore, new()
    {
        private readonly TUIThreadInspectorCore inspector = new();
        public override bool CanSetValue() => inspector.IsAppAlive();
        public override void Update(object target, int frameIndex, bool isUIAccess, object? priority = default)
        {
            if (priority is not TPriorityCore cvt_priority) throw new InvalidDataException($"The value of \"priority\" is not [ {typeof(TPriorityCore).FullName} ] !");
            Update(target, frameIndex, isUIAccess, cvt_priority);
        }
        public virtual void Update(object target, int frameIndex, bool isUIAccess, TPriorityCore priority)
        {
            if (isUIAccess)
            {
                SetValues(target, frameIndex);
                return;
            }
            inspector.ProtectedInvoke(inspector.IsUIThread(), () => { SetValues(target, frameIndex); },priority);
        }
    }

    public abstract class InterpolatorOutputCore<TUIThreadInspectorCore> : InterpolatorOutputBase, IFrameSequence
        where TUIThreadInspectorCore : IUIThreadInspectorCore, new()
    {
        private readonly TUIThreadInspectorCore inspector = new();
        public override bool CanSetValue() => inspector.IsAppAlive();
        public override void Update(object target, int frameIndex, bool isUIAccess, object? priority = default)
        {
            Update(target, frameIndex, isUIAccess);
        }
        public virtual void Update(object target, int frameIndex, bool isUIAccess)
        {
            if (isUIAccess)
            {
                SetValues(target, frameIndex);
                return;
            }
            inspector.ProtectedInvoke(inspector.IsUIThread(), () => { SetValues(target, frameIndex); });
        }
    }

    public abstract class InterpolatorOutputBase : IFrameSequenceCore
    {
        public abstract bool CanSetValue();
        public virtual Dictionary<PropertyInfo, List<object?>> Frames { get; protected set; } = [];
        public virtual int Count { get; protected set; } = 0;
        public abstract void Update(object target, int frameIndex, bool isUIAccess, object? priority = default);
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
        public virtual void SetValues(object target, int frameIndex)
        {
            foreach (var kvp in Frames)
            {
                if (CanSetValue())
                {
                    kvp.Key.SetValue(target, kvp.Value[frameIndex]);
                }
            }
        }
    }
}
