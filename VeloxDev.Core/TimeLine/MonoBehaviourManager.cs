using System.Collections.Concurrent;
using System.Diagnostics;
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

        #region 私有字段

        private static readonly ConcurrentDictionary<int, BehaviorWrapper> _behaviors = new();
        private static readonly ConcurrentQueue<IMonoBehaviour> _addQueue = new();
        private static readonly ConcurrentQueue<IMonoBehaviour> _removeQueue = new();
        private static readonly ConcurrentQueue<ConfigChangeRequest> _configQueue = new();

        // 线程间通信
        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private static readonly BlockingCollection<FrameEventArgs> _fixedUpdateEvents = [];

        // 性能参数
        private static volatile bool _isRunning = false;
        private static volatile bool _isPaused = false;
        private static float _timeScale = 1.0f;
        private static int _targetFPS = 60;
        private static int _fixedUpdateInterval = 16; // 默认60Hz
        private static long _totalTimeMs = 0;
        private static long _lastFrameTime = 0;
        private static int _currentFPS = 0;
        private static int _fpsCounter = 0;
        private static long _fpsLastUpdateTime = 0;
        private static long _totalFrames = 0;
        private static int _instanceCounter = 0;

        // 线程控制
        private static CancellationTokenSource _mainCancellationTokenSource = new();
        private static Task? _updateTask;
        private static Task? _fixedUpdateTask;

        // 缓存
        private static readonly ThreadSafeFrameEventArgs _cachedFrameArgs = new();

        // 事件
        public static event EventHandler? OnSystemStarted;
        public static event EventHandler? OnSystemPaused;
        public static event EventHandler? OnSystemResumed;
        public static event EventHandler? OnSystemStopped;

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

        #region 配置修改API

        public static void SetTargetFPS(int fps)
        {
            if (fps < 1 || fps > 1000) return;
            _configQueue.Enqueue(new ConfigChangeRequest { TargetFPS = fps });
        }

        public static void SetFixedUpdateInterval(int intervalMs)
        {
            if (intervalMs < 1 || intervalMs > 1000) return;
            _configQueue.Enqueue(new ConfigChangeRequest { FixedUpdateInterval = intervalMs });
        }

        public static void SetTimeScale(float timeScale)
        {
            if (timeScale < 0) timeScale = 0;
            _configQueue.Enqueue(new ConfigChangeRequest { TimeScale = timeScale });
        }

        public static void SetPreUpdateCallback(Action<FrameEventArgs> callback)
        {
            _configQueue.Enqueue(new ConfigChangeRequest { PreUpdateCallback = callback });
        }

        public static void SetPostUpdateCallback(Action<FrameEventArgs> callback)
        {
            _configQueue.Enqueue(new ConfigChangeRequest { PostUpdateCallback = callback });
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

            // 启动 FixedUpdate 线程（物理线程）
            _fixedUpdateTask = Task.Run(() => FixedUpdateLoop(_mainCancellationTokenSource.Token),
                _mainCancellationTokenSource.Token);

            // 启动 Update 线程（主渲染线程）
            _updateTask = Task.Run(() => UpdateLoop(_mainCancellationTokenSource.Token),
                _mainCancellationTokenSource.Token);

            OnSystemStarted?.Invoke(null, EventArgs.Empty);
            Debug.WriteLine("MonoBehaviourManager started with multi-threading.");
        }

        public static async void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _isPaused = false;
            _mainCancellationTokenSource.Cancel();

            try
            {
                if (_updateTask != null) await _updateTask;
                if (_fixedUpdateTask != null) await _fixedUpdateTask;
            }
            catch (OperationCanceledException)
            {
                // 任务取消是预期的
            }

            _fixedUpdateEvents.Dispose();
            OnSystemStopped?.Invoke(null, EventArgs.Empty);
            Debug.WriteLine("MonoBehaviourManager stopped.");
        }

        public static void Pause()
        {
            if (!_isRunning || _isPaused) return;

            _isPaused = true;
            OnSystemPaused?.Invoke(null, EventArgs.Empty);
            Debug.WriteLine("MonoBehaviourManager paused.");
        }

        public static void Resume()
        {
            if (!_isRunning || !_isPaused) return;

            _isPaused = false;
            OnSystemResumed?.Invoke(null, EventArgs.Empty);
            Debug.WriteLine("MonoBehaviourManager resumed.");
        }

        public static void TogglePause()
        {
            if (_isPaused)
                Resume();
            else
                Pause();
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        public static void RegisterBehaviour(IMonoBehaviour behavior)
        {
            ExecuteOnMainThread(() =>
            {
                if (behavior == null)
                {
                    Debug.WriteLine("Warning: Attempted to register null behavior");
                    return;
                }

                _addQueue.Enqueue(behavior);
            });
        }

        public static void UnregisterBehaviour(IMonoBehaviour behavior)
        {
            ExecuteOnMainThread(() =>
            {
                if (behavior == null)
                {
                    Debug.WriteLine("Warning: Attempted to unregister null behavior");
                    return;
                }

                _removeQueue.Enqueue(behavior);
            });
        }

        #endregion

        #region FixedUpdate 线程（物理线程）

        private static async Task FixedUpdateLoop(CancellationToken token)
        {
            Debug.WriteLine("FixedUpdate thread started");
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

                        // 执行 FixedUpdate（物理计算）
                        await ExecuteBehaviorsFixedUpdate(fixedFrameArgs, token);

                        // 将事件发送到主线程
                        if (!fixedFrameArgs.Handled)
                        {
                            _fixedUpdateEvents.Add(fixedFrameArgs, token);
                        }

                        lastFixedUpdateTime = currentTime;
                    }

                    await Task.Delay(1, token); // 减少CPU占用
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("FixedUpdate thread cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in FixedUpdate thread: {ex.Message}");
            }

            Debug.WriteLine("FixedUpdate thread stopped");
        }

        private static FrameEventArgs CreateFixedFrameEventArgs(int deltaTime)
        {
            return new FrameEventArgs
            {
                DeltaTime = (int)(deltaTime * _timeScale),
                TotalTime = (int)_totalTimeMs,
                CurrentFPS = _currentFPS,
                TargetFPS = _targetFPS,
                Handled = false
            };
        }

        #endregion

        #region Update 线程（主线程）

        private static async Task UpdateLoop(CancellationToken token)
        {
            Debug.WriteLine("Update thread started");

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

                    // 处理主线程任务（行为注册/注销等）
                    ProcessMainThreadOperations();

                    var deltaTime = CalculateDeltaTime(frameStartTime);
                    if (deltaTime <= 0)
                    {
                        await Task.Delay(1, token);
                        continue;
                    }

                    var frameArgs = CreateFrameEventArgs(deltaTime);

                    // 处理来自 FixedUpdate 线程的事件
                    ProcessFixedUpdateEvents();

                    // 执行 Update
                    await ExecuteBehaviorsUpdate(frameArgs, token);

                    // 执行 LateUpdate
                    await ExecuteBehaviorsLateUpdate(frameArgs, token);

                    UpdatePerformanceStats(frameStartTime, deltaTime);
                    await FrameRateControl(frameStartTime, token);

                    _totalFrames++;
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Update thread cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Update thread: {ex.Message}");
            }

            Debug.WriteLine("Update thread stopped");
        }

        private static void ProcessFixedUpdateEvents()
        {
            while (_fixedUpdateEvents.TryTake(out var fixedEvent))
            {
                Debug.WriteLine($"FixedUpdate event received: DeltaTime={fixedEvent.DeltaTime}");
            }
        }

        private static FrameEventArgs CreateFrameEventArgs(int deltaTime)
        {
            _cachedFrameArgs.DeltaTime = (int)(deltaTime * _timeScale);
            _cachedFrameArgs.TotalTime = (int)_totalTimeMs;
            _cachedFrameArgs.CurrentFPS = _currentFPS;
            _cachedFrameArgs.TargetFPS = _targetFPS;
            _cachedFrameArgs.Handled = false;

            return _cachedFrameArgs;
        }

        #endregion

        #region 行为执行方法

        private static async Task ExecuteBehaviorsUpdate(FrameEventArgs frameArgs, CancellationToken token)
        {
            await ExecuteBehaviorsMethod(wrapper =>
            {
                lock (wrapper.LockObject)
                {
                    wrapper.Behavior.InvokeUpdate(frameArgs);
                }
            }, "Update", frameArgs, token);
        }

        private static async Task ExecuteBehaviorsLateUpdate(FrameEventArgs frameArgs, CancellationToken token)
        {
            await ExecuteBehaviorsMethod(wrapper =>
            {
                lock (wrapper.LockObject)
                {
                    wrapper.Behavior.InvokeLateUpdate(frameArgs);
                }
            }, "LateUpdate", frameArgs, token);
        }

        private static async Task ExecuteBehaviorsFixedUpdate(FrameEventArgs frameArgs, CancellationToken token)
        {
            await ExecuteBehaviorsMethod(wrapper =>
            {
                lock (wrapper.LockObject)
                {
                    wrapper.Behavior.InvokeFixedUpdate(frameArgs);
                }
            }, "FixedUpdate", frameArgs, token);
        }

        private static async Task ExecuteBehaviorsMethod(Action<BehaviorWrapper> action, string methodName,
            FrameEventArgs frameArgs, CancellationToken token)
        {
            if (_behaviors.IsEmpty || frameArgs.Handled || token.IsCancellationRequested)
                return;

            var wrappers = _behaviors.Values
                .OrderBy(w => w.ExecutionOrder)
                .ToArray();

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
                    Debug.WriteLine($"Error in {methodName} for {wrapper.Behavior.GetType().Name}: {ex.Message}");
                }
            }
        }

        #endregion

        #region 辅助方法

        private static void ProcessMainThreadOperations()
        {
            // 处理主线程队列中的任务
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
                if (config.TargetFPS.HasValue) _targetFPS = config.TargetFPS.Value;
                if (config.FixedUpdateInterval.HasValue) _fixedUpdateInterval = config.FixedUpdateInterval.Value;
                if (config.TimeScale.HasValue) _timeScale = config.TimeScale.Value;
                if (config.PauseState.HasValue) _isPaused = config.PauseState.Value;
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

                    // 在主线程中执行初始化
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

        private static int GetBehaviorHash(IMonoBehaviour behavior)
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

        private static async Task FrameRateControl(long frameStartTime, CancellationToken token)
        {
            var targetFrameTime = 1000.0 / _targetFPS;
            var elapsed = GetCurrentTimestamp() - frameStartTime;

            if (elapsed < targetFrameTime)
            {
                var sleepTime = (int)(targetFrameTime - elapsed);
                if (sleepTime > 0) await Task.Delay(sleepTime, token);
            }
        }

        private static void SafeExecute(Action action, string methodName, IMonoBehaviour behavior)
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