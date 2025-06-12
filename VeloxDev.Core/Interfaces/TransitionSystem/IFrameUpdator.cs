using System;
using System.Collections.Generic;
using System.Text;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameUpdator<TPriority>
    {
        public Task Update(object target,
            bool isUIAccess,
            int frameIndex,
            IFrameSequence<TPriority> frameSequence,
            ITransitionEffect<TPriority> effect,
            int duration,
            CancellationTokenSource cts);
    }
}
