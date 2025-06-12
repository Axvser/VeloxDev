﻿using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public class TransitionInterpreter<TUpdator, TOutput, TPriority>() : ITransitionInterpreter<TPriority>
        where TUpdator : IFrameUpdator<TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public FrameEventArgs Args { get; protected set; } = new();

        public async Task Execute(
            object target,
            IFrameSequence<TPriority> frameSequence,
            ITransitionEffect<TPriority> effect,
            bool isUIAccess,
            IFrameUpdator<TPriority> updator,
            CancellationTokenSource cts)
        {
            var spans = GetSpans(frameSequence, effect);
            try
            {
                effect.StartInvoke(target, Args);
                for (int loop = effect.LoopTime;
                    loop > 0;
                    loop -= effect.LoopTime == int.MaxValue ? 0 : 1)
                {
                    for (int index = 0;
                        index > 0 && index < frameSequence.Count;
                        index++)
                    {
                        await updator.Update(
                            target,
                            isUIAccess,
                            index,
                            frameSequence,
                            effect,
                            spans[index],
                            cts);
                    }

                    if (effect.IsAutoReverse)
                    {
                        for (int index = frameSequence.Count - 1;
                        index > -1 && index < frameSequence.Count;
                        index--)
                        {
                            await updator.Update(
                                target,
                                isUIAccess,
                                index,
                                frameSequence,
                                effect,
                                spans[index],
                                cts);
                        }
                    }
                }
                effect.CompletedInvoke(target, Args);
            }
            catch
            {
                effect.CancledInvoke(target, Args);
            }
            finally
            {
                effect.FinallyInvoke(target, Args);
            }
        }

        public void Exit()
        {
            Dispose();
        }

        public static List<int> GetSpans(IFrameSequence<TPriority> frameSequence, ITransitionEffect<TPriority> effect)
        {
            List<int> spans = [];
            var span = (int)(1000d / effect.FPS);

            for (int i = 0; i < frameSequence.Count; i++)
            {
                spans.Add(span + GetSpanOffset(frameSequence.Count, span, effect.Acceleration, i));
            }

            return spans;
        }
        public static int GetSpanOffset(int count, double span, double acc, int index)
        {
            if (acc == 0 || count == 0) return 0;
            return (int)(index / count * span);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
