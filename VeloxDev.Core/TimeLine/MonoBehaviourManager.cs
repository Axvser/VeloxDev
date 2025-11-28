using System.Collections.Concurrent;
using System.Diagnostics;
using VeloxDev.Core.Interfaces.MonoBehaviour;

namespace VeloxDev.Core.TimeLine
{
    public static class MonoBehaviourManager
    {
        #region 内部类

        private class BehaviorWrapper(IMonoBehaviour​ behavior, int executionOrder)
        {
            public IMonoBehaviour​ Behavior { get; } = behavior;
            public int ExecutionOrder { get; } = executionOrder;
        }

        private class ConfigChangeRequest
        {
            public int? TargetFPS { get; set; }
            public Action<FrameEventArgs>? PreUpdateCallback { get; set; }
            public Action<FrameEventArgs>? PostUpdateCallback { get; set; }
        }

        #endregion

        #region 私有字段

        private static readonly ConcurrentDictionary<int, BehaviorWrapper> _behaviors = new();
        private static readonly ConcurrentQueue<IMonoBehaviour​> _addQueue = new();
        private static readonly ConcurrentQueue<IMonoBehaviour​> _removeQueue = new();
        private static readonly ConcurrentQueue<ConfigChangeRequest> _configQueue = new();

        // 性能参数
        private static volatile bool _isRunning = false;
        private static int _targetFPS = 60;
        private static long _totalTimeMs = 0;
        private static long _lastFrameTime = 0;
        private static int _currentFPS = 0;
        private static int _fpsCounter = 0;
        private static long _fpsLastUpdateTime = 0;
        private static long _totalFrames = 0;
        private static int _instanceCounter = 0;

        // 缓存和配置
        private static readonly FrameEventArgs _cachedFrameArgs = new();
        private static Action<FrameEventArgs>? _preUpdateCallback;
        private static Action<FrameEventArgs>? _postUpdateCallback;

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

        #region 配置修改API

        public static void SetTargetFPS(int fps)
        {
            if (fps < 1 || fps > 1000) return;
            _configQueue.Enqueue(new ConfigChangeRequest { TargetFPS = fps });
        }

        public static void SetPreUpdateCallback(Action<FrameEventArgs> callback)
        {
            _configQueue.Enqueue(new ConfigChangeRequest { PreUpdateCallback = callback });
        }

        public static void SetPostUpdateCallback(Action<FrameEventArgs> callback)
        {
            _configQueue.Enqueue(new ConfigChangeRequest { PostUpdateCallback = callback });
        }

        public static void ClearCallbacks()
        {
            _configQueue.Enqueue(new ConfigChangeRequest
            {
                PreUpdateCallback = null,
                PostUpdateCallback = null
            });
        }

        public static void Pause()
        {
            _configQueue.Enqueue(new ConfigChangeRequest
            {
                PreUpdateCallback = args => args.Handled = true
            });
        }

        public static void Resume()
        {
            _configQueue.Enqueue(new ConfigChangeRequest { PreUpdateCallback = null });
        }

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

        public static void RegisterBehaviour(IMonoBehaviour​ behavior)
        {
            if (behavior == null)
            {
                Debug.WriteLine("Warning: Attempted to register null behavior");
                return;
            }

            _addQueue.Enqueue(behavior);
        }

        public static void UnregisterBehaviour(IMonoBehaviour​ behavior)
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

                    ProcessPendingOperations();

                    var deltaTime = CalculateDeltaTime(frameStartTime);
                    if (deltaTime <= 0)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    var frameArgs = CreateFrameEventArgs(deltaTime);

                    _preUpdateCallback?.Invoke(frameArgs);

                    if (!frameArgs.Handled)
                    {
                        ExecuteBehaviors(frameArgs);
                    }

                    _postUpdateCallback?.Invoke(frameArgs);

                    UpdatePerformanceStats(frameStartTime, deltaTime);
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
            // 直接复用缓存对象并赋值
            _cachedFrameArgs.DeltaTime = deltaTime;
            _cachedFrameArgs.TotalTime = (int)_totalTimeMs;
            _cachedFrameArgs.CurrentFPS = _currentFPS;
            _cachedFrameArgs.TargetFPS = _targetFPS;
            _cachedFrameArgs.Handled = false;

            return _cachedFrameArgs;
        }

        private static void ExecuteBehaviors(FrameEventArgs frameArgs)
        {
            if (_behaviors.IsEmpty || frameArgs.Handled) return;

            var wrappers = _behaviors.Values
                .OrderBy(w => w.ExecutionOrder)
                .ToArray();

            foreach (var wrapper in wrappers)
            {
                if (wrapper?.Behavior == null) continue;
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
            ProcessConfigChanges();
            ProcessAddedBehaviors();
            ProcessRemovedBehaviors();
        }

        private static void ProcessConfigChanges()
        {
            while (_configQueue.TryDequeue(out var config))
            {
                if (config.TargetFPS.HasValue)
                {
                    _targetFPS = config.TargetFPS.Value;
                }

                if (config.PreUpdateCallback != null || config.PreUpdateCallback == null)
                {
                    _preUpdateCallback = config.PreUpdateCallback;
                }

                if (config.PostUpdateCallback != null || config.PostUpdateCallback == null)
                {
                    _postUpdateCallback = config.PostUpdateCallback;
                }
            }
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

        private static int GetBehaviorHash(IMonoBehaviour​ behavior)
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

        private static void SafeExecute(Action action, string methodName, IMonoBehaviour​ behavior)
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