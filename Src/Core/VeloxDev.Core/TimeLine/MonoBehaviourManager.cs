using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VeloxDev.Core.Interfaces.MonoBehaviour;

namespace VeloxDev.Core.TimeLine
{
    public static class MonoBehaviourManager
    {
        #region 常量定义

        private const int MIN_SLEEP_MS = 1;
        private const int DEFAULT_PAUSE_DELAY_MS = 10;
        private const int DEFAULT_RESTART_CHECK_INTERVAL_MS = 5;
        private const int RESTART_SHUTDOWN_TIMEOUT_MS = 1000;
        private const int RESTART_QUEUE_CLEAR_TIMEOUT_MS = 500;
        private const int THREAD_INACTIVITY_TIMEOUT_MS = 2000;

        private const int MIN_FPS = 1;
        private const int MAX_FPS = 1000;
        private const int DEFAULT_TARGET_FPS = 60;
        private const int DEFAULT_FIXED_UPDATE_INTERVAL_MS = 16;

        private const int MAX_WRAPPER_POOL_SIZE = 100;
        private const int DEFAULT_OBJECT_POOL_SIZE = 50;
        private const int MAX_CONFIG_CACHE_DURATION_MS = 1000;

        private const float MIN_TIME_SCALE = 0f;
        private const float MAX_TIME_SCALE = 10f;
        private const float DEFAULT_TIME_SCALE = 1.0f;
        private const int MIN_UPDATE_INTERVAL_MS = 1;
        private const int MAX_UPDATE_INTERVAL_MS = 1000;

        #endregion

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
        }

        private class ObjectPool<T>(int maxSize) where T : class, new()
        {
            private readonly ConcurrentQueue<T> _pool = new();
            private readonly int _maxSize = maxSize;

            public T Get()
            {
                return _pool.TryDequeue(out var item) ? item : new T();
            }

            public void Return(T item)
            {
                if (_pool.Count < _maxSize)
                {
                    _pool.Enqueue(item);
                }
            }
        }

        #endregion

        #region 私有字段

        private static readonly ConcurrentDictionary<int, BehaviorWrapper> _behaviors = new();
        private static readonly ConcurrentQueue<IMonoBehaviour> _addQueue = new();
        private static readonly ConcurrentQueue<IMonoBehaviour> _removeQueue = new();
        private static readonly ConcurrentQueue<ConfigChangeRequest> _configQueue = new();
        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private static readonly BlockingCollection<FrameEventArgs> _fixedUpdateEvents = [];

        private static volatile bool _isRunning = false;
        private static volatile bool _isPaused = false;
        private static float _timeScale = DEFAULT_TIME_SCALE;
        private static int _targetFPS = DEFAULT_TARGET_FPS;
        private static int _fixedUpdateInterval = DEFAULT_FIXED_UPDATE_INTERVAL_MS;

        private static long _totalTimeMs = 0;
        private static long _lastFrameTime = 0;
        private static int _currentFPS = 0;
        private static int _fpsCounter = 0;
        private static long _fpsLastUpdateTime = 0;
        private static long _totalFrames = 0;
        private static int _instanceCounter = 0;

        private static CancellationTokenSource _mainCancellationTokenSource = new();
        private static Task? _updateTask;
        private static Task? _fixedUpdateTask;

        private static volatile bool _isUpdateThreadActive = false;
        private static volatile bool _isFixedUpdateThreadActive = false;
        private static long _updateThreadLastActivity = 0;
        private static long _fixedUpdateThreadLastActivity = 0;
        private static readonly object _threadStatusLock = new();

        private static readonly ObjectPool<FrameEventArgs> _frameEventArgsPool = new(DEFAULT_OBJECT_POOL_SIZE);
        private static readonly ObjectPool<ConfigChangeRequest> _configRequestPool = new(DEFAULT_OBJECT_POOL_SIZE);
        private static readonly ConcurrentQueue<BehaviorWrapper> _wrapperPool = new();

        private static double _cachedTargetFrameTime = 1000.0 / DEFAULT_TARGET_FPS;
        private static long _lastConfigCheckTime = 0;
        private static BehaviorWrapper[] _cachedWrappers = [];

        private static readonly Stopwatch _frameTimer = new();
        private static readonly long _minSleepThreshold = TimeSpan.FromMilliseconds(MIN_SLEEP_MS).Ticks;

