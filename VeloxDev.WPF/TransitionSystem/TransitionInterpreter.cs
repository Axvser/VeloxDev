using System.Reflection;
using System.Windows;
using VeloxDev.WPF.FrameworkSupport;
using VeloxDev.WPF.StructuralDesign.Animator;
using VeloxDev.WPF.TransitionSystem.Basic;

namespace VeloxDev.WPF.TransitionSystem
{
    public sealed class TransitionInterpreter : IExecutableTransition
    {
        internal TransitionInterpreter(
            TransitionScheduler scheduler,
            TransitionParams param,
            CancellationTokenSource cts,
            State state,
            List<List<Tuple<PropertyInfo, List<object?>>>>? preload = null)
        {
            TransitionScheduler = scheduler;
            Param = param;
            Cts = cts;
            State = state;
            if (scheduler.TransitionApplied.TryGetTarget(out var target))
            {
                Target = target;
                DeltaTime = XMath.Clamp((int)param.DeltaTime, 0, int.MaxValue);
                FrameCount = XMath.Clamp((int)param.FrameCount, 2, int.MaxValue);
                FrameEventArgs.MaxFrameIndex = FrameCount;
                LoadFrames(preload);
            }
            else
            {
                Target = new object();
                scheduler.Dispose();
            }
        }

        public TransitionScheduler TransitionScheduler { get; internal set; }
        public TransitionParams Param { get; internal set; }
        public CancellationTokenSource Cts { get; internal set; }
        public State State { get; internal set; }

        public object Target { get; set; }
        public List<List<Tuple<PropertyInfo, List<object?>>>> FrameSequence { get; set; } = [];
        public FrameEventArgs FrameEventArgs { get; internal set; } = new();

        private int DeltaTime { get; set; } = 0;
        private int FrameCount { get; set; } = 1;
        private int LoopDepth { get; set; } = 0;

        public async Task Start(object? target = null)
        {
            var accTimes = GetAccDeltaTime(FrameCount);
            var isInvokeAsync = !Application.Current.Dispatcher.CheckAccess() || Param.IsAsync;
            try
            {
                Param.StartInvoke(Target, FrameEventArgs);
                for (int x = LoopDepth; Param.LoopTime == int.MaxValue || x <= Param.LoopTime; x++, LoopDepth++)
                {
                    if (!Param.IsAutoReverse && x > 0)
                    {
                        ConditionCheck();
                        Reset();
                    }

                    for (int i = 0; i < FrameCount; i++)
                    {
                        ConditionCheck();
                        FrameEventArgs.CurrentFrameIndex = i;
                        FrameStart();
                        for (int j = 0; j < FrameSequence.Count; j++)
                        {
                            for (int k = 0; k < FrameSequence[j].Count; k++)
                            {
                                FrameUpdate(i, j, k, isInvokeAsync);
                            }
                        }
                        FrameEnd();
                        await Task.Delay(Param.Acceleration == 0 ? DeltaTime : i < accTimes.Count & accTimes.Count > 0 ? accTimes[i] : DeltaTime, Cts.Token);
                    }

                    if (Param.IsAutoReverse)
                    {
                        for (int i = FrameCount - 1; i > -1; i--)
                        {
                            ConditionCheck();
                            FrameEventArgs.CurrentFrameIndex = i;
                            FrameStart();
                            for (int j = 0; j < FrameSequence.Count; j++)
                            {
                                for (int k = 0; k < FrameSequence[j].Count; k++)
                                {
                                    FrameUpdate(i, j, k, isInvokeAsync);
                                }
                            }
                            FrameEnd();
                            await Task.Delay(Param.Acceleration == 0 ? DeltaTime : i < accTimes.Count & accTimes.Count > 0 ? accTimes[i] : DeltaTime, Cts.Token);
                        }
                    }
                }
            }
            catch
            {
                WhileCancled();
            }
            finally
            {
                WhileEnded();
            }
        }
        public void Stop()
        {
            var oldsource = Interlocked.CompareExchange(ref TransitionScheduler.tokensource, Cts, TransitionScheduler.tokensource);
            if (oldsource != null)
            {
                oldsource.Cancel();
                oldsource.Dispose();
            }
        }

        private void Reset()
        {
            var isInvokeAsync = !Application.Current.Dispatcher.CheckAccess() || Param.IsAsync;
            FrameEventArgs.CurrentFrameIndex = 0;
            for (int j = 0; j < FrameSequence.Count; j++)
            {
                for (int k = 0; k < FrameSequence[j].Count; k++)
                {
                    FrameUpdate(0, j, k, isInvokeAsync);
                }
            }
        }
        private void LoadFrames(List<List<Tuple<PropertyInfo, List<object?>>>>? preload = null)
        {
            Param.AwakeInvoke(Target, FrameEventArgs);
            if (preload is null)
            {
                var type = Target.GetType();
                FrameSequence = LinearInterpolation.ComputingFrames(type, State, Target, FrameCount);
            }
        }
        private void ConditionCheck()
        {
            if (Cts.IsCancellationRequested || FrameEventArgs.Handled) throw new Exception();
        }
        private void FrameStart()
        {
            Param.UpdateInvoke(Target, FrameEventArgs);
        }
        private async void FrameUpdate(int i, int j, int k, bool isAsync)
        {
            if (!IsFrameIndexRight(i, j, k) || Application.Current == null) return;

            if (isAsync)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    FrameSequence[j][k].Item1.SetValue(Target, FrameSequence[j][k].Item2[i]);
                }, Param.Priority);
            }
            else
            {
                FrameSequence[j][k].Item1.SetValue(Target, FrameSequence[j][k].Item2[i]);
            }
        }
        private void FrameEnd()
        {
            Param.LateUpdateInvoke(Target, FrameEventArgs);
        }
        private void WhileCancled()
        {
            Param.CancledInvoke(Target, FrameEventArgs);
        }
        private void WhileEnded()
        {
            Param.CompletedInvoke(Target, FrameEventArgs);
        }
        private List<int> GetAccDeltaTime(int Steps)
        {
            List<int> result = [];
            if (Param.Acceleration == 0) return result;
            var acc = XMath.Clamp(Param.Acceleration, -1, 1);
            var start = DeltaTime * (1 + acc);
            var end = DeltaTime * (1 - acc);
            var delta = end - start;
            for (int i = 0; i < Steps; i++)
            {
                var t = (double)(i + 1) / Steps;
                result.Add((int)(start + t * delta));
            }
            return result;
        }
        private bool IsFrameIndexRight(int i, int j, int k)
        {
            if (FrameSequence.Count > 0 && j >= 0 && j < FrameSequence.Count)
            {
                if (FrameSequence[j].Count > 0 && k >= 0 && k < FrameSequence[j].Count)
                {
                    if (FrameSequence[j][k].Item2.Count > 0 && i >= 0 && i < FrameSequence[j][k].Item2.Count)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
    }
}
