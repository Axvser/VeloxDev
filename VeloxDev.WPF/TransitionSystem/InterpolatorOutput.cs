using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.TransitionSystem
{
    public class InterpolatorOutput : InterpolatorOutputCore<DispatcherPriority>
    {
        public override void Update(object target, int frameIndex, bool isUIAccess, DispatcherPriority priority)
        {
            if (isUIAccess)
            {
                Update(target, frameIndex);
            }
            else
            {
                Application.Current.Dispatcher.InvokeAsync(() => Update(target, frameIndex), priority);
            }
        }
        private void Update(object target, int frameIndex)
        {
            foreach (var kvp in Frames)
            {
                kvp.Key.SetValue(target, kvp.Value[frameIndex]);
            }
        }
    }
}
