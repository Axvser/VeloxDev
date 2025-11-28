using System.Collections.Concurrent;
using VeloxDev.Core.Interfaces.MonoBehavior;

namespace VeloxDev.Core.TimeLine
{
    /// <summary>
    /// 管理所有MonoBehaviour实例的生命周期，提供高性能、线程安全的帧更新调度
    /// </summary>
    public static class MonoBehaviourManager
    {
        #region 内部类和结构

        private class BehaviorWrapper(IMonoBehavior behavior, int executionOrder)
        {
            public WeakReference<IMonoBehavior> WeakBehavior { get; } = new WeakReference<IMonoBehavior>(behavior);
            public int ExecutionOrder { get; } = executionOrder;
            public long LastExecutionTicks { get; set; }
            public long AverageExecutionTime { get; set; }
        }

        private struct ExecutionContext
        {
            public FrameEventArgs FrameArgs;
            public CancellationToken CancellationToken;
            public long FrameStartTicks;
            public long TimeSliceTicks;
        }

        #endregion

        #region 常量和配置

        private const int DEFAULT_TARGET_FPS = 60;
        private const int MAX_FRAME_TIME_MS = 16; // 60FPS对应的帧时间
        private const int MIN_FRAME_TIME_MS = 2;  // 500FPS对应的帧时间
        private const int TIME_SLICE_US = 1000;   // 每帧每个行为最大执行时间1ms

        // 自适应帧率控制参数
        private const double ADAPTIVE_SMOOTHING = 0.9;
        private const int ADAPTIVE_THRESHOLD_MS = 12;

        #endregion

        #region 私有字段

        private static readonly ConcurrentDictionary<int, BehaviorWrapper> _behaviors = new();
        private static readonly ReaderWriterLockSlim _behaviorLock = new();
        private static readonly ConcurrentQueue<IMonoBehavior> _addQueue = new();
        private static readonly ConcurrentQueue<WeakReference<IMonoBehavior>> _removeQueue = new();

        private static volatile bool _isRunning = false;
        private static volatile bool _isInitialized = false;
        private static int _targetFPS = DEFAULT_TARGET_FPS;
        private static long _totalTime = 0;
        private static int _instanceCounter = 0;

        // 性能监控
        private static long _lastFrameTicks = 0;
        private static double _averageFrameTime = MAX_FRAME_TIME_MS;
        private static int _currentFPS = 0;
        private static long _fpsCounterTime = 0;
        private static int _fpsCounter = 0;

        // 取消令牌源
        private static CancellationTokenSource _cancellationTokenSource = new();

        #endregion

        #region 公共属性

        /// <summary>
        /// 获取或设置目标帧率
        /// </summary>
        public static int TargetFPS
        {
            get => _targetFPS;
            set => _targetFPS = Math.Max(1, Math.Min(1000, value));
        }

        /// <summary>
        /// 获取当前帧率
        /// </summary>
        public static int CurrentFPS => _currentFPS;

        /// <summary>
        /// 获取总运行时间（毫秒）
        /// </summary>
        public static long TotalTime => _totalTime;

        /// <summary>
        /// 管理器是否正在运行
        /// </summary>
        public static bool IsRunning => _isRunning;

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化管理器
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            _cancellationTokenSource = new CancellationTokenSource();
            _lastFrameTicks = DateTime.UtcNow.Ticks;
            _isInitialized = true;

            Console.WriteLine("MonoBehaviourManager initialized.");
        }

        /// <summary>
        /// 启动帧更新循环
        /// </summary>
        public static void Start()
        {
            if (!_isInitialized) Initialize();
            if (_isRunning) return;

            _isRunning = true;
            _ = Task.Run(async () => await RunMainLoopAsync());

            Console.WriteLine("MonoBehaviourManager started.");
        }

