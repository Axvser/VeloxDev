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

        public const string DEFAULT_CHANNEL = "default";

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
                    _pool.Push(item);
                else
                    Interlocked.Decrement(ref _count);
            }
        }

        #endregion

        #region LoopChannel

        private sealed class LoopChannel
        {
            public readonly string Name;

            private readonly ConcurrentDictionary<int, BehaviorWrapper> _behaviors = new();
            private readonly ConcurrentQueue<IMonoBehaviour> _addQueue = new();
            private readonly ConcurrentQueue<IMonoBehaviour> _removeQueue = new();
            private readonly ConcurrentQueue<ConfigChangeRequest> _configQueue = new();
            private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
            private readonly ConcurrentQueue<FrameEventArgs> _fixedUpdateEvents = new();

            private volatile bool _isRunning;
            private volatile bool _isPaused;
            private long _timeScaleBits = BitConverter.DoubleToInt64Bits(DEFAULT_TIME_SCALE);
            private int _targetFPS = DEFAULT_TARGET_FPS;
            private int _fixedUpdateInterval = DEFAULT_FIXED_UPDATE_INTERVAL_MS;
            private long _totalTimeMs;
            private long _lastFrameTime;
            private int _currentFPS;
            private int _fpsCounter;
            private long _fpsLastUpdateTime;
            private long _totalFrames;
            private int _instanceCounter;

            private CancellationTokenSource _cts = new();
            private Thread? _updateThread;
            private Thread? _fixedUpdateThread;
            private volatile bool _isUpdateThreadActive;
            private volatile bool _isFixedUpdateThreadActive;
            private long _updateThreadLastActivity;
            private long _fixedUpdateThreadLastActivity;

            private readonly ObjectPool<FrameEventArgs> _frameEventArgsPool = new(DEFAULT_OBJECT_POOL_SIZE);
            private readonly ObjectPool<ConfigChangeRequest> _configRequestPool = new(DEFAULT_OBJECT_POOL_SIZE);
            private readonly ObjectPool<BehaviorWrapper> _wrapperPool = new(DEFAULT_OBJECT_POOL_SIZE);
            private double _cachedTargetFrameTime = 1000.0 / DEFAULT_TARGET_FPS;
            private long _lastConfigCheckTime;
            private volatile BehaviorWrapper[] _cachedWrappers = [];
            private volatile bool _wrappersNeedSort;

            private readonly Stopwatch _frameTimer = new();
            private static readonly long _ticksPerMs = Stopwatch.Frequency / 1000;

            public event EventHandler? Started;
            public event EventHandler? Paused;
            public event EventHandler? Resumed;
            public event EventHandler? Stopped;

            public LoopChannel(string name) { Name = name; }

            #region 公共属性

            public bool IsRunning => _isRunning;
            public bool IsPaused => _isPaused;
            public int CurrentFPS => _currentFPS;
            public int TargetFPS => Volatile.Read(ref _targetFPS);
            public long TotalTimeMs => Interlocked.Read(ref _totalTimeMs);
            public long TotalFrames => Interlocked.Read(ref _totalFrames);
            public int ActiveBehaviorCount => _behaviors.Count;
            public float TimeScale => (float)BitConverter.Int64BitsToDouble(Interlocked.Read(ref _timeScaleBits));
            public string SystemStatus => !_isRunning ? "Stopped" : _isPaused ? "Paused" : "Running";

            public bool IsUpdateThreadAlive => _isRunning && _isUpdateThreadActive &&
                (GetTimestamp() - Interlocked.Read(ref _updateThreadLastActivity)) < THREAD_INACTIVITY_TIMEOUT_MS;

            public bool IsFixedUpdateThreadAlive => _isRunning && _isFixedUpdateThreadActive &&
                (GetTimestamp() - Interlocked.Read(ref _fixedUpdateThreadLastActivity)) < THREAD_INACTIVITY_TIMEOUT_MS;

            #endregion

            #region 配置

            public void SetTargetFPS(int fps)
            {
                if (fps < MIN_FPS || fps > MAX_FPS) return;
                var req = _configRequestPool.Get();
                req.Reset();
                req.TargetFPS = fps;
                _configQueue.Enqueue(req);
            }

            public void SetFixedUpdateInterval(int intervalMs)
            {
                if (intervalMs < MIN_UPDATE_INTERVAL_MS || intervalMs > MAX_UPDATE_INTERVAL_MS) return;
                var req = _configRequestPool.Get();
                req.Reset();
                req.FixedUpdateInterval = intervalMs;
                _configQueue.Enqueue(req);
            }

            public void SetTimeScale(float timeScale)
            {
                if (timeScale < MIN_TIME_SCALE) timeScale = MIN_TIME_SCALE;
                if (timeScale > MAX_TIME_SCALE) timeScale = MAX_TIME_SCALE;
                var req = _configRequestPool.Get();
                req.Reset();
                req.TimeScale = timeScale;
                _configQueue.Enqueue(req);
            }

            public void ExecuteOnMainThread(Action action) => _mainThreadQueue.Enqueue(action);

            #endregion

            #region 生命周期

            public void Start()
            {
                if (_isRunning) return;

                _isRunning = true;
                _isPaused = false;
                _isUpdateThreadActive = false;
                _isFixedUpdateThreadActive = false;
                Interlocked.Exchange(ref _updateThreadLastActivity, 0);
                Interlocked.Exchange(ref _fixedUpdateThreadLastActivity, 0);

                _cts = new CancellationTokenSource();
                _lastFrameTime = GetTimestamp();
                _fpsLastUpdateTime = _lastFrameTime;
                _frameTimer.Restart();

                RebuildCachedWrappers();

                var token = _cts.Token;

                _fixedUpdateThread = new Thread(() => FixedUpdateLoop(token))
                {
                    Name = $"VeloxDev.FixedUpdate[{Name}]",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };

                _updateThread = new Thread(() => UpdateLoop(token))
                {
                    Name = $"VeloxDev.Update[{Name}]",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };

                _fixedUpdateThread.Start();
                _updateThread.Start();

                Started?.Invoke(this, EventArgs.Empty);
            }

            public async Task StopAsync()
            {
                if (!_isRunning) return;

                _isRunning = false;
                _isPaused = false;
                _isUpdateThreadActive = false;
                _isFixedUpdateThreadActive = false;

                _cts.Cancel();
                _frameTimer.Stop();

                try
                {
                    await Task.Run(() =>
                    {
                        _updateThread?.Join(RESTART_SHUTDOWN_TIMEOUT_MS);
                        _fixedUpdateThread?.Join(RESTART_SHUTDOWN_TIMEOUT_MS);
                    }).ConfigureAwait(false);
                }
                catch (Exception) { }
                finally
                {
                    _updateThread = null;
                    _fixedUpdateThread = null;
                    ResetStatistics();
                    ClearQueues();
                }

                Stopped?.Invoke(this, EventArgs.Empty);
            }

            public void Pause()
            {
                if (!_isRunning || _isPaused) return;
                _isPaused = true;
                Paused?.Invoke(this, EventArgs.Empty);
            }

            public void Resume()
            {
                if (!_isRunning || !_isPaused) return;
                _isPaused = false;
                Resumed?.Invoke(this, EventArgs.Empty);
            }

            public async Task RestartAsync()
            {
                await StopAsync().ConfigureAwait(false);

                var shutdownConfirmed = await WaitForConditionAsync(
                    timeout: TimeSpan.FromMilliseconds(RESTART_SHUTDOWN_TIMEOUT_MS),
                    checkInterval: TimeSpan.FromMilliseconds(DEFAULT_RESTART_CHECK_INTERVAL_MS),
                    condition: () => !_isRunning && !_isUpdateThreadActive && !_isFixedUpdateThreadActive
                ).ConfigureAwait(false);

                if (!shutdownConfirmed)
                {
                    Debug.WriteLine($"[{Name}] Warning: Force restarting after timeout");
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

            public void RegisterBehaviour(IMonoBehaviour behavior)
            {
                if (behavior != null) _addQueue.Enqueue(behavior);
            }

            public void UnregisterBehaviour(IMonoBehaviour behavior)
            {
                if (behavior != null) _removeQueue.Enqueue(behavior);
            }

            public void TogglePause() { if (_isPaused) Resume(); else Pause(); }

            #endregion

            #region 主循环

            private void FixedUpdateLoop(CancellationToken token)
            {
                _isFixedUpdateThreadActive = true;
                Interlocked.Exchange(ref _fixedUpdateThreadLastActivity, GetTimestamp());

                long lastFixedUpdateTime = GetTimestamp();

                try
                {
                    while (_isRunning && !token.IsCancellationRequested)
                    {
                        Interlocked.Exchange(ref _fixedUpdateThreadLastActivity, GetTimestamp());

                        if (_isPaused)
                        {
                            PrecisionSleep(DEFAULT_PAUSE_DELAY_MS, token);
                            continue;
                        }

                        var currentTime = GetTimestamp();
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
                        var waitTime = (int)(nextUpdateTime - GetTimestamp());
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

            private void UpdateLoop(CancellationToken token)
            {
                _isUpdateThreadActive = true;
                Interlocked.Exchange(ref _updateThreadLastActivity, GetTimestamp());

                try
                {
                    while (_isRunning && !token.IsCancellationRequested)
                    {
                        Interlocked.Exchange(ref _updateThreadLastActivity, GetTimestamp());

                        if (_isPaused)
                        {
                            PrecisionSleep(DEFAULT_PAUSE_DELAY_MS, token);
                            continue;
                        }

                        var frameStartTime = GetTimestamp();
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
            private void ExecuteBehaviorsUpdateSync(FrameEventArgs frameArgs, CancellationToken token)
            {
                var wrappers = GetCachedWrappers();
                for (int i = 0; i < wrappers.Length; i++)
                {
                    if (frameArgs.Handled || token.IsCancellationRequested) break;
                    var w = wrappers[i];
                    if (w is { IsActive: true, Behavior: not null })
                    {
                        try { w.Behavior.InvokeUpdate(frameArgs); }
                        catch (Exception ex) { Debug.WriteLine($"[{Name}] Update error: {ex.Message}"); }
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ExecuteBehaviorsLateUpdateSync(FrameEventArgs frameArgs, CancellationToken token)
            {
                var wrappers = GetCachedWrappers();
                for (int i = 0; i < wrappers.Length; i++)
                {
                    if (frameArgs.Handled || token.IsCancellationRequested) break;
                    var w = wrappers[i];
                    if (w is { IsActive: true, Behavior: not null })
                    {
                        try { w.Behavior.InvokeLateUpdate(frameArgs); }
                        catch (Exception ex) { Debug.WriteLine($"[{Name}] LateUpdate error: {ex.Message}"); }
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ExecuteBehaviorsFixedUpdateSync(FrameEventArgs frameArgs, CancellationToken token)
            {
                var wrappers = GetCachedWrappers();
                for (int i = 0; i < wrappers.Length; i++)
                {
                    if (frameArgs.Handled || token.IsCancellationRequested) break;
                    var w = wrappers[i];
                    if (w is { IsActive: true, Behavior: not null })
                    {
                        try { w.Behavior.InvokeFixedUpdate(frameArgs); }
                        catch (Exception ex) { Debug.WriteLine($"[{Name}] FixedUpdate error: {ex.Message}"); }
                    }
                }
            }

            #endregion

            #region 辅助方法

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private BehaviorWrapper[] GetCachedWrappers()
            {
                var currentTime = GetTimestamp();
                if (_wrappersNeedSort || Interlocked.Read(ref _lastConfigCheckTime) + MAX_CONFIG_CACHE_DURATION_MS < currentTime)
                {
                    RebuildCachedWrappers();
                    Interlocked.Exchange(ref _lastConfigCheckTime, currentTime);
                }
                return _cachedWrappers;
            }

            private void ProcessMainThreadOperations()
            {
                // 限制每帧处理数量，防止卡顿
                int processed = 0;
                while (processed < 64 && _mainThreadQueue.TryDequeue(out var action))
                {
                    try { action(); } catch (Exception ex) { Debug.WriteLine($"[{Name}] Main thread error: {ex.Message}"); }
                    processed++;
                }

                ProcessConfigChanges();
                ProcessAddedBehaviors();
                ProcessRemovedBehaviors();
            }

            private void ProcessConfigChanges()
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

            private void ProcessAddedBehaviors()
            {
                bool added = false;
                while (_addQueue.TryDequeue(out var behavior))
                {
                    if (behavior == null) continue;

                    var wrapper = _wrapperPool.Get();
                    wrapper.Reset(behavior, Interlocked.Increment(ref _instanceCounter));

                    _behaviors[RuntimeHelpers.GetHashCode(behavior)] = wrapper;
                    SafeExecute(behavior.InvokeAwake);
                    SafeExecute(behavior.InvokeStart);
                    added = true;
                }
                if (added) _wrappersNeedSort = true;
            }

            private void ProcessRemovedBehaviors()
            {
                bool removed = false;
                while (_removeQueue.TryDequeue(out var behavior))
                {
                    if (behavior != null && _behaviors.TryRemove(RuntimeHelpers.GetHashCode(behavior), out var wrapper))
                    {
                        wrapper.Clear();
                        _wrapperPool.Return(wrapper);
                        removed = true;
                    }
                }
                if (removed) _wrappersNeedSort = true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private FrameEventArgs CreateFrameEventArgs(int deltaTime)
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

            private void DrainFixedUpdateEvents()
            {
                while (_fixedUpdateEvents.TryDequeue(out var fixedEvent))
                    _frameEventArgsPool.Return(fixedEvent);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void FrameRateControlSync(long frameStartTime, CancellationToken token)
            {
                var elapsed = GetTimestamp() - frameStartTime;
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
            private void PrecisionSleep(int milliseconds, CancellationToken token)
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

            private void RebuildCachedWrappers()
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
            private static long GetTimestamp() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private int CalculateDeltaTime(long currentTime)
            {
                var last = Volatile.Read(ref _lastFrameTime);
                if (last <= 0) return 1;
                return Math.Max(1, (int)(currentTime - last));
            }

            private void UpdatePerformanceStats(long frameStartTime, int deltaTime)
            {
                Interlocked.Add(ref _totalTimeMs, deltaTime);
                Volatile.Write(ref _lastFrameTime, frameStartTime);
                _fpsCounter++;

                var currentTime = GetTimestamp();
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

            private void ResetStatistics()
            {
                Interlocked.Exchange(ref _totalTimeMs, 0);
                _currentFPS = 0;
                _fpsCounter = 0;
                _fpsLastUpdateTime = 0;
                Interlocked.Exchange(ref _totalFrames, 0);
                Volatile.Write(ref _lastFrameTime, 0);
                Interlocked.Exchange(ref _lastConfigCheckTime, 0);
            }

            private void ClearQueues()
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

            private void ForceCleanup()
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                _updateThread = null;
                _fixedUpdateThread = null;
                _isRunning = false;
                _isUpdateThreadActive = false;
                _isFixedUpdateThreadActive = false;

                ClearQueues();
            }

            private static async Task<bool> WaitForConditionAsync(TimeSpan timeout, TimeSpan checkInterval, Func<bool> condition)
            {
                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed < timeout)
                {
                    if (condition()) return true;
                    await Task.Delay((int)Math.Max(1, checkInterval.TotalMilliseconds)).ConfigureAwait(false);
                }
                return false;
            }

            #endregion
        }

        #endregion

        #region 通道管理

        private static readonly ConcurrentDictionary<string, LoopChannel> _channels = new();

        private static LoopChannel GetOrCreateChannel(string name)
        {
            return _channels.GetOrAdd(name, n =>
            {
                var ch = new LoopChannel(n);
                ch.Started += (s, e) => OnChannelStarted?.Invoke(s, new MonoBehaviourChannelEventArgs(n));
                ch.Paused += (s, e) => OnChannelPaused?.Invoke(s, new MonoBehaviourChannelEventArgs(n));
                ch.Resumed += (s, e) => OnChannelResumed?.Invoke(s, new MonoBehaviourChannelEventArgs(n));
                ch.Stopped += (s, e) => OnChannelStopped?.Invoke(s, new MonoBehaviourChannelEventArgs(n));
                return ch;
            });
        }

        /// <summary>获取所有已创建的通道名称</summary>
        public static IEnumerable<string> ChannelNames => _channels.Keys;

        #endregion

        #region 全局事件

        public static event EventHandler<MonoBehaviourChannelEventArgs>? OnChannelStarted;
        public static event EventHandler<MonoBehaviourChannelEventArgs>? OnChannelPaused;
        public static event EventHandler<MonoBehaviourChannelEventArgs>? OnChannelResumed;
        public static event EventHandler<MonoBehaviourChannelEventArgs>? OnChannelStopped;

        #endregion

        #region 生命周期管理

        public static void Start(string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).Start();

        public static Task StopAsync(string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).StopAsync();

        public static void Pause(string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).Pause();

        public static void Resume(string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).Resume();

        public static Task RestartAsync(string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).RestartAsync();

        public static void TogglePause(string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).TogglePause();

        public static void RegisterBehaviour(IMonoBehaviour behavior, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).RegisterBehaviour(behavior);

        public static void UnregisterBehaviour(IMonoBehaviour behavior, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).UnregisterBehaviour(behavior);

        #endregion

        #region 配置API

        public static void SetTargetFPS(int fps, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).SetTargetFPS(fps);

        public static void SetFixedUpdateInterval(int intervalMs, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).SetFixedUpdateInterval(intervalMs);

        public static void SetTimeScale(float timeScale, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).SetTimeScale(timeScale);

        public static void ExecuteOnMainThread(Action action, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).ExecuteOnMainThread(action);

        #endregion

        #region 状态查询

        public static bool IsRunning(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) && c.IsRunning;

        public static bool IsPaused(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) && c.IsPaused;

        public static int CurrentFPS(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.CurrentFPS : 0;

        public static int TargetFPS(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.TargetFPS : DEFAULT_TARGET_FPS;

        public static long TotalTimeMs(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.TotalTimeMs : 0;

        public static long TotalFrames(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.TotalFrames : 0;

        public static int ActiveBehaviorCount(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.ActiveBehaviorCount : 0;

        public static float TimeScale(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.TimeScale : DEFAULT_TIME_SCALE;

        public static string SystemStatus(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.SystemStatus : "Stopped";

        public static bool IsUpdateThreadAlive(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) && c.IsUpdateThreadAlive;

        public static bool IsFixedUpdateThreadAlive(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) && c.IsFixedUpdateThreadAlive;

        #endregion
    }

    public sealed class MonoBehaviourChannelEventArgs : EventArgs
    {
        public string ChannelName { get; }
        public MonoBehaviourChannelEventArgs(string channelName) { ChannelName = channelName; }
    }
}