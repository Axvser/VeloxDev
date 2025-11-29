using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VeloxDev.Core.Interfaces.MonoBehaviour;

namespace VeloxDev.Core.TimeLine
{
    public static class MonoBehaviourManager
    {
        #region 内部类

        private class BehaviorWrapper(IMonoBehaviour behavior, int executionOrder)
        {
            public IMonoBehaviour Behavior { get; } = behavior;
            public int ExecutionOrder { get; } = executionOrder;
            public readonly object LockObject = new();
        }

        private class ConfigChangeRequest
        {
            public int? TargetFPS { get; set; }
            public int? FixedUpdateInterval { get; set; }
            public float? TimeScale { get; set; }
            public bool? PauseState { get; set; }
            public Action<FrameEventArgs>? PreUpdateCallback { get; set; }
            public Action<FrameEventArgs>? PostUpdateCallback { get; set; }
        }

        #endregion

        #region 私有字段 - 缓存优化

        // 核心数据结构 - 只创建一次
        private static readonly ConcurrentDictionary<int, BehaviorWrapper> _behaviors = new();
        private static readonly ConcurrentQueue<IMonoBehaviour> _addQueue = new();
        private static readonly ConcurrentQueue<IMonoBehaviour> _removeQueue = new();
        private static readonly ConcurrentQueue<ConfigChangeRequest> _configQueue = new();
        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private static readonly BlockingCollection<FrameEventArgs> _fixedUpdateEvents = [];

        // 性能参数 - 需要重置的统计信息
        private static volatile bool _isRunning = false;
        private static volatile bool _isPaused = false;
        private static float _timeScale = 1.0f;
        private static int _targetFPS = 60;
        private static int _fixedUpdateInterval = 16;

        // 这些统计信息需要在Stop时重置
        private static long _totalTimeMs = 0;
        private static long _lastFrameTime = 0;
        private static int _currentFPS = 0;
        private static int _fpsCounter = 0;
        private static long _fpsLastUpdateTime = 0;
        private static long _totalFrames = 0;

        // 这些不需要重置
        private static int _instanceCounter = 0;

        // 线程控制 - 缓存对象池
        private static CancellationTokenSource _mainCancellationTokenSource = new();
        private static Task? _updateTask;
        private static Task? _fixedUpdateTask;

        // 对象池 - 避免频繁创建
        private static readonly ObjectPool<FrameEventArgs> _frameEventArgsPool = new(() => new FrameEventArgs(), 100);
        private static readonly ObjectPool<ConfigChangeRequest> _configRequestPool = new(() => new ConfigChangeRequest(), 50);
        private static readonly ConcurrentQueue<BehaviorWrapper> _wrapperPool = new();

        // 缓存计算结果
        private static double _cachedTargetFrameTime = 1000.0 / 60;
        private static long _lastConfigCheckTime = 0;
        private static BehaviorWrapper[] _cachedWrappers = [];

        // 事件
        public static event EventHandler? OnSystemStarted;
        public static event EventHandler? OnSystemPaused;
        public static event EventHandler? OnSystemResumed;
        public static event EventHandler? OnSystemStopped;

        #endregion

        #region 对象池实现

        private class ObjectPool<T>(Func<T> createFunc, int maxSize) where T : class
        {
            private readonly ConcurrentQueue<T> _pool = new();
            private readonly Func<T> _createFunc = createFunc;
            private readonly int _maxSize = maxSize;

            public T Get()
            {
                if (_pool.TryDequeue(out var item))
                    return item;
                return _createFunc();
            }

            public void Return(T item)
            {
                if (_pool.Count < _maxSize)
                    _pool.Enqueue(item);
            }
        }

        #endregion

        #region 公共静态属性

        public static bool IsRunning => _isRunning;
        public static bool IsPaused => _isPaused;
        public static int CurrentFPS => _currentFPS;
        public static int TargetFPS => _targetFPS;
        public static long TotalTimeMs => _totalTimeMs;
        public static long TotalFrames => _totalFrames;
        public static int ActiveBehaviorCount => _behaviors.Count;
        public static float TimeScale => _timeScale;

        public static string SystemStatus
        {
            get
            {
                if (!_isRunning) return "Stopped";
                if (_isPaused) return "Paused";
                return "Running";
            }
        }

        public static bool IsUpdateThreadAlive => _updateTask?.Status == TaskStatus.Running;
        public static bool IsFixedUpdateThreadAlive => _fixedUpdateTask?.Status == TaskStatus.Running;

