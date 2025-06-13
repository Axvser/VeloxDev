using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public class TransitionInterpreterBase<TOutput, TPriority>() : ITransitionInterpreter<TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public FrameEventArgs Args { get; protected set; } = new();

        public async Task Execute(
            object target,
            IFrameSequence<TPriority> frameSequence,
            ITransitionEffect<TPriority> effect,
            bool isUIAccess,
            CancellationTokenSource cts)
        {
            var spans = GetAccDeltaTime(effect, frameSequence.Count);
            var foreverloop = effect.LoopTime == int.MaxValue;
            try
            {
                effect.InvokeStart(target, Args);
                for (int loop = 0;
                    loop <= effect.LoopTime || foreverloop;
                    loop += foreverloop ? 0 : 1)
                {
                    for (int index = 0;
                        index > 0 && index < frameSequence.Count;
                        index++)
                    {
                        effect.InvokeUpdate(target, Args);
                        frameSequence.Update(
                            target,
                            index,
                            isUIAccess,
                            effect.Priority);
                        effect.InvokeLateUpdate(target, Args);
                        await Task.Delay(spans[index], cts.Token);
                    }

                    if (effect.IsAutoReverse)
                    {
                        for (int index = frameSequence.Count - 1;
                        index > -1 && index < frameSequence.Count;
                        index--)
                        {
                            effect.InvokeUpdate(target, Args);
                            frameSequence.Update(
                                target,
                                index,
                                isUIAccess,
                                effect.Priority);
                            effect.InvokeLateUpdate(target, Args);
                            await Task.Delay(spans[index], cts.Token);
                        }
                    }
                }
                effect.InvokeCompleted(target, Args);
            }
            catch
            {
                effect.InvokeCancled(target, Args);
            }
            finally
            {
                effect.InvokeFinally(target, Args);
                Exit();
            }
        }

        public void Exit()
        {
            Dispose();
        }

        private static List<int> GetAccDeltaTime(
            ITransitionEffect<TPriority> effect,
            int steps)
        {
            List<int> result = [];
            var standardDeltaTime = 1000d / effect.FPS;
            double acc;
            if (effect.Acceleration > 1) acc = 1;
            else if (effect.Acceleration < -1) acc = -1;
            else acc = effect.Acceleration;
            var start = standardDeltaTime * (1 + acc);
            var end = standardDeltaTime * (1 - acc);
            var delta = end - start;
            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add((int)(start + t * delta));
            }
            return result;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
