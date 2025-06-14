using Avalonia.Threading;
using System.Collections.Generic;
using System.Reflection;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.TransitionSystem
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
                Dispatcher.UIThread.InvokeAsync(() => Update(target, frameIndex), priority);
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
