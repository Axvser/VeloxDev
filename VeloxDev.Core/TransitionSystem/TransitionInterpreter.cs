using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class TransitionInterpreterCore<
        TOutputCore,
        TTransitionEffectCore,
        TPriorityCore> : TransitionInterpreterCore, ITransitionInterpreter<TPriorityCore>
        where TTransitionEffectCore : ITransitionEffect<TPriorityCore>
        where TOutputCore : IFrameSequence<TPriorityCore>
    {
        public virtual async Task Execute(
            object target,
            IFrameSequence<TPriorityCore> frameSequence,
            ITransitionEffect<TPriorityCore> effect,
            bool isUIAccess,
            CancellationTokenSource cts)
        {
            this.cts = cts;
            var indexs = GetEaseIndex(effect, frameSequence.Count);
            var span = GetDeltaTime(effect);
            var foreverloop = effect.LoopTime == int.MaxValue;
            try
            {
                effect.InvokeStart(target, Args);
                for (int loop = 0;
                    loop <= effect.LoopTime || foreverloop;
                    loop += foreverloop ? 0 : 1)
                {
                    for (int index = 0; index < frameSequence.Count; index++)
                    {
                        if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                        effect.InvokeUpdate(target, Args);
                        frameSequence.Update(
                            target,
                            indexs[index],
                            isUIAccess,
                            effect.Priority);
                        effect.InvokeLateUpdate(target, Args);
                        await Task.Delay(span, cts.Token);
                    }

                    if (effect.IsAutoReverse)
                    {
                        for (int index = frameSequence.Count - 1; index >= 0; index--)
                        {
                            if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                            effect.InvokeUpdate(target, Args);
                            frameSequence.Update(
                                target,
                                indexs[index],
                                isUIAccess,
                                effect.Priority);
                            effect.InvokeLateUpdate(target, Args);
                            await Task.Delay(span, cts.Token);
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

        private static int GetDeltaTime(ITransitionEffect<TPriorityCore> effect)
            => (int)(1000d / effect.FPS);

        private static List<int> GetEaseIndex(
            ITransitionEffect<TPriorityCore> effect,
            int steps)
        {
            List<int> result = [];
            var endIndex = steps - 1d;
            for (int i = 0; i < steps; i++)
            {
                var ease = effect.EaseCalculator.Ease(i / endIndex);
                var index = (int)(steps * ease);
                if (index < 0) result.Add(0);
                else if (index >= steps) result.Add(steps - 1);
                else result.Add(index);
            }
            return result;
        }
    }

    public abstract class TransitionInterpreterCore<
        TOutputCore,
        TTransitionEffectCore> : TransitionInterpreterCore, ITransitionInterpreter
        where TTransitionEffectCore : ITransitionEffectCore
        where TOutputCore : IFrameSequence
    {
        public virtual async Task Execute(
            object target,
            IFrameSequence frameSequence,
            ITransitionEffectCore effect,
            bool isUIAccess,
            CancellationTokenSource cts)
        {
            this.cts = cts;
            var indexs = GetEaseIndex(effect, frameSequence.Count);
            var span = GetDeltaTime(effect);
            var foreverloop = effect.LoopTime == int.MaxValue;
            try
            {
                effect.InvokeStart(target, Args);
                for (int loop = 0;
                    loop <= effect.LoopTime || foreverloop;
                    loop += foreverloop ? 0 : 1)
                {
                    for (int index = 0; index < frameSequence.Count; index++)
                    {
                        if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                        effect.InvokeUpdate(target, Args);
                        frameSequence.Update(
                            target,
                            indexs[index],
                            isUIAccess);
                        effect.InvokeLateUpdate(target, Args);
                        await Task.Delay(span, cts.Token);
                    }

                    if (effect.IsAutoReverse)
                    {
                        for (int index = frameSequence.Count - 1; index >= 0; index--)
                        {
                            if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                            effect.InvokeUpdate(target, Args);
                            frameSequence.Update(
                                target,
                                indexs[index],
                                isUIAccess);
                            effect.InvokeLateUpdate(target, Args);
                            await Task.Delay(span, cts.Token);
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

        private static int GetDeltaTime(ITransitionEffectCore effect)
            => (int)(1000d / effect.FPS);

        private static List<int> GetEaseIndex(
            ITransitionEffectCore effect,
            int steps)
        {
            List<int> result = [];
            var endIndex = steps - 1d;
            for (int i = 0; i < steps; i++)
            {
                var ease = effect.EaseCalculator.Ease(i / endIndex);
                var index = (int)(steps * ease);
                if (index < 0) result.Add(0);
                else if (index >= steps) result.Add(steps - 1);
                else result.Add(index);
            }
            return result;
        }
    }

    public abstract class TransitionInterpreterCore : ITransitionInterpreterCore, IDisposable
    {
        protected CancellationTokenSource? cts = null;
        public virtual FrameEventArgs Args { get; set; } = new();

        public virtual void Exit()
        {
            var oldCts = Interlocked.Exchange(ref cts, null);
            if (oldCts != null && !oldCts.IsCancellationRequested)
            {
                oldCts.Cancel();
            }
            Dispose();
        }

        public virtual void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
