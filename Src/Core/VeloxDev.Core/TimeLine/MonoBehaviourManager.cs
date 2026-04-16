using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VeloxDev.MonoBehaviour;

namespace VeloxDev.TimeLine
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

        private const int DEFAULT_OBJECT_POOL_SIZE = 50;
        private const int MAX_CONFIG_CACHE_DURATION_MS = 1000;

        private const float MIN_TIME_SCALE = 0f;
        private const float MAX_TIME_SCALE = 10f;
        private const float DEFAULT_TIME_SCALE = 1.0f;
        private const int MIN_UPDATE_INTERVAL_MS = 1;
        private const int MAX_UPDATE_INTERVAL_MS = 1000;

        // 自旋阈值：低于此毫秒数用纯自旋，高于则 Thread.Sleep(1) + 尾部自旋
        private const int SPIN_ONLY_THRESHOLD_MS = 2;

        #endregion

        #region 内部类

        private sealed class BehaviorWrapper
        {
            public IMonoBehaviour? Behavior;
            public int ExecutionOrder;
            public volatile bool IsActive;

            public void Reset(IMonoBehaviour behavior, int order)
            {
                Behavior = behavior;
                ExecutionOrder = order;
                IsActive = true;
            }

            public void Clear()
            {
                Behavior = null;
                IsActive = false;
            }
        }

        private sealed class ConfigChangeRequest
        {
            public int? TargetFPS;
            public int? FixedUpdateInterval;
            public float? TimeScale;
            public bool? PauseState;

            public void Reset()
            {
                TargetFPS = null;
                FixedUpdateInterval = null;
                TimeScale = null;
                PauseState = null;
            }
        }

        private sealed class ObjectPool<T>(int maxSize) where T : class, new()
        {
            private readonly ConcurrentStack<T> _pool = new();
            private int _count;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Get()
            {
                if (_pool.TryPop(out var item))
                {
                    Interlocked.Decrement(ref _count);
                    return item;
                }
                return new T();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Return(T item)
            {
                if (Interlocked.Increment(ref _count) <= maxSize)
                {
                    _pool.Push(item);
                }
                else
                {
                    Interlocked.Decrement(ref _count);
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

        // FixedUpdate 事件用无锁环形缓冲替代 BlockingCollection
        private static readonly ConcurrentQueue<FrameEventArgs> _fixedUpdateEvents = new();

        private static volatile bool _isRunning;
        private static volatile bool _isPaused;

        // 使用 Interlocked 安全读写的字段，通过 long/int 包装 float
        private static long _timeScaleBits = BitConverter.DoubleToInt64Bits(DEFAULT_TIME_SCALE);
        private static int _targetFPS = DEFAULT_TARGET_FPS;
        private static int _fixedUpdateInterval = DEFAULT_FIXED_UPDATE_INTERVAL_MS;

        private static long _totalTimeMs;
        private static long _lastFrameTime;
        private static int _currentFPS;
        private static int _fpsCounter;
        private static long _fpsLastUpdateTime;
        private static long _totalFrames;
        private static int _instanceCounter;

        private static CancellationTokenSource _mainCancellationTokenSource = new();
        private static Thread? _updateThread;
        private static Thread? _fixedUpdateThread;

        private static volatile bool _isUpdateThreadActive;
        private static volatile bool _isFixedUpdateThreadActive;
        private static long _updateThreadLastActivity;
        private static long _fixedUpdateThreadLastActivity;

        private static readonly ObjectPool<FrameEventArgs> _frameEventArgsPool = new(DEFAULT_OBJECT_POOL_SIZE);
        private static readonly ObjectPool<ConfigChangeRequest> _configRequestPool = new(DEFAULT_OBJECT_POOL_SIZE);
        private static readonly ObjectPool<BehaviorWrapper> _wrapperPool = new(DEFAULT_OBJECT_POOL_SIZE);

        private static double _cachedTargetFrameTime = 1000.0 / DEFAULT_TARGET_FPS;
        private static long _lastConfigCheckTime;

        // 缓存的排序包装器数组 — volatile 确保跨线程可见性
        private static volatile BehaviorWrapper[] _cachedWrappers = [];
        private static volatile bool _wrappersNeedSort;

        private static readonly Stopwatch _frameTimer = new();
        private static readonly long _ticksPerMs = Stopwatch.Frequency / 1000;

        public static event EventHandler? OnSystemStarted;
        public static event EventHandler? OnSystemPaused;
        public static event EventHandler? OnSystemResumed;
        public static event EventHandler? OnSystemStopped;

        #endregion

        #region 公共属性

        public static bool IsRunning => _isRunning;
        public static bool IsPaused => _isPaused;
        public static int CurrentFPS => _currentFPS;
        public static int TargetFPS => Volatile.Read(ref _targetFPS);
        public static long TotalTimeMs => Interlocked.Read(ref _totalTimeMs);
        public static long TotalFrames => Interlocked.Read(ref _totalFrames);
        public static int ActiveBehaviorCount => _behaviors.Count;
        public static float TimeScale => (float)BitConverter.Int64BitsToDouble(Interlocked.Read(ref _timeScaleBits));

        public static string SystemStatus => !_isRunning ? "Stopped" : _isPaused ? "Paused" : "Running";

        public static bool IsUpdateThreadAlive => _isRunning && _isUpdateThreadActive &&
            (GetCurrentTimestamp() - Interlocked.Read(ref _updateThreadLastActivity)) < THREAD_INACTIVITY_TIMEOUT_MS;

        public static bool IsFixedUpdateThreadAlive => _isRunning && _isFixedUpdateThreadActive &&
            (GetCurrentTimestamp() - Interlocked.Read(ref _fixedUpdateThreadLastActivity)) < THREAD_INACTIVITY_TIMEOUT_MS;

        #endregion

        #region 配置API

        public static void SetTargetFPS(int fps)
        {
            if (fps < MIN_FPS || fps > MAX_FPS) return;
            var request = _configRequestPool.Get();
            request.Reset();
            request.TargetFPS = fps;
            _configQueue.Enqueue(request);
        }

        public static void SetFixedUpdateInterval(int intervalMs)
        {
            if (intervalMs < MIN_UPDATE_INTERVAL_MS || intervalMs > MAX_UPDATE_INTERVAL_MS) return;
            var request = _configRequestPool.Get();
            request.Reset();
            request.FixedUpdateInterval = intervalMs;
            _configQueue.Enqueue(request);
        }

        public static void SetTimeScale(float timeScale)
        {
            if (timeScale < MIN_TIME_SCALE) timeScale = MIN_TIME_SCALE;
            if (timeScale > MAX_TIME_SCALE) timeScale = MAX_TIME_SCALE;
            var request = _configRequestPool.Get();
            request.Reset();
            request.TimeScale = timeScale;
            _configQueue.Enqueue(request);
        }

        public static void ExecuteOnMainThread(Action action) => _mainThreadQueue.Enqueue(action);

        #endregion

        #region 生命周期管理

        public static void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _isPaused = false;
            _isUpdateThreadActive = false;
            _isFixedUpdateThreadActive = false;
            Interlocked.Exchange(ref _updateThreadLastActivity, 0);
            Interlocked.Exchange(ref _fixedUpdateThreadLastActivity, 0);

            _mainCancellationTokenSource = new CancellationTokenSource();
            _lastFrameTime = GetCurrentTimestamp();
            _fpsLastUpdateTime = _lastFrameTime;
            _frameTimer.Restart();

            RebuildCachedWrappers();

            var token = _mainCancellationTokenSource.Token;

            _fixedUpdateThread = new Thread(() => FixedUpdateLoop(token))
            {
                Name = "VeloxDev.FixedUpdate",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };

            _updateThread = new Thread(() => UpdateLoop(token))
            {
                Name = "VeloxDev.Update",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };

            _fixedUpdateThread.Start();
            _updateThread.Start();

            OnSystemStarted?.Invoke(null, EventArgs.Empty);
        }

        public static async Task StopAsync()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _isPaused = false;
            _isUpdateThreadActive = false;
            _isFixedUpdateThreadActive = false;

            _mainCancellationTokenSource.Cancel();
            _frameTimer.Stop();

            try
            {
                // 等待线程退出，设置合理超时
                await Task.Run(() =>
                {
                    _updateThread?.Join(RESTART_SHUTDOWN_TIMEOUT_MS);
                    _fixedUpdateThread?.Join(RESTART_SHUTDOWN_TIMEOUT_MS);
                }).ConfigureAwait(false);
            }
            catch (Exception) { /* 静默处理 */ }
            finally
            {
                _updateThread = null;
                _fixedUpdateThread = null;
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

        public static void RegisterBehaviour(IMonoBehaviour behavior)
        {
            if (behavior != null) _addQueue.Enqueue(behavior);
        }

        public static void UnregisterBehaviour(IMonoBehaviour behavior)
        {
            if (behavior != null) _removeQueue.Enqueue(behavior);
        }

        #endregion

        #region 重启机制

        private static async Task ExecuteRestartAsync()
        {
            await StopAsync().ConfigureAwait(false);

            var shutdownConfirmed = await WaitForConditionAsync(
                timeout: TimeSpan.FromMilliseconds(RESTART_SHUTDOWN_TIMEOUT_MS),
                checkInterval: TimeSpan.FromMilliseconds(DEFAULT_RESTART_CHECK_INTERVAL_MS),
                condition: () => !_isRunning && !_isUpdateThreadActive && !_isFixedUpdateThreadActive
            ).ConfigureAwait(false);

            if (!shutdownConfirmed)
            {
                Debug.WriteLine("Warning: Force restarting after timeout");
                ForceCleanup();
            }

            await WaitForConditionAsync(
                timeout: TimeSpan.FromMilliseconds(RESTART_QUEUE_CLEAR_TIMEOUT_MS),
                checkInterval: TimeSpan.FromMilliseconds(DEFAULT_RESTART_CHECK_INTERVAL_MS / 2),
                condition: () => _addQueue.IsEmpty && _removeQueue.IsEmpty &&
                               _configQueue.IsEmpty && _mainThreadQueue.IsEmpty
            ).ConfigureAwait(false);

            Start();
        }

        private static async Task<bool> WaitForConditionAsync(TimeSpan timeout, TimeSpan checkInterval, Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                if (condition()) return true;
                // 重启不在热路径中，用 Task.Delay 即可，避免 CPU 浪费
                await Task.Delay((int)Math.Max(1, checkInterval.TotalMilliseconds)).ConfigureAwait(false);
            }

            return false;
        }

        private static void ForceCleanup()
        {
            _mainCancellationTokenSource?.Cancel();
            _mainCancellationTokenSource?.Dispose();
            _mainCancellationTokenSource = new CancellationTokenSource();

            _updateThread = null;
            _fixedUpdateThread = null;
            _isRunning = false;
            _isUpdateThreadActive = false;
            _isFixedUpdateThreadActive = false;

            ClearQueues();
        }

        #endregion

        #region 主循环

        private static void FixedUpdateLoop(CancellationToken token)
        {
            _isFixedUpdateThreadActive = true;
            Interlocked.Exchange(ref _fixedUpdateThreadLastActivity, GetCurrentTimestamp());

            long lastFixedUpdateTime = GetCurrentTimestamp();

            try
            {
                while (_isRunning && !token.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _fixedUpdateThreadLastActivity, GetCurrentTimestamp());

                    if (_isPaused)
                    {
                        PrecisionSleep(DEFAULT_PAUSE_DELAY_MS, token);
                        continue;
                    }

                    var currentTime = GetCurrentTimestamp();
                    var interval = Volatile.Read(ref _fixedUpdateInterval);
                    var elapsed = currentTime - lastFixedUpdateTime;

                    if (elapsed >= interval)
                    {
                        var fixedFrameArgs = CreateFrameEventArgs((int)elapsed);
                        ExecuteBehaviorsFixedUpdateSync(fixedFrameArgs, token);

                        if (!fixedFrameArgs.Handled)
                            _fixedUpdateEvents.Enqueue(fixedFrameArgs);
                        else
                            _frameEventArgsPool.Return(fixedFrameArgs);

                        lastFixedUpdateTime = currentTime;
                    }

                    var nextUpdateTime = lastFixedUpdateTime + interval;
                    var waitTime = (int)(nextUpdateTime - GetCurrentTimestamp());
                    if (waitTime > 0)
                        PrecisionSleep(waitTime, token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _isFixedUpdateThreadActive = false;
            }
        }

        private static void UpdateLoop(CancellationToken token)
        {
            _isUpdateThreadActive = true;
            Interlocked.Exchange(ref _updateThreadLastActivity, GetCurrentTimestamp());

            try
            {
                while (_isRunning && !token.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _updateThreadLastActivity, GetCurrentTimestamp());

                    if (_isPaused)
                    {
                        PrecisionSleep(DEFAULT_PAUSE_DELAY_MS, token);
                        continue;
                    }

                    var frameStartTime = GetCurrentTimestamp();
                    ProcessMainThreadOperations();

                    var deltaTime = CalculateDeltaTime(frameStartTime);
                    if (deltaTime <= 0)
                    {
                        PrecisionSleep(MIN_SLEEP_MS, token);
                        continue;
                    }

                    var frameArgs = CreateFrameEventArgs(deltaTime);
                    DrainFixedUpdateEvents();

                    ExecuteBehaviorsUpdateSync(frameArgs, token);
                    ExecuteBehaviorsLateUpdateSync(frameArgs, token);

                    _frameEventArgsPool.Return(frameArgs);

                    UpdatePerformanceStats(frameStartTime, deltaTime);
                    FrameRateControlSync(frameStartTime, token);
                    Interlocked.Increment(ref _totalFrames);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _isUpdateThreadActive = false;
            }
        }

        #endregion

        #region 行为执行

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExecuteBehaviorsUpdateSync(FrameEventArgs frameArgs, CancellationToken token)
        {
            var wrappers = GetCachedWrappers();
            for (int i = 0; i < wrappers.Length; i++)
            {
                if (frameArgs.Handled || token.IsCancellationRequested) break;
                var w = wrappers[i];
                if (w is { IsActive: true, Behavior: not null })
                {
                    try { w.Behavior.InvokeUpdate(frameArgs); }
                    catch (Exception ex) { Debug.WriteLine($"Update error: {ex.Message}"); }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExecuteBehaviorsLateUpdateSync(FrameEventArgs frameArgs, CancellationToken token)
        {
            var wrappers = GetCachedWrappers();
            for (int i = 0; i < wrappers.Length; i++)
            {
                if (frameArgs.Handled || token.IsCancellationRequested) break;
                var w = wrappers[i];
                if (w is { IsActive: true, Behavior: not null })
                {
                    try { w.Behavior.InvokeLateUpdate(frameArgs); }
                    catch (Exception ex) { Debug.WriteLine($"LateUpdate error: {ex.Message}"); }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExecuteBehaviorsFixedUpdateSync(FrameEventArgs frameArgs, CancellationToken token)
        {
            var wrappers = GetCachedWrappers();
            for (int i = 0; i < wrappers.Length; i++)
            {
                if (frameArgs.Handled || token.IsCancellationRequested) break;
                var w = wrappers[i];
                if (w is { IsActive: true, Behavior: not null })
                {
                    try { w.Behavior.InvokeFixedUpdate(frameArgs); }
                    catch (Exception ex) { Debug.WriteLine($"FixedUpdate error: {ex.Message}"); }
                }
            }
        }

        #endregion

        #region 辅助方法

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BehaviorWrapper[] GetCachedWrappers()
        {
            var currentTime = GetCurrentTimestamp();
            if (_wrappersNeedSort || Interlocked.Read(ref _lastConfigCheckTime) + MAX_CONFIG_CACHE_DURATION_MS < currentTime)
            {
                RebuildCachedWrappers();
                Interlocked.Exchange(ref _lastConfigCheckTime, currentTime);
            }
            return _cachedWrappers;
        }

        private static void ProcessMainThreadOperations()
        {
            // 限制每帧处理数量，防止卡顿
            int processed = 0;
            while (processed < 64 && _mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); } catch (Exception ex) { Debug.WriteLine($"Main thread error: {ex.Message}"); }
                processed++;
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
                    Volatile.Write(ref _targetFPS, config.TargetFPS.Value);
                    _cachedTargetFrameTime = 1000.0 / config.TargetFPS.Value;
                }
                if (config.FixedUpdateInterval.HasValue)
                    Volatile.Write(ref _fixedUpdateInterval, config.FixedUpdateInterval.Value);
                if (config.TimeScale.HasValue)
                    Interlocked.Exchange(ref _timeScaleBits, BitConverter.DoubleToInt64Bits(config.TimeScale.Value));
                if (config.PauseState.HasValue)
                    _isPaused = config.PauseState.Value;

                config.Reset();
                _configRequestPool.Return(config);
            }
        }

        private static void ProcessAddedBehaviors()
        {
            bool added = false;
            while (_addQueue.TryDequeue(out var behavior))
            {
                if (behavior == null) continue;

                var wrapper = _wrapperPool.Get();
                wrapper.Reset(behavior, Interlocked.Increment(ref _instanceCounter));

                _behaviors[GetBehaviorHash(behavior)] = wrapper;
                SafeExecute(behavior.InvokeAwake);
                SafeExecute(behavior.InvokeStart);
                added = true;
            }
            if (added) _wrappersNeedSort = true;
        }

        private static void ProcessRemovedBehaviors()
        {
            bool removed = false;
            while (_removeQueue.TryDequeue(out var behavior))
            {
                if (behavior != null && _behaviors.TryRemove(GetBehaviorHash(behavior), out var wrapper))
                {
                    wrapper.Clear();
                    _wrapperPool.Return(wrapper);
                    removed = true;
                }
            }
            if (removed) _wrappersNeedSort = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FrameEventArgs CreateFrameEventArgs(int deltaTime)
        {
            var ts = (float)BitConverter.Int64BitsToDouble(Interlocked.Read(ref _timeScaleBits));
            var frameArgs = _frameEventArgsPool.Get();
            frameArgs.DeltaTime = (int)(deltaTime * ts);
            frameArgs.TotalTime = (int)Interlocked.Read(ref _totalTimeMs);
            frameArgs.CurrentFPS = _currentFPS;
            frameArgs.TargetFPS = Volatile.Read(ref _targetFPS);
            frameArgs.Handled = false;
            return frameArgs;
        }

        private static void DrainFixedUpdateEvents()
        {
            while (_fixedUpdateEvents.TryDequeue(out var fixedEvent))
                _frameEventArgsPool.Return(fixedEvent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FrameRateControlSync(long frameStartTime, CancellationToken token)
        {
            var elapsed = GetCurrentTimestamp() - frameStartTime;
            var target = _cachedTargetFrameTime;
            if (elapsed < target)
            {
                var sleepTime = (int)(target - elapsed);
                if (sleepTime > 0) PrecisionSleep(sleepTime, token);
            }
        }

        /// <summary>
        /// 高精度睡眠：长等待用 Thread.Sleep(1) 节省 CPU，尾部自旋保精度
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PrecisionSleep(int milliseconds, CancellationToken token)
        {
            if (milliseconds <= 0) return;

            var targetTicks = _frameTimer.ElapsedTicks + (long)milliseconds * _ticksPerMs;

            // 长等待阶段：Thread.Sleep(1) 实际精度约 1-2ms，节约 CPU
            if (milliseconds > SPIN_ONLY_THRESHOLD_MS)
            {
                var sleepUntilTicks = targetTicks - SPIN_ONLY_THRESHOLD_MS * _ticksPerMs;
                while (_frameTimer.ElapsedTicks < sleepUntilTicks)
                {
                    if (token.IsCancellationRequested) return;
                    Thread.Sleep(1);
                }
            }

            // 尾部自旋：高精度等待最后 ~2ms
            var sw = new SpinWait();
            while (_frameTimer.ElapsedTicks < targetTicks)
            {
                if (token.IsCancellationRequested) return;
                sw.SpinOnce();
            }
        }

        private static void RebuildCachedWrappers()
        {
            var values = _behaviors.Values;
            var arr = new BehaviorWrapper[values.Count];
            int idx = 0;
            foreach (var v in values)
            {
                if (v is { IsActive: true })
                    arr[idx++] = v;
            }

            // 插入排序 — 行为数量通常很少，避免 LINQ 分配
            for (int i = 1; i < idx; i++)
            {
                var key = arr[i];
                int j = i - 1;
                while (j >= 0 && arr[j].ExecutionOrder > key.ExecutionOrder)
                {
                    arr[j + 1] = arr[j];
                    j--;
                }
                arr[j + 1] = key;
            }

            if (idx < arr.Length)
                Array.Resize(ref arr, idx);

            _cachedWrappers = arr;
            _wrappersNeedSort = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBehaviorHash(IMonoBehaviour behavior) => RuntimeHelpers.GetHashCode(behavior);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long GetCurrentTimestamp() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalculateDeltaTime(long currentTime)
        {
            var last = Volatile.Read(ref _lastFrameTime);
            if (last <= 0) return 1;
            return Math.Max(1, (int)(currentTime - last));
        }

        private static void UpdatePerformanceStats(long frameStartTime, int deltaTime)
        {
            Interlocked.Add(ref _totalTimeMs, deltaTime);
            Volatile.Write(ref _lastFrameTime, frameStartTime);
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
            Interlocked.Exchange(ref _totalTimeMs, 0);
            _currentFPS = 0;
            _fpsCounter = 0;
            _fpsLastUpdateTime = 0;
            Interlocked.Exchange(ref _totalFrames, 0);
            Volatile.Write(ref _lastFrameTime, 0);
            Interlocked.Exchange(ref _lastConfigCheckTime, 0);
        }

        private static void ClearQueues()
        {
            while (_fixedUpdateEvents.TryDequeue(out var args))
                _frameEventArgsPool.Return(args);

            while (_configQueue.TryDequeue(out var config))
            {
                config.Reset();
                _configRequestPool.Return(config);
            }

            while (_mainThreadQueue.TryDequeue(out _)) { }
            while (_addQueue.TryDequeue(out _)) { }
            while (_removeQueue.TryDequeue(out _)) { }
        }

        #endregion

        #region 其他方法

        public static void TogglePause() { if (_isPaused) Resume(); else Pause(); }

        #endregion
    }
}