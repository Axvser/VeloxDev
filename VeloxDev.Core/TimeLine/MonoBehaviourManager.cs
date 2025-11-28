using System.Collections.Concurrent;
using System.Diagnostics;
using VeloxDev.Core.Interfaces.MonoBehavior;

namespace VeloxDev.Core.TimeLine
{
    public static class MonoBehaviourManager
    {
        #region 内部类

        private class BehaviorWrapper(IMonoBehavior behavior, int executionOrder)
        {
            public IMonoBehavior Behavior { get; } = behavior;
            public int ExecutionOrder { get; } = executionOrder;
        }

        #endregion

        #region 私有字段

        private static readonly ConcurrentDictionary<int, BehaviorWrapper> _behaviors = new();
        private static readonly ConcurrentQueue<IMonoBehavior> _addQueue = new();
        private static readonly ConcurrentQueue<IMonoBehavior> _removeQueue = new();

        private static volatile bool _isRunning = false;
        private static int _targetFPS = 60;
        private static long _totalTimeMs = 0;
        private static long _lastFrameTime = 0;
        private static int _currentFPS = 0;
        private static int _fpsCounter = 0;
        private static long _fpsLastUpdateTime = 0;
        private static long _totalFrames = 0;
        private static int _instanceCounter = 0;

        private static CancellationTokenSource _cancellationTokenSource = new();

        #endregion

        #region 公共属性

        public static int TargetFPS
        {
            get => _targetFPS;
            set => _targetFPS = value < 1 ? 1 : (value > 1000 ? 1000 : value);
        }

        public static int CurrentFPS => _currentFPS;
        public static long TotalTimeMs => _totalTimeMs;
        public static long TotalFrames => _totalFrames;
        public static bool IsRunning => _isRunning;
        public static int ActiveBehaviorCount => _behaviors.Count;

        #endregion

        #region 核心生命周期管理

        public static void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _lastFrameTime = GetCurrentTimestamp();
            _fpsLastUpdateTime = _lastFrameTime;

            ThreadPool.QueueUserWorkItem(_ => RunMainLoop());
            Debug.WriteLine("MonoBehaviourManager started.");
        }

        public static void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationTokenSource?.Cancel();
            Debug.WriteLine("MonoBehaviourManager stopped.");
        }

        public static void RegisterBehavior(IMonoBehavior behavior)
        {
            if (behavior == null)
            {
                Debug.WriteLine("Warning: Attempted to register null behavior");
                return;
            }

            _addQueue.Enqueue(behavior);
        }

        public static void UnregisterBehavior(IMonoBehavior behavior)
        {
            if (behavior == null)
            {
                Debug.WriteLine("Warning: Attempted to unregister null behavior");
                return;
            }

            _removeQueue.Enqueue(behavior);
        }

        #endregion

        #region 主循环核心逻辑

        private static void RunMainLoop()
        {
            Debug.WriteLine("Main loop started");

            while (_isRunning)
            {
                try
                {
                    var frameStartTime = GetCurrentTimestamp();

                    // 处理挂起的操作
                    ProcessPendingOperations();

                    // 计算时间增量
                    var deltaTime = CalculateDeltaTime(frameStartTime);
                    if (deltaTime <= 0)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    // 创建帧事件参数
                    var frameArgs = CreateFrameEventArgs(deltaTime);

                    // 执行行为更新（Handled属性控制执行）
                    ExecuteBehaviors(frameArgs);

                    // 更新性能统计
                    UpdatePerformanceStats(frameStartTime, deltaTime);

                    // 帧率控制
                    FrameRateControl(frameStartTime);

                    _totalFrames++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in main loop: {ex.Message}");
                    Thread.Sleep(10);
                }
            }

            Debug.WriteLine("Main loop stopped");
        }

        private static FrameEventArgs CreateFrameEventArgs(int deltaTime)
        {
            return new FrameEventArgs
            {
                DeltaTime = deltaTime,
                TotalTime = (int)_totalTimeMs,
                CurrentFPS = _currentFPS,
                TargetFPS = _targetFPS,
                Handled = false
            };
        }

        private static void ExecuteBehaviors(FrameEventArgs frameArgs)
        {
            if (_behaviors.IsEmpty || frameArgs.Handled) return;

            // 按执行顺序排序
            var wrappers = _behaviors.Values
                .OrderBy(w => w.ExecutionOrder)
                .ToArray();

            foreach (var wrapper in wrappers)
            {
                if (wrapper?.Behavior == null) continue;

                // 检查Handled状态，如果被设置为true则中断执行
                if (frameArgs.Handled) break;

                try
                {
                    wrapper.Behavior.InvokeUpdate(frameArgs);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in Update for {wrapper.Behavior.GetType().Name}: {ex.Message}");
                }
            }
        }

        #endregion

        #region 辅助方法

        private static void ProcessPendingOperations()
        {
            ProcessAddedBehaviors();
            ProcessRemovedBehaviors();
        }

        private static void ProcessAddedBehaviors()
        {
            while (_addQueue.TryDequeue(out var behavior))
            {
                if (behavior == null) continue;

                try
                {
                    int executionOrder = Interlocked.Increment(ref _instanceCounter);
                    var wrapper = new BehaviorWrapper(behavior, executionOrder);

                    _behaviors[GetBehaviorHash(behavior)] = wrapper;

                    // 初始化行为（不受Handled影响）
                    SafeExecute(behavior.InvokeAwake, "Awake", behavior);
                    SafeExecute(behavior.InvokeStart, "Start", behavior);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error registering behavior {behavior.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void ProcessRemovedBehaviors()
        {
            while (_removeQueue.TryDequeue(out var behavior))
            {
                if (behavior == null) continue;

                _behaviors.TryRemove(GetBehaviorHash(behavior), out _);
            }
        }

        private static int GetBehaviorHash(IMonoBehavior behavior)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(behavior);
        }

        private static long GetCurrentTimestamp()
        {
            return Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
        }

        private static int CalculateDeltaTime(long currentTime)
        {
            if (_lastFrameTime <= 0) return 1;
            var delta = (int)(currentTime - _lastFrameTime);
            return delta < 1 ? 1 : delta;
        }

        private static void UpdatePerformanceStats(long frameStartTime, int deltaTime)
        {
            _totalTimeMs += deltaTime;
            _lastFrameTime = frameStartTime;

            _fpsCounter++;
            var currentTime = GetCurrentTimestamp();
            if (currentTime - _fpsLastUpdateTime >= 1000)
            {
                _currentFPS = _fpsCounter;
                _fpsCounter = 0;
                _fpsLastUpdateTime = currentTime;
            }
        }

        private static void FrameRateControl(long frameStartTime)
        {
            var targetFrameTime = 1000.0 / _targetFPS;
            var elapsed = GetCurrentTimestamp() - frameStartTime;

            if (elapsed < targetFrameTime)
            {
                var sleepTime = (int)(targetFrameTime - elapsed);
                if (sleepTime > 0) Thread.Sleep(sleepTime);
            }
        }

        private static void SafeExecute(Action action, string methodName, IMonoBehavior behavior)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in {methodName} for {behavior?.GetType().Name}: {ex.Message}");
            }
        }

        #endregion
    }
}