        public static event EventHandler? OnSystemStarted;
        public static event EventHandler? OnSystemPaused;
        public static event EventHandler? OnSystemResumed;
        public static event EventHandler? OnSystemStopped;

        #endregion

        #region 公共属性

        public static bool IsRunning => _isRunning;
        public static bool IsPaused => _isPaused;
        public static int CurrentFPS => _currentFPS;
        public static int TargetFPS => _targetFPS;
        public static long TotalTimeMs => _totalTimeMs;
        public static long TotalFrames => _totalFrames;
        public static int ActiveBehaviorCount => _behaviors.Count;
        public static float TimeScale => _timeScale;

        public static string SystemStatus => !_isRunning ? "Stopped" : _isPaused ? "Paused" : "Running";

        public static bool IsUpdateThreadAlive => _isRunning && _isUpdateThreadActive &&
            (GetCurrentTimestamp() - _updateThreadLastActivity) < THREAD_INACTIVITY_TIMEOUT_MS;

        public static bool IsFixedUpdateThreadAlive => _isRunning && _isFixedUpdateThreadActive &&
            (GetCurrentTimestamp() - _fixedUpdateThreadLastActivity) < THREAD_INACTIVITY_TIMEOUT_MS;

        #endregion

        #region 配置API

        public static void SetTargetFPS(int fps)
        {
            if (fps < MIN_FPS || fps > MAX_FPS) return;
            var request = _configRequestPool.Get();
            request.TargetFPS = fps;
            _configQueue.Enqueue(request);
            _cachedTargetFrameTime = 1000.0 / fps;
        }

        public static void SetFixedUpdateInterval(int intervalMs)
        {
            if (intervalMs < MIN_UPDATE_INTERVAL_MS || intervalMs > MAX_UPDATE_INTERVAL_MS) return;
            var request = _configRequestPool.Get();
            request.FixedUpdateInterval = intervalMs;
            _configQueue.Enqueue(request);
        }

        public static void SetTimeScale(float timeScale)
        {
            if (timeScale < MIN_TIME_SCALE) timeScale = MIN_TIME_SCALE;
            if (timeScale > MAX_TIME_SCALE) timeScale = MAX_TIME_SCALE;
            var request = _configRequestPool.Get();
            request.TimeScale = timeScale;
            _configQueue.Enqueue(request);
        }

        public static void ExecuteOnMainThread(Action action) => _mainThreadQueue.Enqueue(action);

        #endregion

        #region 生命周期管理

        public static void Start()
        {
            if (_isRunning) return;

            lock (_threadStatusLock)
            {
                _isRunning = true;
                _isPaused = false;
                _isUpdateThreadActive = false;
                _isFixedUpdateThreadActive = false;
                _updateThreadLastActivity = 0;
                _fixedUpdateThreadLastActivity = 0;

                _mainCancellationTokenSource = new CancellationTokenSource();
                _lastFrameTime = GetCurrentTimestamp();
                _fpsLastUpdateTime = _lastFrameTime;
                _frameTimer.Restart();

                UpdateCachedWrappers();

                _fixedUpdateTask = Task.Run(() => FixedUpdateLoop(_mainCancellationTokenSource.Token), _mainCancellationTokenSource.Token);
                _updateTask = Task.Run(() => UpdateLoop(_mainCancellationTokenSource.Token), _mainCancellationTokenSource.Token);

                OnSystemStarted?.Invoke(null, EventArgs.Empty);
            }
        }