        #endregion

        #region 配置修改API - 使用对象池

        public static void SetTargetFPS(int fps)
        {
            if (fps < 1 || fps > 1000) return;
            var request = _configRequestPool.Get();
            request.TargetFPS = fps;
            request.FixedUpdateInterval = null;
            request.TimeScale = null;
            request.PauseState = null;
            _configQueue.Enqueue(request);
            _cachedTargetFrameTime = 1000.0 / fps;
        }

        public static void SetFixedUpdateInterval(int intervalMs)
        {
            if (intervalMs < 1 || intervalMs > 1000) return;
            var request = _configRequestPool.Get();
            request.TargetFPS = null;
            request.FixedUpdateInterval = intervalMs;
            request.TimeScale = null;
            request.PauseState = null;
            _configQueue.Enqueue(request);
        }

        public static void SetTimeScale(float timeScale)
        {
            if (timeScale < 0) timeScale = 0;
            var request = _configRequestPool.Get();
            request.TargetFPS = null;
            request.FixedUpdateInterval = null;
            request.TimeScale = timeScale;
            request.PauseState = null;
            _configQueue.Enqueue(request);
        }

        public static void ExecuteOnMainThread(Action action)
        {
            _mainThreadQueue.Enqueue(action);
        }

        #endregion

        #region 核心生命周期管理

        public static void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _isPaused = false;
            _mainCancellationTokenSource = new CancellationTokenSource();
            _lastFrameTime = GetCurrentTimestamp();
            _fpsLastUpdateTime = _lastFrameTime;

            // 预缓存包装器数组
            UpdateCachedWrappers();

            _fixedUpdateTask = Task.Run(() => FixedUpdateLoop(_mainCancellationTokenSource.Token),
                _mainCancellationTokenSource.Token);

            _updateTask = Task.Run(() => UpdateLoop(_mainCancellationTokenSource.Token),
                _mainCancellationTokenSource.Token);

            OnSystemStarted?.Invoke(null, EventArgs.Empty);
            Debug.WriteLine("MonoBehaviourManager started.");
        }

        public static async void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _isPaused = false;
            _mainCancellationTokenSource.Cancel();

            try
            {
                var stopTasks = new List<Task>();
                if (_updateTask != null) stopTasks.Add(_updateTask);
                if (_fixedUpdateTask != null) stopTasks.Add(_fixedUpdateTask);

                await Task.WhenAll([.. stopTasks]).ContinueWith(_ => { });
            }
            catch (OperationCanceledException)
            {
                // 预期中的取消
            }
            finally
            {
                _updateTask = null;
                _fixedUpdateTask = null;
            }

            // 重置统计信息
            ResetStatistics();

            // 清空队列但不释放集合
            ClearQueues();

