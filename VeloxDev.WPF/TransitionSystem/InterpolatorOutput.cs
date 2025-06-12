using System.Reflection;
using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.TransitionSystem
{
    public class InterpolatorOutput : InterpolatorOutputBase<DispatcherPriority>
    {
        public override void Update(object target, int frameIndex, bool isUIAccess, DispatcherPriority priority)
        {
            if (isUIAccess)
            {
                Update(target, frameIndex, priority);
            }
            else
            {
                Dispatcher.CurrentDispatcher.InvokeAsync(() => Update(target, frameIndex, priority), priority);
            }
        }
        internal virtual void AddFrameFrameSequence(PropertyInfo propertyInfo, ICollection<object?> objects)
        {

        }
        internal virtual void SetCount(int count)
        {
            Count = count;
        }
        private void Update(object target, int frameIndex, DispatcherPriority priority)
        {
            foreach (var kvp in Frames)
            {
                kvp.Key.SetValue(target, kvp.Value[frameIndex]);
            }
        }
    }
}
