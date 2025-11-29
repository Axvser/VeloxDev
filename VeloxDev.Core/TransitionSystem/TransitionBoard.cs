using System.Collections.Concurrent;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TimeLine;

namespace VeloxDev.Core.TransitionSystem
{
    public class TransitionBoardCore<
        T,
        TStateCore,
        TEffectCore,
        TInterpolatorCore,
        TUIThreadInspectorCore,
        TTransitionInterpreterCore> : TransitionBoardCore, ITransitionBoard
        where T : class
        where TStateCore : IFrameState, new()
        where TEffectCore : ITransitionEffectCore, new()
        where TInterpolatorCore : IFrameInterpolator, new()
        where TUIThreadInspectorCore : IUIThreadInspector, new()
        where TTransitionInterpreterCore : class, ITransitionInterpreter, new()
    {
        private readonly ConcurrentDictionary<object, List<ITransitionBoardItem>> _animationItems = new();
        private readonly System.Timers.Timer _timer = new();
        private DateTime _startTime;
        private bool _isRunning = false;

        internal TUIThreadInspectorCore uIThreadInspector = new();

        public TransitionBoardCore()
        {
            _timer.Interval = 1000.0 / 60.0; // 默认60FPS
            _timer.Elapsed += OnTimerElapsed;
        }

        public bool Add(object target, StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> snapshot)
        {
            if (target == null || snapshot == null) return false;

            var boardItem = new TransitionBoardItem(
                target,
                snapshot.state,
                snapshot.effect,
                snapshot.interpolator);

            // 预计算帧序列
            var isUIThread = uIThreadInspector.IsUIThread();
            boardItem.FrameSequence = snapshot.interpolator.Interpolate(
                target, snapshot.state, snapshot.effect, isUIThread, uIThreadInspector);

            // 添加到动画项字典
            if (!_animationItems.TryGetValue(target, out var items))
            {
                items = [];
                _animationItems[target] = items;
            }

            items.Add(boardItem);
            return true;
        }

        public bool Remove(object target, StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> snapshot)
        {
            if (target == null || !_animationItems.TryGetValue(target, out var items))
                return false;

            // 查找并移除对应的动画项
            var itemToRemove = items.FirstOrDefault(item =>
                ReferenceEquals(item.State, snapshot.state) && ReferenceEquals(item.Effect, snapshot.effect));

            if (itemToRemove != null)
            {
                items.Remove(itemToRemove);
                if (items.Count == 0)
                {
                    _animationItems.TryRemove(target, out _);
                }
                return true;
            }

            return false;
        }

        public override void Execute()
        {
            if (_isRunning) return;

            _startTime = DateTime.Now;
            _isRunning = true;
            _timer.Start();

            // 触发所有动画项的Awake事件
            foreach (var targetItems in _animationItems.Values)
            {
                foreach (var item in targetItems)
                {
                    item.Effect.InvokeAwake(item.Target, new TransitionEventArgs());
                }
            }
        }

        public override void Pause()
        {
            if (!_isRunning) return;

            _timer.Stop();
            _isRunning = false;
        }

        public override void Resume()
        {
            if (_isRunning) return;

            _timer.Start();
            _isRunning = true;
        }

        private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (!_isRunning) return;

            var currentTime = DateTime.Now;
            var elapsed = currentTime - _startTime;

            // 处理所有动画项
            foreach (var targetItems in _animationItems.Values.ToList())
            {
                for (int i = targetItems.Count - 1; i >= 0; i--)
                {
                    var item = targetItems[i];
                    ProcessBoardItem(item, elapsed);

                    // 移除已完成的动画项
                    if (item.IsCompleted)
                    {
                        targetItems.RemoveAt(i);
                    }
                }
            }

            // 清理空的目标
            var emptyTargets = _animationItems.Where(kvp => kvp.Value.Count == 0)
                                              .Select(kvp => kvp.Key).ToList();
            foreach (var target in emptyTargets)
            {
                _animationItems.TryRemove(target, out _);
            }

            // 如果所有动画都完成，停止计时器
            if (_animationItems.IsEmpty)
            {
                StopBoard();
            }
        }

