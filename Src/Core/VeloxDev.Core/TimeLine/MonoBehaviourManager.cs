using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VeloxDev.MonoBehaviour;
using VeloxDev.Timing;

namespace VeloxDev.TimeLine
{
    public static class MonoBehaviourManager
    {
        #region Constants

        private const int MIN_SLEEP_MS = 1;
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

        private const float DEFAULT_TIME_SCALE = 1.0f;
        private const int MIN_UPDATE_INTERVAL_MS = 1;
        private const int MAX_UPDATE_INTERVAL_MS = 1000;

        // 睡多久就查一次令牌：既不忙等，也不让一次停止等上一整个间隔（最低目标帧率下预算有一秒长）。
        private const int MAX_SLEEP_CHUNK_MS = 50;

        public const string DEFAULT_CHANNEL = "default";

        #endregion

        #region Internal classes

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

            public void Reset()
            {
                TargetFPS = null;
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

            private volatile bool _isRunning;

            /// <summary>
            /// The channel's transport. Pause, resume and rate all live here rather than in channel fields, and
            /// exposing it is what lets an animation share this channel's clock: one <c>Pause()</c> then stops the
            /// frame callbacks and the animations together, and the rate multiplies both.
            /// </summary>
            private readonly ITimeSourceControl _bus = TimerCore.CreateTimeSource<ITimeSourceControl>();

            /// <summary>Best-effort interval sampling: what each frame is told has elapsed.</summary>
            private readonly IUncompensatedTimeSampler _updateSampler;

            /// <summary>Fixed-step sampling for FixedUpdate, whose push count has to come out exact.</summary>
            private readonly ICompensatingTimeSampler _fixedSampler;

            /// <summary>
            /// A step handed in from another thread, consumed by the fixed loop on its own thread. Zero means nothing
            /// is pending — a step of zero is not a legal value, so it is free to mean "empty".
            /// </summary>
            private int _pendingFixedIntervalMs;

            private int _targetFPS = DEFAULT_TARGET_FPS;
            private long _totalTimeTicks;
            private int _currentFPS;
            private int _fpsCounter;
            private long _fpsLastUpdateTimestamp;
            private long _totalFrames;
            private int _instanceCounter;

            private CancellationTokenSource _cts = new();
            private Thread? _updateThread;
            private Thread? _fixedUpdateThread;
            private volatile bool _isUpdateThreadActive;
            private volatile bool _isFixedUpdateThreadActive;
            private long _updateThreadLastActivityTimestamp;
            private long _fixedUpdateThreadLastActivityTimestamp;
            // Async task replacing Thread in WASM mode
            private Task? _updateTask;
            private Task? _fixedUpdateTask;

            // Per-channel override of UseAsyncLoop; falls back to MonoBehaviourManager.UseAsyncLoop when null
            private bool? _useAsyncLoopOverride;

            private readonly ObjectPool<FrameEventArgs> _frameEventArgsPool = new(DEFAULT_OBJECT_POOL_SIZE);
            private readonly ObjectPool<ConfigChangeRequest> _configRequestPool = new(DEFAULT_OBJECT_POOL_SIZE);
            private readonly ObjectPool<BehaviorWrapper> _wrapperPool = new(DEFAULT_OBJECT_POOL_SIZE);
            private double _cachedTargetFrameDurationTicks = (double)Stopwatch.Frequency / DEFAULT_TARGET_FPS;
            private long _lastConfigCheckTimestamp;
            private volatile BehaviorWrapper[] _cachedWrappers = [];
            private volatile bool _wrappersNeedSort;

            public LoopChannel(string name)
            {
                Name = name;
                _updateSampler = TimerCore.CreateTimeSampler<IUncompensatedTimeSampler>(_bus);
                _fixedSampler = TimerCore.CreateTimeSampler<ICompensatingTimeSampler>(_bus);
                // 步长显式取自渠道自己的默认值，而不是依赖注册表恰好配了一样的数字。
                _fixedSampler.Step = TimeSpan.FromMilliseconds(DEFAULT_FIXED_UPDATE_INTERVAL_MS);
            }

            public event EventHandler? Started;
            public event EventHandler? Paused;
            public event EventHandler? Resumed;
            public event EventHandler? Stopped;

            #region Public properties

            public bool IsRunning => _isRunning;
            public bool IsPaused => _bus.IsPaused;
            public int CurrentFPS => _currentFPS;
            public int TargetFPS => Volatile.Read(ref _targetFPS);
            public TimeSpan TotalTime => TimeSpan.FromTicks(Interlocked.Read(ref _totalTimeTicks));
            public long TotalTimeMs => (long)TotalTime.TotalMilliseconds;
            public long TotalFrames => Interlocked.Read(ref _totalFrames);
            public int ActiveBehaviorCount => _behaviors.Count;
            public float TimeScale => (float)_bus.Rate;
            public string SystemStatus => !_isRunning ? "Stopped" : _bus.IsPaused ? "Paused" : "Running";

            /// <summary>This channel's transport, so a consumer can anchor to the same clock the frames advance on.</summary>
            public ITimeSourceControl Bus => _bus;

            // 停摆中的循环仍然活着：它 park 在总线上等唤醒而不是空转，于是「最近有没有活动」这个判据在暂停时
            // 必然为假。不改的话，暂停超过 THREAD_INACTIVITY_TIMEOUT_MS 就会把一条好线程报成已死，
            // RestartAsync 也会因此等不到关闭确认而走 ForceCleanup。
            public bool IsUpdateThreadAlive => _isRunning && _isUpdateThreadActive &&
                (!_bus.IsAdvancing || IsRecentActivity(Interlocked.Read(ref _updateThreadLastActivityTimestamp)));

            public bool IsFixedUpdateThreadAlive => _isRunning && _isFixedUpdateThreadActive &&
                (!_bus.IsAdvancing || IsRecentActivity(Interlocked.Read(ref _fixedUpdateThreadLastActivityTimestamp)));

            #endregion

            #region Configuration

            public void SetTargetFPS(int fps)
            {
                if (fps < MIN_FPS || fps > MAX_FPS) return;
                var req = _configRequestPool.Get();
                req.Reset();
                req.TargetFPS = fps;
                _configQueue.Enqueue(req);
            }

            /// <summary>
            /// Sets the interval between FixedUpdate pushes.
            /// </summary>
            /// <remarks>
            /// Handed to the fixed loop rather than applied here, and deliberately not through the config queue: that
            /// queue is drained by the <em>update</em> loop, and the sampler is owned by the fixed one. Writing
            /// <c>Step</c> from the update thread would race <c>Advance</c> over the accumulator it resets, which is
            /// worse than the single field this used to be. The fixed loop picks the value up on its own thread.
            /// </remarks>
            public void SetFixedUpdateInterval(int intervalMs)
            {
                if (intervalMs < MIN_UPDATE_INTERVAL_MS || intervalMs > MAX_UPDATE_INTERVAL_MS) return;
                Volatile.Write(ref _pendingFixedIntervalMs, intervalMs);
            }

            /// <summary>
            /// Sets the channel's rate, which is the bus's rate verbatim.
            /// </summary>
            /// <remarks>
            /// Applied straight to the bus instead of through the config queue: the bus serialises its own writers,
            /// and the queue existed only because the rate used to be a channel field the loop had to own. It also
            /// follows the bus's rule rather than a channel-local one — no clamping, and a negative rate is rejected
            /// rather than silently ignored. A rate of zero freezes the clock, which parks both loops rather than
            /// leaving them running against a clock that never moves.
            /// </remarks>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeScale"/> is negative.</exception>
            public void SetTimeScale(float timeScale) => _bus.SetRate(timeScale);

            public void ExecuteOnMainThread(Action action) => _mainThreadQueue.Enqueue(action);

            /// <summary>
            /// Sets whether the current channel uses async/await instead of native Threads to drive the frame loop.
            /// When null, falls back to the global <see cref="MonoBehaviourManager.UseAsyncLoop"/>.
            /// </summary>
            /// <exception cref="InvalidOperationException">Thrown when the channel is already running.</exception>
            public void SetUseAsyncLoop(bool useAsyncLoop)
            {
                if (_isRunning)
                    throw new InvalidOperationException(
                        $"Cannot change UseAsyncLoop while channel '{Name}' is running. Stop the channel first.");
                _useAsyncLoopOverride = useAsyncLoop;
            }

            /// <summary>
            /// Clears the current channel's independent override, falling back to the global
            /// <see cref="MonoBehaviourManager.UseAsyncLoop"/>.
            /// </summary>
            /// <exception cref="InvalidOperationException">Thrown when the channel is already running.</exception>
            public void ClearUseAsyncLoopOverride()
            {
                if (_isRunning)
                    throw new InvalidOperationException(
                        $"Cannot clear UseAsyncLoop override while channel '{Name}' is running. Stop the channel first.");
                _useAsyncLoopOverride = null;
            }

            private bool EffectiveUseAsyncLoop => _useAsyncLoopOverride ?? MonoBehaviourManager.UseAsyncLoop;

            #endregion

            #region Lifecycle

            public void Start()
            {
                if (_isRunning) return;

                _isRunning = true;
                _isUpdateThreadActive = false;
                _isFixedUpdateThreadActive = false;
                Interlocked.Exchange(ref _updateThreadLastActivityTimestamp, 0);
                Interlocked.Exchange(ref _fixedUpdateThreadLastActivityTimestamp, 0);

                _cts = new CancellationTokenSource();

                // 采样器重新锚定到「现在」：这就是原来那句 _lastFrameTimestamp = GetTimestamp()，只是累计量
                // 现在归采样器所有。不清的话，重启后的第一帧会带上整个停机时间。
                _updateSampler.Reset();
                _fixedSampler.Reset();

                // 启动要清掉上一个生命周期留下的暂停，与旧实现的 _isPaused = false 对齐。不清的话，
                // 一个「暂停中被停掉」的渠道重启后会立刻 park，再也跑不起来。
                _bus.Resume();

                _fpsLastUpdateTimestamp = GetTimestamp();

                RebuildCachedWrappers();

                var token = _cts.Token;

                if (EffectiveUseAsyncLoop)
                {
                    _fixedUpdateTask = FixedUpdateLoopAsync(token);
                    _updateTask = UpdateLoopAsync(token);
                }
                else
                {
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
                }

                Started?.Invoke(this, EventArgs.Empty);
            }

            public async Task StopAsync()
            {
                if (!_isRunning) return;

                _isRunning = false;
                _isUpdateThreadActive = false;
                _isFixedUpdateThreadActive = false;

                // 与旧实现的 _isPaused = false 对齐：停下的渠道不留一个暂停状态给下一个生命周期。
                // 注意这不会解除 rate 为 0 造成的冻结——那不是一个暂停，只有把 rate 调回非零才行。
                _bus.Resume();

                _cts.Cancel();

                try
                {
                    if (EffectiveUseAsyncLoop)
                    {
                        var pending = new System.Collections.Generic.List<Task>(2);
                        if (_updateTask != null) pending.Add(_updateTask);
                        if (_fixedUpdateTask != null) pending.Add(_fixedUpdateTask);
                        if (pending.Count > 0)
                            await Task.WhenAny(Task.WhenAll(pending), Task.Delay(RESTART_SHUTDOWN_TIMEOUT_MS)).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Run(() =>
                        {
                            _updateThread?.Join(RESTART_SHUTDOWN_TIMEOUT_MS);
                            _fixedUpdateThread?.Join(RESTART_SHUTDOWN_TIMEOUT_MS);
                        }).ConfigureAwait(false);
                    }
                }
                catch (Exception) { }
                finally
                {
                    _updateThread = null;
                    _fixedUpdateThread = null;
                    _updateTask = null;
                    _fixedUpdateTask = null;
                    ResetStatistics();
                    ClearQueues();
                }

                Stopped?.Invoke(this, EventArgs.Empty);
            }

            /// <summary>
            /// Freezes the channel: no frame callbacks, no fixed pushes, and no time accrued, for as long as it lasts.
            /// </summary>
            /// <remarks>
            /// The bus is the one source of truth for the paused state, so this is what an animation anchored to the
            /// same transport observes too. Nothing polls: both loops park on the bus's signal, so a paused channel
            /// costs no wake-ups at all — where it used to wake every 10 ms to re-read a flag.
            /// </remarks>
            public void Pause()
            {
                if (!_isRunning || _bus.IsPaused) return;
                _bus.Pause();
                Paused?.Invoke(this, EventArgs.Empty);
            }

            /// <summary>Lets a paused channel run again, at the rate it was last set to.</summary>
            /// <remarks>
            /// After a rate of zero this lifts the pause without making the clock advance, so the channel stays
            /// parked; only a non-zero rate starts it. That is the bus's rule, and it is deliberately not papered
            /// over here.
            /// </remarks>
            public void Resume()
            {
                if (!_isRunning || !_bus.IsPaused) return;
                _bus.Resume();
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

            public void TogglePause() { if (_bus.IsPaused) Resume(); else Pause(); }

            #endregion

            #region Main loop

            private void FixedUpdateLoop(CancellationToken token)
            {
                _isFixedUpdateThreadActive = true;
                Interlocked.Exchange(ref _fixedUpdateThreadLastActivityTimestamp, GetTimestamp());

                try
                {
                    while (_isRunning && !token.IsCancellationRequested)
                    {
                        Interlocked.Exchange(ref _fixedUpdateThreadLastActivityTimestamp, GetTimestamp());

                        // 步长只能在这条线程上落：采样器归它所有（理由见 SetFixedUpdateInterval）。
                        var pendingInterval = Volatile.Read(ref _pendingFixedIntervalMs);
                        if (pendingInterval != 0)
                        {
                            Volatile.Write(ref _pendingFixedIntervalMs, 0);
                            _fixedSampler.Step = TimeSpan.FromMilliseconds(pendingInterval);
                        }

                        // 停摆时 park 在总线上，而不是像以前那样每 10 ms 醒来轮询一次。专用线程不能 await，
                        // 所以这里同步阻塞：拿一条自有线程的阻塞换零唤醒是划算的，而取消会立刻穿透这个等待
                        // （总线会观察令牌），所以 StopAsync 不需要额外唤醒它。
                        if (!_bus.IsAdvancing)
                        {
                            _bus.WaitWhileStalledAsync(token).GetAwaiter().GetResult();
                            continue;
                        }

                        // 一次调用可能欠好几步（一段停顿之后欠的），要全部推完。旧实现推完一次就把基准设成
                        // 当前时间，超出的余数直接消失——推送总数于是永久少于墙钟，相位也跟着漂。
                        var count = _fixedSampler.Advance(out var sample);
                        if (count > 0)
                        {
                            // 每一步的 Total 是自己的步序号乘步长，而不是把最后一次的读数重复 N 遍。
                            var stepTicks = _fixedSampler.Step.Ticks;
                            var firstStep = sample.Step - count + 1;

                            for (var i = 0; i < count; i++)
                            {
                                var fixedFrameArgs = CreateFrameEventArgs(
                                    sample.Delta,
                                    TimeSpan.FromTicks((firstStep + i) * stepTicks));
                                ExecuteBehaviorsFixedUpdateSync(fixedFrameArgs, token);

                                // 直接还池。这里原来把它塞进一个跨线程队列，由 update 循环取出再还池——
                                // 一次往返，而那条队列从不把参数交给任何人，纯粹是浪费。
                                _frameEventArgsPool.Return(fixedFrameArgs);
                            }
                        }

                        // 睡到下一个步边界。采样器欠着步时这个值是零，于是立刻再推一批——限速的追赶就是这么展开的。
                        var wait = _fixedSampler.TimeToNextStep;
                        if (wait > TimeSpan.Zero)
                            Sleep(wait, token);
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
                Interlocked.Exchange(ref _updateThreadLastActivityTimestamp, GetTimestamp());

                try
                {
                    while (_isRunning && !token.IsCancellationRequested)
                    {
                        Interlocked.Exchange(ref _updateThreadLastActivityTimestamp, GetTimestamp());

                        if (!_bus.IsAdvancing)
                        {
                            _bus.WaitWhileStalledAsync(token).GetAwaiter().GetResult();
                            continue;
                        }

                        var frameStartTime = GetTimestamp();
                        ProcessMainThreadOperations();

                        // 无偿采样：总线没前进就没有帧可推。暂停期不累计，所以恢复后的第一帧也不会带上整段暂停。
                        var sample = _updateSampler.Sample();
                        if (sample.Delta == TimeSpan.Zero)
                        {
                            Sleep(TimeSpan.FromMilliseconds(MIN_SLEEP_MS), token);
                            continue;
                        }

                        var frameArgs = CreateFrameEventArgs(sample.Delta, sample.Total);

                        ExecuteBehaviorsUpdateSync(frameArgs, token);
                        ExecuteBehaviorsLateUpdateSync(frameArgs, token);

                        _frameEventArgsPool.Return(frameArgs);

                        UpdatePerformanceStats(frameStartTime, sample.Total);
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

            // WASM-compatible path: replaces Thread + Thread.Sleep with async/await + Task.Delay
            private async Task FixedUpdateLoopAsync(CancellationToken token)
            {
                _isFixedUpdateThreadActive = true;
                Interlocked.Exchange(ref _fixedUpdateThreadLastActivityTimestamp, GetTimestamp());

                try
                {
                    while (_isRunning && !token.IsCancellationRequested)
                    {
                        Interlocked.Exchange(ref _fixedUpdateThreadLastActivityTimestamp, GetTimestamp());

                        // 步长只能在这条线程上落：采样器归它所有（理由见 SetFixedUpdateInterval）。
                        var pendingInterval = Volatile.Read(ref _pendingFixedIntervalMs);
                        if (pendingInterval != 0)
                        {
                            Volatile.Write(ref _pendingFixedIntervalMs, 0);
                            _fixedSampler.Step = TimeSpan.FromMilliseconds(pendingInterval);
                        }

                        // 停摆时的等待是异步的，所以这条路径连线程都不占。
                        if (!_bus.IsAdvancing)
                        {
                            await _bus.WaitWhileStalledAsync(token).ConfigureAwait(false);
                            continue;
                        }

                        var count = _fixedSampler.Advance(out var sample);
                        if (count > 0)
                        {
                            var stepTicks = _fixedSampler.Step.Ticks;
                            var firstStep = sample.Step - count + 1;

                            for (var i = 0; i < count; i++)
                            {
                                var fixedFrameArgs = CreateFrameEventArgs(
                                    sample.Delta,
                                    TimeSpan.FromTicks((firstStep + i) * stepTicks));
                                ExecuteBehaviorsFixedUpdateSync(fixedFrameArgs, token);

                                // 直接还池。这里原来把它塞进一个跨线程队列，由 update 循环取出再还池——
                                // 一次往返，而那条队列从不把参数交给任何人，纯粹是浪费。
                                _frameEventArgsPool.Return(fixedFrameArgs);
                            }
                        }

                        var wait = _fixedSampler.TimeToNextStep;
                        if (wait > TimeSpan.Zero)
                        {
                            await Task.Delay(
                                (int)Math.Max(1, wait.TotalMilliseconds),
                                token).ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Yield();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    _isFixedUpdateThreadActive = false;
                }
            }

            private async Task UpdateLoopAsync(CancellationToken token)
            {
                _isUpdateThreadActive = true;
                Interlocked.Exchange(ref _updateThreadLastActivityTimestamp, GetTimestamp());

                try
                {
                    while (_isRunning && !token.IsCancellationRequested)
                    {
                        Interlocked.Exchange(ref _updateThreadLastActivityTimestamp, GetTimestamp());

                        if (!_bus.IsAdvancing)
                        {
                            await _bus.WaitWhileStalledAsync(token).ConfigureAwait(false);
                            continue;
                        }

                        var frameStartTime = GetTimestamp();
                        ProcessMainThreadOperations();

                        var sample = _updateSampler.Sample();
                        if (sample.Delta == TimeSpan.Zero)
                        {
                            await Task.Delay(MIN_SLEEP_MS, token).ConfigureAwait(false);
                            continue;
                        }

                        var frameArgs = CreateFrameEventArgs(sample.Delta, sample.Total);

                        ExecuteBehaviorsUpdateSync(frameArgs, token);
                        ExecuteBehaviorsLateUpdateSync(frameArgs, token);

                        _frameEventArgsPool.Return(frameArgs);

                        UpdatePerformanceStats(frameStartTime, sample.Total);

                        var frameElapsed = GetTimestamp() - frameStartTime;
                        var frameTarget = _cachedTargetFrameDurationTicks;
                        if (frameElapsed < frameTarget)
                        {
                            var waitMs = (int)Math.Max(
                                1,
                                TimeConversion.TicksToMilliseconds(
                                    (long)(frameTarget - frameElapsed),
                                    TimeConversion.DefaultTicksPerSecond));
                            await Task.Delay(waitMs, token).ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Yield();
                        }

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

            #region Behavior execution

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

            #region Helper methods

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private BehaviorWrapper[] GetCachedWrappers()
            {
                var currentTime = GetTimestamp();
                if (_wrappersNeedSort || currentTime - Interlocked.Read(ref _lastConfigCheckTimestamp) >
                    TimeConversion.MillisecondsToTicks(MAX_CONFIG_CACHE_DURATION_MS, TimeConversion.DefaultTicksPerSecond))
                {
                    RebuildCachedWrappers();
                    Interlocked.Exchange(ref _lastConfigCheckTimestamp, currentTime);
                }
                return _cachedWrappers;
            }

            private void ProcessMainThreadOperations()
            {
                // Limit the number processed per frame to prevent stutter
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
                    // 目标帧率走队列是为了让这个字段和下面那个缓存一起改：它们必须同时生效，
                    // 而两者都只有 update 循环会读。
                    if (config.TargetFPS.HasValue)
                    {
                        Volatile.Write(ref _targetFPS, config.TargetFPS.Value);
                        _cachedTargetFrameDurationTicks = (double)Stopwatch.Frequency / config.TargetFPS.Value;
                    }

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

            /// <summary>
            /// Builds the arguments for one frame. Both times come from a <see cref="TimeSample"/>, so the rate has
            /// already been applied by the clock rather than scaled here afterwards — which is also why
            /// <c>DeltaTime</c> is the virtual interval and <c>TotalTime</c> excludes everything spent stalled.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private FrameEventArgs CreateFrameEventArgs(TimeSpan delta, TimeSpan total)
            {
                var frameArgs = _frameEventArgsPool.Get();
                frameArgs.DeltaTime = delta;
                frameArgs.TotalTime = total;
                frameArgs.CurrentFPS = _currentFPS;
                frameArgs.TargetFPS = Volatile.Read(ref _targetFPS);
                frameArgs.Handled = false;
                return frameArgs;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void FrameRateControlSync(long frameStartTime, CancellationToken token)
            {
                // 目标帧率约束的是采样节奏，不是虚拟时钟，所以这里量的是墙钟——rate 减半不应该让帧率也减半。
                var elapsed = GetTimestamp() - frameStartTime;
                var target = _cachedTargetFrameDurationTicks;
                if (elapsed < target)
                {
                    var sleepTime = (long)(target - elapsed);
                    if (sleepTime > 0)
                        Sleep(TimeConversion.TicksToTimeSpan(sleepTime, TimeConversion.DefaultTicksPerSecond), token);
                }
            }

            /// <summary>
            /// Waits out <paramref name="duration"/> without spinning.
            /// </summary>
            /// <remarks>
            /// Nothing here needs sub-millisecond promptness. Correctness belongs to the samplers: FixedUpdate repays every
            /// step it owes however late it is asked, and Update is not compensated at all, so a late wake means a frame or
            /// a step arriving late — never one lost, and never a wrong interval. What the tail spin this replaced bought
            /// was cadence precision, and it paid for it with a busy-wait on both loops on every frame.
            /// <para>
            /// Slept in chunks so a stop is noticed within <see cref="MAX_SLEEP_CHUNK_MS"/> instead of after a whole
            /// interval: at the lowest target FPS a frame budget is a second long, and a stop would otherwise wait it out.
            /// </para>
            /// </remarks>
            private static void Sleep(TimeSpan duration, CancellationToken token)
            {
                var chunk = TimeSpan.FromMilliseconds(MAX_SLEEP_CHUNK_MS);
                for (var remaining = duration; remaining > TimeSpan.Zero; remaining -= chunk)
                {
                    if (token.IsCancellationRequested) return;
                    Thread.Sleep(remaining < chunk ? remaining : chunk);
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

                // Insertion sort — behavior counts are usually small, avoiding LINQ allocations
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
            private static long GetTimestamp() => Stopwatch.GetTimestamp();

            /// <summary>
            /// Publishes the total the sampler just reported, rather than accumulating deltas here.
            /// </summary>
            /// <remarks>
            /// The total is the clock's, so it excludes everything spent stalled and never inherits a lump of it —
            /// which is what the old accumulate-deltas form did on the first frame after a resume. The rate is
            /// already in it too, since the bus applies it before anything is sampled.
            /// </remarks>
            private void UpdatePerformanceStats(long frameStartTime, TimeSpan total)
            {
                Interlocked.Exchange(ref _totalTimeTicks, total.Ticks);
                _fpsCounter++;

                // FPS 量的是墙钟，所以这里用泵的开始时刻而不是虚拟时间。
                if (frameStartTime - _fpsLastUpdateTimestamp >= Stopwatch.Frequency)
                {
                    _currentFPS = _fpsCounter;
                    _fpsCounter = 0;
                    _fpsLastUpdateTimestamp = frameStartTime;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool IsRecentActivity(long lastActivityTimestamp)
            {
                if (lastActivityTimestamp <= 0)
                    return false;

                return TimeConversion.TicksToMilliseconds(
                    GetTimestamp() - lastActivityTimestamp,
                    TimeConversion.DefaultTicksPerSecond) < THREAD_INACTIVITY_TIMEOUT_MS;
            }

            private static void SafeExecute(Action action)
            {
                try { action?.Invoke(); } catch (Exception ex) { Debug.WriteLine($"Behavior error: {ex.Message}"); }
            }

            private void ResetStatistics()
            {
                Interlocked.Exchange(ref _totalTimeTicks, 0);
                _currentFPS = 0;
                _fpsCounter = 0;
                _fpsLastUpdateTimestamp = 0;
                Interlocked.Exchange(ref _totalFrames, 0);
                Interlocked.Exchange(ref _lastConfigCheckTimestamp, 0);
            }

            private void ClearQueues()
            {
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
                _updateTask = null;
                _fixedUpdateTask = null;
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

        #region Configuration

        /// <summary>
        /// Uses async/await + Task.Delay instead of native Threads to drive the frame loop.
        /// Automatically enabled on platforms that do not support Thread (WASM, iOS NativeAOT, etc.).
        /// </summary>
        public static bool UseAsyncLoop { get; set; } =
#if NET5_0_OR_GREATER
            OperatingSystem.IsBrowser() || OperatingSystem.IsIOS();
#else
            true;
#endif

        #endregion

        #region Channel management

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

        /// <summary>Gets the names of all created channels.</summary>
        public static IEnumerable<string> ChannelNames => _channels.Keys;

        #endregion

        #region Global events

        public static event EventHandler<MonoBehaviourChannelEventArgs>? OnChannelStarted;
        public static event EventHandler<MonoBehaviourChannelEventArgs>? OnChannelPaused;
        public static event EventHandler<MonoBehaviourChannelEventArgs>? OnChannelResumed;
        public static event EventHandler<MonoBehaviourChannelEventArgs>? OnChannelStopped;

        #endregion

        #region Lifecycle management

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

        #region Configuration API

        public static void SetTargetFPS(int fps, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).SetTargetFPS(fps);

        public static void SetFixedUpdateInterval(int intervalMs, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).SetFixedUpdateInterval(intervalMs);

        public static void SetTimeScale(float timeScale, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).SetTimeScale(timeScale);

        public static void ExecuteOnMainThread(Action action, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).ExecuteOnMainThread(action);

        /// <summary>
        /// Sets whether the specified channel uses async/await instead of native Threads to drive the frame loop.
        /// Overrides the global <see cref="UseAsyncLoop"/> setting.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the channel is already running.</exception>
        public static void SetUseAsyncLoop(bool useAsyncLoop, string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).SetUseAsyncLoop(useAsyncLoop);

        /// <summary>
        /// Clears the specified channel's independent override, falling back to the global <see cref="UseAsyncLoop"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the channel is already running.</exception>
        public static void ClearUseAsyncLoopOverride(string channel = DEFAULT_CHANNEL)
            => GetOrCreateChannel(channel).ClearUseAsyncLoopOverride();

        #endregion

        #region Status queries

        public static bool IsRunning(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) && c.IsRunning;

        public static bool IsPaused(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) && c.IsPaused;

        public static int CurrentFPS(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.CurrentFPS : 0;

        public static int TargetFPS(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.TargetFPS : DEFAULT_TARGET_FPS;

        public static TimeSpan TotalTime(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.TotalTime : TimeSpan.Zero;

        public static long TotalTimeMs(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.TotalTimeMs : 0;

        public static long TotalFrames(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.TotalFrames : 0;

        public static int ActiveBehaviorCount(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.ActiveBehaviorCount : 0;

        public static float TimeScale(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.TimeScale : DEFAULT_TIME_SCALE;

        /// <summary>
        /// The time source a channel's frames advance on, or null when that channel does not exist yet.
        /// </summary>
        /// <remarks>
        /// This is what lets an animation share a channel's transport: passing it to
        /// <c>Transition.Execute(target, bus, ...)</c> anchors the animation to the same clock, so one
        /// <c>Pause()</c> on the channel stops the frame callbacks and the animation together and the channel's rate
        /// multiplies both. Null is the honest answer for a channel that was never started — a query must not create
        /// one as a side effect.
        /// </remarks>
        public static ITimeSourceControl? Bus(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.Bus : null;

        public static string SystemStatus(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) ? c.SystemStatus : "Stopped";

        public static bool IsUpdateThreadAlive(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) && c.IsUpdateThreadAlive;

        public static bool IsFixedUpdateThreadAlive(string channel = DEFAULT_CHANNEL)
            => _channels.TryGetValue(channel, out var c) && c.IsFixedUpdateThreadAlive;

        #endregion
    }

    public sealed class MonoBehaviourChannelEventArgs(string channelName) : EventArgs
    {
        public string ChannelName { get; } = channelName;
    }
}