            OnSystemStopped?.Invoke(null, EventArgs.Empty);
            Debug.WriteLine("MonoBehaviourManager stopped.");
        }

        private static void ResetStatistics()
        {
            // 重置所有运行时统计信息
            _totalTimeMs = 0;
            _currentFPS = 0;
            _fpsCounter = 0;
            _fpsLastUpdateTime = 0;
            _totalFrames = 0;
            _lastFrameTime = 0;

            // 重置时间戳
            _lastConfigCheckTime = 0;

            Debug.WriteLine("Statistics reset for new session.");
        }

        private static void ClearQueues()
        {
            // 清空队列并回收对象到对象池
            while (_fixedUpdateEvents.TryTake(out var frameEvent))
            {
                if (frameEvent is FrameEventArgs args)
                    _frameEventArgsPool.Return(args);
            }

            while (_configQueue.TryDequeue(out var config))
            {
                _configRequestPool.Return(config);
            }

            while (_mainThreadQueue.TryDequeue(out _)) { }
        }

        public static void Pause()
        {
            if (!_isRunning || _isPaused) return;
            _isPaused = true;
            OnSystemPaused?.Invoke(null, EventArgs.Empty);
        }

        public static void Resume()
        {
            if (!_isRunning || !_isPaused) return;
            _isPaused = false;
            OnSystemResumed?.Invoke(null, EventArgs.Empty);
        }

        public static void RegisterBehaviour(IMonoBehaviour behavior)
        {
            ExecuteOnMainThread(() =>
            {
                if (behavior == null) return;
                _addQueue.Enqueue(behavior);
            });
        }

        public static void UnregisterBehaviour(IMonoBehaviour behavior)
        {
            ExecuteOnMainThread(() =>
            {
                if (behavior == null) return;
                _removeQueue.Enqueue(behavior);
            });
        }

        #endregion

        #region FixedUpdate 线程 - 使用对象池

        private static async Task FixedUpdateLoop(CancellationToken token)
        {
            long lastFixedUpdateTime = GetCurrentTimestamp();

            try
            {
                while (_isRunning && !token.IsCancellationRequested)
                {
                    if (_isPaused)
                    {
                        await Task.Delay(10, token);
                        continue;
                    }

                    var currentTime = GetCurrentTimestamp();
                    var elapsed = currentTime - lastFixedUpdateTime;

                    if (elapsed >= _fixedUpdateInterval)
                    {
                        var fixedFrameArgs = CreateFixedFrameEventArgs((int)elapsed);

                        await ExecuteBehaviorsFixedUpdate(fixedFrameArgs, token);

                        if (!fixedFrameArgs.Handled)
                        {
                            _fixedUpdateEvents.Add(fixedFrameArgs, token);
                        }

                        lastFixedUpdateTime = currentTime;
                    }

                    await Task.Delay(1, token);
                }
            }
            catch (OperationCanceledException)
            {
                // 预期中的取消
            }
        }

        private static FrameEventArgs CreateFixedFrameEventArgs(int deltaTime)
        {
            var frameArgs = _frameEventArgsPool.Get();
            frameArgs.DeltaTime = (int)(deltaTime * _timeScale);
            frameArgs.TotalTime = (int)_totalTimeMs;
            frameArgs.CurrentFPS = _currentFPS;
            frameArgs.TargetFPS = _targetFPS;
            frameArgs.Handled = false;
            return frameArgs;
        }

        #endregion

        #region Update 线程 - 优化缓存

        private static async Task UpdateLoop(CancellationToken token)
        {
            try
            {
                while (_isRunning && !token.IsCancellationRequested)
                {
                    if (_isPaused)
                    {
                        await Task.Delay(10, token);
                        continue;
                    }

                    var frameStartTime = GetCurrentTimestamp();

                    ProcessMainThreadOperations();

                    var deltaTime = CalculateDeltaTime(frameStartTime);
                    if (deltaTime <= 0)
                    {
                        await Task.Delay(1, token);
                        continue;
                    }

                    var frameArgs = CreateFrameEventArgs(deltaTime);

                    ProcessFixedUpdateEvents();

                    await ExecuteBehaviorsUpdate(frameArgs, token);
                    await ExecuteBehaviorsLateUpdate(frameArgs, token);

                    UpdatePerformanceStats(frameStartTime, deltaTime);
                    await FrameRateControl(frameStartTime, token);

                    _totalFrames++;
                }
            }
            catch (OperationCanceledException)
            {
                // 预期中的取消
            }
        }

        private static void ProcessFixedUpdateEvents()
        {
            while (_fixedUpdateEvents.TryTake(out var fixedEvent))
            {
                // 使用后回收对象
                _frameEventArgsPool.Return(fixedEvent);
            }
        }

        private static FrameEventArgs CreateFrameEventArgs(int deltaTime)
        {
            var frameArgs = _frameEventArgsPool.Get();
            frameArgs.DeltaTime = (int)(deltaTime * _timeScale);
            frameArgs.TotalTime = (int)_totalTimeMs;
            frameArgs.CurrentFPS = _currentFPS;
            frameArgs.TargetFPS = _targetFPS;
            frameArgs.Handled = false;
            return frameArgs;
        }

        #endregion

        #region 行为执行方法 - 优化缓存

        private static async Task ExecuteBehaviorsUpdate(FrameEventArgs frameArgs, CancellationToken token)
        {
            await ExecuteBehaviorsMethod(wrapper =>
            {
                lock (wrapper.LockObject)
                {
                    wrapper.Behavior.InvokeUpdate(frameArgs);
                }
            }, frameArgs, token);
        }

        private static async Task ExecuteBehaviorsLateUpdate(FrameEventArgs frameArgs, CancellationToken token)
        {
            await ExecuteBehaviorsMethod(wrapper =>
            {
                lock (wrapper.LockObject)
                {
                    wrapper.Behavior.InvokeLateUpdate(frameArgs);
                }
            }, frameArgs, token);
        }

        private static async Task ExecuteBehaviorsFixedUpdate(FrameEventArgs frameArgs, CancellationToken token)
        {
            await ExecuteBehaviorsMethod(wrapper =>
            {
                lock (wrapper.LockObject)
                {
                    wrapper.Behavior.InvokeFixedUpdate(frameArgs);
                }
            }, frameArgs, token);
        }

        private static async Task ExecuteBehaviorsMethod(Action<BehaviorWrapper> action,
            FrameEventArgs frameArgs, CancellationToken token)
        {
            if (_behaviors.IsEmpty || frameArgs.Handled || token.IsCancellationRequested)
                return;

            // 使用缓存的包装器数组，避免每次排序
            var wrappers = _cachedWrappers;
            if (wrappers.Length == 0 || _lastConfigCheckTime + 1000 < GetCurrentTimestamp())
            {
                UpdateCachedWrappers();
                wrappers = _cachedWrappers;
                _lastConfigCheckTime = GetCurrentTimestamp();
            }

            foreach (var wrapper in wrappers)
            {
                if (wrapper?.Behavior == null || frameArgs.Handled || token.IsCancellationRequested)
                    break;

                try
                {
                    await Task.Run(() => action(wrapper), token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in behavior execution: {ex.Message}");
                }
            }
        }

        private static void UpdateCachedWrappers()
        {
            _cachedWrappers = [.. _behaviors.Values.OrderBy(w => w.ExecutionOrder)];
        }

        #endregion

        #region 辅助方法 - 性能优化

        private static void ProcessMainThreadOperations()
        {
            // 处理主线程队列
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in main thread operation: {ex.Message}");
                }
            }

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
                    _cachedTargetFrameTime = 1000.0 / _targetFPS;
                }
                if (config.FixedUpdateInterval.HasValue) _fixedUpdateInterval = config.FixedUpdateInterval.Value;
                if (config.TimeScale.HasValue) _timeScale = config.TimeScale.Value;
                if (config.PauseState.HasValue) _isPaused = config.PauseState.Value;

                _configRequestPool.Return(config);
            }
        }

        private static void ProcessAddedBehaviors()
        {
            while (_addQueue.TryDequeue(out var behavior))
            {
                if (behavior == null) continue;

                BehaviorWrapper wrapper;
                if (_wrapperPool.TryDequeue(out var pooledWrapper))
                {
                    wrapper = pooledWrapper;
                }
                else
                {
                    wrapper = new BehaviorWrapper(behavior, Interlocked.Increment(ref _instanceCounter));
                }

                _behaviors[GetBehaviorHash(behavior)] = wrapper;

                SafeExecute(behavior.InvokeAwake, behavior);
                SafeExecute(behavior.InvokeStart, behavior);

                UpdateCachedWrappers();
            }
        }

        private static void ProcessRemovedBehaviors()
        {
            while (_removeQueue.TryDequeue(out var behavior))
            {
                if (behavior == null) continue;
                if (_behaviors.TryRemove(GetBehaviorHash(behavior), out var wrapper))
                {
                    if (_wrapperPool.Count < 100)
                        _wrapperPool.Enqueue(wrapper);
                }
                UpdateCachedWrappers();
            }
        }

        private static int GetBehaviorHash(IMonoBehaviour behavior)
        {
            return RuntimeHelpers.GetHashCode(behavior);
        }

        private static long GetCurrentTimestamp()
        {
            return Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
        }

        private static int CalculateDeltaTime(long currentTime)
        {
            if (_lastFrameTime <= 0) return 1;
            var delta = (int)(currentTime - _lastFrameTime);
            return Math.Max(1, delta);
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

        private static async Task FrameRateControl(long frameStartTime, CancellationToken token)
        {
            var elapsed = GetCurrentTimestamp() - frameStartTime;

            if (elapsed < _cachedTargetFrameTime)
            {
                var sleepTime = (int)(_cachedTargetFrameTime - elapsed);
                if (sleepTime > 0) await Task.Delay(sleepTime, token);
            }
        }

        private static void SafeExecute(Action action, IMonoBehaviour behavior)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in behavior {behavior?.GetType().Name}: {ex.Message}");
            }
        }

        #endregion

        #region 其他方法

        public static void TogglePause()
        {
            if (_isPaused) Resume(); else Pause();
        }

        public static void Restart()
        {
            Stop();
            // 添加短暂延迟确保完全停止
            Task.Delay(10).ContinueWith(_ => Start());
        }

        #endregion
    }
}