        private void ProcessBoardItem(ITransitionBoardItem item, TimeSpan elapsed)
        {
            var effect = item.Effect;
            var frameSequence = item.FrameSequence;

            // 检查frameSequence是否为空
            if (frameSequence == null) return;

            // 考虑开始偏移
            var effectiveElapsed = elapsed - item.StartOffset;
            if (effectiveElapsed < TimeSpan.Zero)
            {
                // 动画尚未开始
                return;
            }

            // 计算归一化进度 (0.0 - 1.0)
            double rawProgress = effectiveElapsed.TotalMilliseconds / effect.Duration.TotalMilliseconds;

            // 处理循环
            if (effect.LoopTime > 0 || effect.LoopTime == int.MaxValue)
            {
                rawProgress %= 1.0;
            }

            // 应用缓动函数
            double easedProgress = effect.Ease.Ease(rawProgress < 0 ? 0 : (rawProgress > 1 ? 1 : rawProgress));

            // 计算帧索引 - 基于动画自身的帧序列
            int frameIndex = (int)(easedProgress * (frameSequence.Count - 1));
            frameIndex = frameIndex < 0 ? 0 : (frameIndex >= frameSequence.Count ? frameSequence.Count - 1 : frameIndex);

            // 检查是否完成
            if (rawProgress >= 1.0 && (effect.LoopTime == 0 || effect.LoopTime == int.MaxValue))
            {
                item.IsCompleted = true;
                effect.InvokeCompleted(item.Target, new TransitionEventArgs());
                effect.InvokeFinally(item.Target, new TransitionEventArgs());
                return;
            }

            // 应用当前帧
            var isUIThread = uIThreadInspector.IsUIThread();
            uIThreadInspector.ProtectedInvoke(isUIThread, () =>
            {
                // 触发Update事件
                effect.InvokeUpdate(item.Target, new TransitionEventArgs());

                // 更新属性
                frameSequence.Update(item.Target, frameIndex, isUIThread);

                // 触发LateUpdate事件
                effect.InvokeLateUpdate(item.Target, new TransitionEventArgs());
            });
        }

        private void StopBoard()
        {
            _timer.Stop();
            _isRunning = false;
        }

        public override void Exit()
        {
            StopBoard();

            // 触发所有动画项的Cancel事件
            foreach (var targetItems in _animationItems.Values)
            {
                foreach (var item in targetItems)
                {
                    item.Effect.InvokeCancled(item.Target, new TransitionEventArgs());
                    item.Effect.InvokeFinally(item.Target, new TransitionEventArgs());
                }
            }

            _animationItems.Clear();
            Dispose();
        }

        // 属性设置方法
        public TransitionBoardCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore>
            WithFrameRate(double fps)
        {
            _timer.Interval = 1000.0 / fps;
            return this;
        }

