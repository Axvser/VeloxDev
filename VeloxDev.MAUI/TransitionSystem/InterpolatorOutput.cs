﻿using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.MAUI.TransitionSystem
{
    public class InterpolatorOutput : InterpolatorOutputCore
    {
        private readonly IDispatcher? dispatcher = Dispatcher.GetForCurrentThread();

        public override void Update(object target, int frameIndex, bool isUIAccess)
        {
            if (isUIAccess)
            {
                Update(target, frameIndex);
            }
            else
            {
                dispatcher?.Dispatch(() =>
                {
                    Update(target, frameIndex);
                });
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