        /// <summary>
        /// 停止帧更新循环
        /// </summary>
        public static void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            Console.WriteLine("MonoBehaviourManager stopped.");
        }

        /// <summary>
        /// 注册MonoBehaviour实例
        /// </summary>
        public static void RegisterBehavior(IMonoBehavior behavior)
        {
            if (behavior == null) throw new ArgumentNullException(nameof(behavior));

            _addQueue.Enqueue(behavior);

            // 如果管理器正在运行，立即初始化新行为
            if (_isRunning)
            {
                ProcessPendingOperations();
            }
        }

        /// <summary>
        /// 注销MonoBehaviour实例
        /// </summary>
        public static void UnregisterBehavior(IMonoBehavior behavior)
        {
            if (behavior == null) return;

            var weakRef = new WeakReference<IMonoBehavior>(behavior);
            _removeQueue.Enqueue(weakRef);
        }

        /// <summary>
        /// 获取当前注册的行为数量
        /// </summary>
        public static int GetBehaviorCount()
        {
            _behaviorLock.EnterReadLock();
            try
            {
                return _behaviors.Count;
            }
            finally
            {
                _behaviorLock.ExitReadLock();
            }
        }

        #endregion

        #region 主循环和核心逻辑

        private static async Task RunMainLoopAsync()
        {
            var token = _cancellationTokenSource.Token;

            while (_isRunning && !token.IsCancellationRequested)
            {
                var frameStartTicks = DateTime.UtcNow.Ticks;
                var deltaTime = CalculateDeltaTime(frameStartTicks);

                // 处理挂起的操作
                ProcessPendingOperations();

                // 清理无效引用
                CleanupInvalidReferences();

                // 创建帧事件参数
                var frameArgs = new FrameEventArgs
                {
                    DeltaTime = deltaTime,
                    TotalTime = (int)_totalTime,
                    CurrentFPS = _currentFPS,
                    TargetFPS = _targetFPS
                };

                // 执行帧更新
                await ExecuteFrameUpdates(frameArgs, frameStartTicks, token);

                // 更新性能统计
                UpdatePerformanceStats(frameStartTicks);

                // 自适应帧率控制
                await AdaptiveFrameRateControl();
            }
        }

        private static int CalculateDeltaTime(long currentTicks)
        {
            if (_lastFrameTicks == 0) return 0;

            var deltaTicks = currentTicks - _lastFrameTicks;
            return (int)(deltaTicks / TimeSpan.TicksPerMillisecond);
        }

        private static void ProcessPendingOperations()
        {
            // 处理新增行为
            while (_addQueue.TryDequeue(out var behavior))
            {
                var wrapper = new BehaviorWrapper(behavior, Interlocked.Increment(ref _instanceCounter));

                _behaviorLock.EnterWriteLock();
                try
                {
                    _behaviors[behavior.GetHashCode()] = wrapper;
                }
                finally
                {
                    _behaviorLock.ExitWriteLock();
                }

                // 初始化新行为
                SafeExecute(() => behavior.InitializeMonoBehavior(), "InitializeMonoBehavior");
                if (_isRunning)
                {
                    SafeExecute(() => behavior.InvokeAwake(), "InvokeAwake");
                    SafeExecute(() => behavior.InvokeStart(), "InvokeStart");
                }
            }

            // 处理移除行为
            while (_removeQueue.TryDequeue(out var weakRef))
            {
                if (weakRef.TryGetTarget(out var behavior))
                {
                    _behaviorLock.EnterWriteLock();
                    try
                    {
                        _behaviors.TryRemove(behavior.GetHashCode(), out _);
                    }
                    finally
                    {
                        _behaviorLock.ExitWriteLock();
                    }
                }
            }
        }

        private static void CleanupInvalidReferences()
        {
            var invalidKeys = new List<int>();

            _behaviorLock.EnterReadLock();
            try
            {
                foreach (var kvp in _behaviors)
                {
                    if (!kvp.Value.WeakBehavior.TryGetTarget(out _))
                    {
                        invalidKeys.Add(kvp.Key);
                    }
                }
            }
            finally
            {
                _behaviorLock.ExitReadLock();
            }

            if (invalidKeys.Count > 0)
            {
                _behaviorLock.EnterWriteLock();
                try
                {
                    foreach (var key in invalidKeys)
                    {
                        _behaviors.TryRemove(key, out _);
                    }
                }
                finally
                {
                    _behaviorLock.ExitWriteLock();
                }
            }
        }

        private static async Task ExecuteFrameUpdates(FrameEventArgs frameArgs, long frameStartTicks, CancellationToken token)
        {
            var context = new ExecutionContext
            {
                FrameArgs = frameArgs,
                CancellationToken = token,
                FrameStartTicks = frameStartTicks,
                TimeSliceTicks = TimeSpan.TicksPerMillisecond / 1000 * TIME_SLICE_US // 转换为ticks
            };

            // 按执行顺序排序执行
            var behaviors = GetSortedBehaviors();

            await ExecuteLifecyclePhase(behaviors, context, "InvokeUpdate", (behavior, ctx) =>
                behavior.InvokeUpdate(ctx.FrameArgs));

            await ExecuteLifecyclePhase(behaviors, context, "InvokeLateUpdate", (behavior, ctx) =>
                behavior.InvokeLateUpdate(ctx.FrameArgs));

            await ExecuteLifecyclePhase(behaviors, context, "InvokeFixedUpdate", (behavior, ctx) =>
                behavior.InvokeFixedUpdate(ctx.FrameArgs));
        }

        private static List<BehaviorWrapper> GetSortedBehaviors()
        {
            _behaviorLock.EnterReadLock();
            try
            {
                return [.. _behaviors.Values
                    .Where(wrapper => wrapper.WeakBehavior.TryGetTarget(out _))
                    .OrderBy(wrapper => wrapper.ExecutionOrder)];
            }
            finally
            {
                _behaviorLock.ExitReadLock();
            }
        }

        private static async Task ExecuteLifecyclePhase(
            List<BehaviorWrapper> behaviors,
            ExecutionContext context,
            string phaseName,
            Action<IMonoBehavior, ExecutionContext> action)
        {
            foreach (var wrapper in behaviors)
            {
                if (context.CancellationToken.IsCancellationRequested) break;
                if (!wrapper.WeakBehavior.TryGetTarget(out var behavior)) continue;

                var executionStart = DateTime.UtcNow.Ticks;

                try
                {
                    // 时间片控制：如果执行时间超过限制，切换到异步执行
                    if (executionStart - context.FrameStartTicks > context.TimeSliceTicks * 10) // 超过10倍时间片
                    {
                        await Task.Run(() => action(behavior, context));
                    }
                    else
                    {
                        action(behavior, context);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in {phaseName} for behavior {behavior.GetType().Name}: {ex.Message}");
                }

                // 更新执行时间统计
                var executionTime = DateTime.UtcNow.Ticks - executionStart;
                UpdateExecutionTimeStats(wrapper, executionTime);

                // 检查是否应该提前结束本帧（性能保护）
                if (DateTime.UtcNow.Ticks - context.FrameStartTicks > TimeSpan.TicksPerMillisecond * ADAPTIVE_THRESHOLD_MS)
                {
                    break; // 跳过剩余行为，保护帧率
                }
            }
        }

        private static void UpdateExecutionTimeStats(BehaviorWrapper wrapper, long executionTime)
        {
            if (wrapper.AverageExecutionTime == 0)
            {
                wrapper.AverageExecutionTime = executionTime;
            }
            else
            {
                wrapper.AverageExecutionTime = (long)(wrapper.AverageExecutionTime * ADAPTIVE_SMOOTHING +
                    executionTime * (1 - ADAPTIVE_SMOOTHING));
            }
            wrapper.LastExecutionTicks = DateTime.UtcNow.Ticks;
        }

        private static void UpdatePerformanceStats(long frameStartTicks)
        {
            var frameTime = (DateTime.UtcNow.Ticks - frameStartTicks) / TimeSpan.TicksPerMillisecond;
            _averageFrameTime = _averageFrameTime * ADAPTIVE_SMOOTHING + frameTime * (1 - ADAPTIVE_SMOOTHING);

            // 更新FPS计数
            _fpsCounter++;
            var currentTime = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            if (currentTime - _fpsCounterTime >= 1000) // 每秒钟更新一次FPS
            {
                _currentFPS = _fpsCounter;
                _fpsCounter = 0;
                _fpsCounterTime = currentTime;
            }

            _totalTime += (int)frameTime;
            _lastFrameTicks = frameStartTicks;
        }

        private static async Task AdaptiveFrameRateControl()
        {
            var targetFrameTime = 1000.0 / _targetFPS;
            var actualFrameTime = _averageFrameTime;

            if (actualFrameTime < targetFrameTime)
            {
                // 执行过快，需要等待
                var sleepTime = (int)(targetFrameTime - actualFrameTime);
                sleepTime = Math.Max(MIN_FRAME_TIME_MS, Math.Min(sleepTime, MAX_FRAME_TIME_MS));

                await Task.Delay(sleepTime);
            }
            // 如果执行过慢，下一帧会自动加快，不需要特殊处理
        }

        private static void SafeExecute(Action action, string operationName)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in {operationName}: {ex.Message}");
            }
        }

        #endregion
    }
}