        public TransitionBoardCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore>
            WithStartOffset(object target, TimeSpan offset)
        {
            if (_animationItems.TryGetValue(target, out var items))
            {
                foreach (var item in items)
                {
                    item.StartOffset = offset;
                }
            }
            return this;
        }
    }

    public class TransitionBoardCore<
        T,
        TStateCore,
        TEffectCore,
        TInterpolatorCore,
        TUIThreadInspectorCore,
        TTransitionInterpreterCore,
        TPriorityCore> : TransitionBoardCore, ITransitionBoard
        where T : class
        where TStateCore : IFrameState, new()
        where TEffectCore : ITransitionEffect<TPriorityCore>, new()
        where TInterpolatorCore : IFrameInterpolator<TPriorityCore>, new()
        where TUIThreadInspectorCore : IUIThreadInspector<TPriorityCore>, new()
        where TTransitionInterpreterCore : class, ITransitionInterpreter<TPriorityCore>, new()
    {
        private readonly ConcurrentDictionary<object, List<ITransitionBoardItem<TPriorityCore>>> _animationItems = new();
        private readonly System.Timers.Timer _timer = new();
        private DateTime _startTime;
        private bool _isRunning = false;

        internal TUIThreadInspectorCore uIThreadInspector = new();

        public TransitionBoardCore()
        {
            _timer.Interval = 1000.0 / 60.0; // 默认60FPS
            _timer.Elapsed += OnTimerElapsed;
        }

        public bool Add(object target, StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> snapshot)
        {
            if (target == null || snapshot == null) return false;

            var boardItem = new TransitionBoardItem<TPriorityCore>(
                target,
                snapshot.state,
                snapshot.effect,
                snapshot.interpolator);

            // 预计算帧序列
            var isUIThread = uIThreadInspector.IsUIThread();
            boardItem.FrameSequence = snapshot.interpolator.Interpolate(
                target, snapshot.state, snapshot.effect, isUIThread, uIThreadInspector);

            // 添加到动画项字典
            if (!_animationItems.TryGetValue(target, out var items))
            {
                items = [];
                _animationItems[target] = items;
            }

            items.Add(boardItem);
            return true;
        }

        public bool Remove(object target, StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> snapshot)
        {
            if (target == null || !_animationItems.TryGetValue(target, out var items))
                return false;

            // 查找并移除对应的动画项
            var itemToRemove = items.FirstOrDefault(item =>
                ReferenceEquals(item.State, snapshot.state) && ReferenceEquals(item.Effect, snapshot.effect));

            if (itemToRemove != null)
            {
                items.Remove(itemToRemove);
                if (items.Count == 0)
                {
                    _animationItems.TryRemove(target, out _);
                }
                return true;
            }

            return false;
        }

        public override void Execute()
        {
            if (_isRunning) return;

            _startTime = DateTime.Now;
            _isRunning = true;
            _timer.Start();

            // 触发所有动画项的Awake事件
            foreach (var targetItems in _animationItems.Values)
            {
                foreach (var item in targetItems)
                {
                    item.Effect.InvokeAwake(item.Target, new TransitionEventArgs());
                }
            }
        }

        public override void Pause()
        {
            if (!_isRunning) return;

            _timer.Stop();
            _isRunning = false;
        }

        public override void Resume()
        {
            if (_isRunning) return;

            _timer.Start();
            _isRunning = true;
        }

        private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (!_isRunning) return;

            var currentTime = DateTime.Now;
            var elapsed = currentTime - _startTime;

            // 处理所有动画项
            foreach (var targetItems in _animationItems.Values.ToList())
            {
                for (int i = targetItems.Count - 1; i >= 0; i--)
                {
                    var item = targetItems[i];
                    ProcessBoardItem(item, elapsed);

                    // 移除已完成的动画项
                    if (item.IsCompleted)
                    {
                        targetItems.RemoveAt(i);
                    }
                }
            }

            // 清理空的目标
            var emptyTargets = _animationItems.Where(kvp => kvp.Value.Count == 0)
                                              .Select(kvp => kvp.Key).ToList();
            foreach (var target in emptyTargets)
            {
                _animationItems.TryRemove(target, out _);
            }

            // 如果所有动画都完成，停止计时器
            if (_animationItems.IsEmpty)
            {
                StopBoard();
            }
        }

        private void ProcessBoardItem(ITransitionBoardItem<TPriorityCore> item, TimeSpan elapsed)
        {
            var effect = item.Effect;
            var frameSequence = item.FrameSequence;

            // 检查frameSequence是否为空
            if (frameSequence == null) return;

            // 考虑开始偏移
            var effectiveElapsed = elapsed - item.StartOffset;
            if (effectiveElapsed < TimeSpan.Zero)
            {
                // 动画尚未开始
                return;
            }

            // 计算归一化进度 (0.0 - 1.0)
            double rawProgress = effectiveElapsed.TotalMilliseconds / effect.Duration.TotalMilliseconds;

            // 处理循环
            if (effect.LoopTime > 0 || effect.LoopTime == int.MaxValue)
            {
                rawProgress %= 1.0;
            }

            // 应用缓动函数
            double easedProgress = effect.Ease.Ease(rawProgress < 0 ? 0 : (rawProgress > 1 ? 1 : rawProgress));

            // 计算帧索引 - 基于动画自身的帧序列
            int frameIndex = (int)(easedProgress * (frameSequence.Count - 1));
            frameIndex = frameIndex < 0 ? 0 : (frameIndex >= frameSequence.Count ? frameSequence.Count - 1 : frameIndex);

            // 检查是否完成
            if (rawProgress >= 1.0 && (effect.LoopTime == 0 || effect.LoopTime == int.MaxValue))
            {
                item.IsCompleted = true;
                effect.InvokeCompleted(item.Target, new TransitionEventArgs());
                effect.InvokeFinally(item.Target, new TransitionEventArgs());
                return;
            }

            // 应用当前帧
            var isUIThread = uIThreadInspector.IsUIThread();
            uIThreadInspector.ProtectedInvoke(isUIThread, () =>
            {
                // 触发Update事件
                effect.InvokeUpdate(item.Target, new TransitionEventArgs());

                // 更新属性
                frameSequence.Update(item.Target, frameIndex, isUIThread, effect.Priority);

                // 触发LateUpdate事件
                effect.InvokeLateUpdate(item.Target, new TransitionEventArgs());
            }, effect.Priority);
        }

        private void StopBoard()
        {
            _timer.Stop();
            _isRunning = false;
        }

        public override void Exit()
        {
            StopBoard();

            // 触发所有动画项的Cancel事件
            foreach (var targetItems in _animationItems.Values)
            {
                foreach (var item in targetItems)
                {
                    item.Effect.InvokeCancled(item.Target, new TransitionEventArgs());
                    item.Effect.InvokeFinally(item.Target, new TransitionEventArgs());
                }
            }

            _animationItems.Clear();
            Dispose();
        }

        // 属性设置方法
        public TransitionBoardCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>
            WithFrameRate(double fps)
        {
            _timer.Interval = 1000.0 / fps;
            return this;
        }

        public TransitionBoardCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>
            WithStartOffset(object target, TimeSpan offset)
        {
            if (_animationItems.TryGetValue(target, out var items))
            {
                foreach (var item in items)
                {
                    item.StartOffset = offset;
                }
            }
            return this;
        }
    }

    public abstract class TransitionBoardCore : IDisposable
    {
        public abstract void Execute();
        public abstract void Pause();
        public abstract void Resume();
        public abstract void Exit();

        #region IDisposable 实现
        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Exit();
                }
                _disposed = true;
            }
        }

        ~TransitionBoardCore()
        {
            Dispose(false);
        }
        #endregion
    }
}