        public static async Task StopAsync()
        {
            if (!_isRunning) return;

            lock (_threadStatusLock)
            {
                _isRunning = false;
                _isPaused = false;
                _isUpdateThreadActive = false;
                _isFixedUpdateThreadActive = false;
            }

            _mainCancellationTokenSource.Cancel();
            _frameTimer.Stop();

            try
            {
                var stopTasks = new List<Task>();
                if (_updateTask != null) stopTasks.Add(_updateTask);
                if (_fixedUpdateTask != null) stopTasks.Add(_fixedUpdateTask);

                if (stopTasks.Count > 0)
                    await Task.WhenAll(stopTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _updateTask = null;
                _fixedUpdateTask = null;
                ResetStatistics();
                ClearQueues();
            }

            OnSystemStopped?.Invoke(null, EventArgs.Empty);
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

        public static async Task RestartAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            await ExecuteRestartAsync().ConfigureAwait(false);

            if (stopwatch.ElapsedMilliseconds > 5)
            {
                Debug.WriteLine($"Smart restart completed in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        public static void RegisterBehaviour(IMonoBehaviour behavior) => ExecuteOnMainThread(() =>
        {
            if (behavior != null) _addQueue.Enqueue(behavior);
        });

        public static void UnregisterBehaviour(IMonoBehaviour behavior) => ExecuteOnMainThread(() =>
        {
            if (behavior != null) _removeQueue.Enqueue(behavior);
        });

        #endregion

        #region 重启机制

        private static async Task ExecuteRestartAsync()
        {
            // 阶段1: 请求停止
            await StopAsync();

            // 阶段2: 等待系统完全停止
            var shutdownConfirmed = await WaitForConditionAsync(
                timeout: TimeSpan.FromMilliseconds(RESTART_SHUTDOWN_TIMEOUT_MS),
                checkInterval: TimeSpan.FromMilliseconds(DEFAULT_RESTART_CHECK_INTERVAL_MS),
                condition: () => !_isRunning && !_isUpdateThreadActive && !_isFixedUpdateThreadActive
            ).ConfigureAwait(false);

            if (!shutdownConfirmed)
            {
                Debug.WriteLine($"Warning: Force restarting after timeout");
                ForceCleanup();
            }

            // 阶段3: 验证所有队列已清空
            await WaitForConditionAsync(
                timeout: TimeSpan.FromMilliseconds(RESTART_QUEUE_CLEAR_TIMEOUT_MS),
                checkInterval: TimeSpan.FromMilliseconds(DEFAULT_RESTART_CHECK_INTERVAL_MS / 2),
                condition: () => _addQueue.IsEmpty && _removeQueue.IsEmpty &&
                               _configQueue.IsEmpty && _mainThreadQueue.IsEmpty
            ).ConfigureAwait(false);

            // 阶段4: 重新启动
            Start();
        }

        private static async Task<bool> WaitForConditionAsync(TimeSpan timeout, TimeSpan checkInterval, Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            var spinWait = new SpinWait();

            // 完全自旋等待，不进行任何线程切换
            while (stopwatch.Elapsed < timeout)
            {
                if (condition()) return true;

                // 使用优化的自旋策略替代Task.Delay
                var spinStart = Stopwatch.GetTimestamp();
                var targetTicks = spinStart + (long)(checkInterval.TotalSeconds * Stopwatch.Frequency);

                while (Stopwatch.GetTimestamp() < targetTicks)
                {
                    if (condition()) return true;
                    spinWait.SpinOnce();
                }
            }

            return false;
        }

        private static void ForceCleanup()
        {
            _mainCancellationTokenSource?.Cancel();
            _mainCancellationTokenSource?.Dispose();
            _mainCancellationTokenSource = new CancellationTokenSource();

            _updateTask = null;
            _fixedUpdateTask = null;
            _isRunning = false;
            _isUpdateThreadActive = false;
            _isFixedUpdateThreadActive = false;

            // 清空所有队列
            ClearQueues();
        }

        #endregion

        #region 主循环

        private static async Task FixedUpdateLoop(CancellationToken token)
        {
            lock (_threadStatusLock)
            {
                _isFixedUpdateThreadActive = true;
                _fixedUpdateThreadLastActivity = GetCurrentTimestamp();
            }

            long lastFixedUpdateTime = GetCurrentTimestamp();

            try
            {
                while (_isRunning && !token.IsCancellationRequested)
                {
                    lock (_threadStatusLock) { _fixedUpdateThreadLastActivity = GetCurrentTimestamp(); }

                    if (_isPaused)
                    {
                        await HighPrecisionSpinWait(DEFAULT_PAUSE_DELAY_MS, token).ConfigureAwait(false);
                        continue;
                    }

                    var currentTime = GetCurrentTimestamp();
                    var elapsed = currentTime - lastFixedUpdateTime;

                    if (elapsed >= _fixedUpdateInterval)
                    {
                        var fixedFrameArgs = CreateFixedFrameEventArgs((int)elapsed);
                        await ExecuteBehaviorsFixedUpdate(fixedFrameArgs, token).ConfigureAwait(false);

                        if (!fixedFrameArgs.Handled)
                            _fixedUpdateEvents.Add(fixedFrameArgs, token);

                        lastFixedUpdateTime = currentTime;
                    }

                    var nextUpdateTime = lastFixedUpdateTime + _fixedUpdateInterval;
                    var waitTime = nextUpdateTime - GetCurrentTimestamp();
                    if (waitTime > 0)
                        await HighPrecisionSpinWait((int)waitTime, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_threadStatusLock) { _isFixedUpdateThreadActive = false; }
            }
        }

        private static async Task UpdateLoop(CancellationToken token)
        {
            lock (_threadStatusLock)
            {
                _isUpdateThreadActive = true;
                _updateThreadLastActivity = GetCurrentTimestamp();
            }

            try
            {
                while (_isRunning && !token.IsCancellationRequested)
                {
                    lock (_threadStatusLock) { _updateThreadLastActivity = GetCurrentTimestamp(); }

                    if (_isPaused)
                    {
                        await HighPrecisionSpinWait(DEFAULT_PAUSE_DELAY_MS, token).ConfigureAwait(false);
                        continue;
                    }

                    var frameStartTime = GetCurrentTimestamp();
                    ProcessMainThreadOperations();

                    var deltaTime = CalculateDeltaTime(frameStartTime);
                    if (deltaTime <= 0)
                    {
                        await HighPrecisionSpinWait(MIN_SLEEP_MS, token).ConfigureAwait(false);
                        continue;
                    }

                    var frameArgs = CreateFrameEventArgs(deltaTime);
                    ProcessFixedUpdateEvents();

                    await ExecuteBehaviorsUpdate(frameArgs, token).ConfigureAwait(false);
                    await ExecuteBehaviorsLateUpdate(frameArgs, token).ConfigureAwait(false);

                    UpdatePerformanceStats(frameStartTime, deltaTime);
                    await FrameRateControl(frameStartTime, token).ConfigureAwait(false);
                    _totalFrames++;
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_threadStatusLock) { _isUpdateThreadActive = false; }
            }
        }

        #endregion

        #region 行为执行

        private static Task ExecuteBehaviorsUpdate(FrameEventArgs frameArgs, CancellationToken token) =>
            ExecuteBehaviorsMethod(wrapper =>
            {
                lock (wrapper.LockObject)
                {
                    wrapper.Behavior.InvokeUpdate(frameArgs);
                }
            }, frameArgs, token);

        private static Task ExecuteBehaviorsLateUpdate(FrameEventArgs frameArgs, CancellationToken token) =>
            ExecuteBehaviorsMethod(wrapper =>
            {
                lock (wrapper.LockObject)
                {
                    wrapper.Behavior.InvokeLateUpdate(frameArgs);
                }
            }, frameArgs, token);

        private static Task ExecuteBehaviorsFixedUpdate(FrameEventArgs frameArgs, CancellationToken token) =>
            ExecuteBehaviorsMethod(wrapper =>
            {
                lock (wrapper.LockObject)
                {
                    wrapper.Behavior.InvokeFixedUpdate(frameArgs);
                }
            }, frameArgs, token);

        private static Task ExecuteBehaviorsMethod(Action<BehaviorWrapper> action, FrameEventArgs frameArgs, CancellationToken token)
        {
            if (_behaviors.IsEmpty || frameArgs.Handled || token.IsCancellationRequested)
                return Task.CompletedTask;

            var wrappers = _cachedWrappers;
            if (wrappers.Length == 0 || _lastConfigCheckTime + MAX_CONFIG_CACHE_DURATION_MS < GetCurrentTimestamp())
            {
                UpdateCachedWrappers();
                wrappers = _cachedWrappers;
                _lastConfigCheckTime = GetCurrentTimestamp();
            }

            foreach (var wrapper in wrappers)
            {
                if (wrapper?.Behavior == null || frameArgs.Handled || token.IsCancellationRequested)
                    break;

                action(wrapper);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region 辅助方法

        private static void ProcessMainThreadOperations()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); } catch (Exception ex) { Debug.WriteLine($"Main thread error: {ex.Message}"); }
            }

            ProcessConfigChanges();
            ProcessAddedBehaviors();
            ProcessRemovedBehaviors();
        }

        private static void ProcessConfigChanges()
        {
            while (_configQueue.TryDequeue(out var config))
            {
                if (config.TargetFPS.HasValue) _targetFPS = config.TargetFPS.Value;
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

                var wrapper = _wrapperPool.TryDequeue(out var pooled) ? pooled :
                    new BehaviorWrapper(behavior, Interlocked.Increment(ref _instanceCounter));

                _behaviors[GetBehaviorHash(behavior)] = wrapper;
                SafeExecute(behavior.InvokeAwake);
                SafeExecute(behavior.InvokeStart);
                UpdateCachedWrappers();
            }
        }

        private static void ProcessRemovedBehaviors()
        {
            while (_removeQueue.TryDequeue(out var behavior))
            {
                if (behavior != null && _behaviors.TryRemove(GetBehaviorHash(behavior), out var wrapper))
                {
                    if (_wrapperPool.Count < MAX_WRAPPER_POOL_SIZE) _wrapperPool.Enqueue(wrapper);
                }
                UpdateCachedWrappers();
            }
        }

        private static FrameEventArgs CreateFixedFrameEventArgs(int deltaTime) => CreateFrameEventArgs(deltaTime);

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

        private static void ProcessFixedUpdateEvents()
        {
            while (_fixedUpdateEvents.TryTake(out var fixedEvent))
                _frameEventArgsPool.Return(fixedEvent);
        }

        private static async Task FrameRateControl(long frameStartTime, CancellationToken token)
        {
            var elapsed = GetCurrentTimestamp() - frameStartTime;
            if (elapsed < _cachedTargetFrameTime)
            {
                var sleepTime = (int)(_cachedTargetFrameTime - elapsed);
                if (sleepTime > 0) await HighPrecisionSpinWait(sleepTime, token).ConfigureAwait(false);
            }
        }

        private static Task HighPrecisionSpinWait(int milliseconds, CancellationToken token)
        {
            if (milliseconds <= 0 || token.IsCancellationRequested)
                return Task.CompletedTask;

            var targetTicks = _frameTimer.ElapsedTicks + (milliseconds * 10000);
            var spinWait = new SpinWait();

            // 纯自旋等待，不进行任何线程切换
            while (_frameTimer.ElapsedTicks < targetTicks)
            {
                if (token.IsCancellationRequested)
                    break;

                // 使用优化的自旋策略
                if (targetTicks - _frameTimer.ElapsedTicks > _minSleepThreshold * 10)
                {
                    // 长等待时适度降低CPU使用率
                    for (int i = 0; i < 50; i++)
                    {
                        if (_frameTimer.ElapsedTicks >= targetTicks || token.IsCancellationRequested)
                            break;
                        spinWait.SpinOnce();
                    }
                }
                else
                {
                    // 短等待时最高精度自旋
                    spinWait.SpinOnce();
                }
            }

            return Task.CompletedTask;
        }

        private static void UpdateCachedWrappers() => _cachedWrappers = [.. _behaviors.Values.OrderBy(w => w.ExecutionOrder)];
        private static int GetBehaviorHash(IMonoBehaviour behavior) => RuntimeHelpers.GetHashCode(behavior);
        private static long GetCurrentTimestamp() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

        private static int CalculateDeltaTime(long currentTime)
        {
            if (_lastFrameTime <= 0) return 1;
            return Math.Max(1, (int)(currentTime - _lastFrameTime));
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

        private static void SafeExecute(Action action)
        {
            try { action?.Invoke(); } catch (Exception ex) { Debug.WriteLine($"Behavior error: {ex.Message}"); }
        }

        private static void ResetStatistics()
        {
            _totalTimeMs = 0; _currentFPS = 0; _fpsCounter = 0;
            _fpsLastUpdateTime = 0; _totalFrames = 0; _lastFrameTime = 0;
            _lastConfigCheckTime = 0;
        }

        private static void ClearQueues()
        {
            while (_fixedUpdateEvents.TryTake(out var frameEvent))
                if (frameEvent is FrameEventArgs args) _frameEventArgsPool.Return(args);

            while (_configQueue.TryDequeue(out var config)) _configRequestPool.Return(config);
            while (_mainThreadQueue.TryDequeue(out _)) { }
        }

        #endregion

        #region 其他方法

        public static void TogglePause() { if (_isPaused) Resume(); else Pause(); }

        #endregion
    }
}