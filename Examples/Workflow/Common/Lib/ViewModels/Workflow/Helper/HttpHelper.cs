using System.Diagnostics;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace Demo.ViewModels.Workflow.Helper
{
    public partial class HttpHelper<T> : NodeHelper<T>
        where T : NodeViewModel
    {
        // The data-flow access context (IAccessContext) is reused across two phases:
        //   context.IsCompilePhase == true  → compile-time static check, context.Data is null;
        //   context.IsCompilePhase == false → runtime real-time check, context.Data is this broadcast's payload.
        //   context.Sender / context.Receiver carry the two slots of this edge.
        public override Task<bool> AccessAsync(IAccessContext context, CancellationToken ct)
        {
            return base.AccessAsync(context, ct);
        }

        private NodeViewModel? _viewModel;
        private CommandEventHandler? _startedHandler;
        private CommandEventHandler? _exitedHandler;
        private CommandEventHandler? _enqueuedHandler;
        private CommandEventHandler? _dequeuedHandler;
        private CancellationTokenSource? _runtimeTickerCts;
        private SynchronizationContext? _uiContext;
        private int _activeRuns;
        private int _runCount;
        private int _waitCount;
        private long _counterVersion;
        private long _appliedCounterVersion;
        private long _runningStateVersion;
        private long _appliedRunningStateVersion;

        public override void Install(IWorkflowNodeViewModel node)
        {
            base.Install(node);
            _viewModel = node as NodeViewModel;
            _uiContext = SynchronizationContext.Current;

            if (_viewModel is null)
            {
                return;
            }

            _startedHandler = e =>
            {
                Interlocked.Increment(ref _activeRuns);
                _ = UpdateCountersAsync(
                    IncrementCounter(ref _runCount),
                    ReadCounter(ref _waitCount),
                    Interlocked.Increment(ref _counterVersion));
                StartRuntimeTicker();
            };
            _exitedHandler = e =>
            {
                _ = UpdateCountersAsync(
                    DecrementCounter(ref _runCount),
                    ReadCounter(ref _waitCount),
                    Interlocked.Increment(ref _counterVersion));
                if (Interlocked.Decrement(ref _activeRuns) <= 0)
                {
                    Interlocked.Exchange(ref _activeRuns, 0);
                    StopRuntimeTicker();
                }
            };
            _enqueuedHandler = e =>
            {
                _ = UpdateCountersAsync(
                    ReadCounter(ref _runCount),
                    IncrementCounter(ref _waitCount),
                    Interlocked.Increment(ref _counterVersion));
            };
            _dequeuedHandler = e =>
            {
                _ = UpdateCountersAsync(
                    ReadCounter(ref _runCount),
                    DecrementCounter(ref _waitCount),
                    Interlocked.Increment(ref _counterVersion));
            };

            _viewModel.ReceiveCommand.Started += _startedHandler;
            _viewModel.ReceiveCommand.Exited += _exitedHandler;
            _viewModel.ReceiveCommand.Enqueued += _enqueuedHandler;
            _viewModel.ReceiveCommand.Dequeued += _dequeuedHandler;
        }

        public override void Uninstall(IWorkflowNodeViewModel node)
        {
            if (_viewModel is not null)
            {
                if (_startedHandler is not null)
                {
                    _viewModel.ReceiveCommand.Started -= _startedHandler;
                }

                if (_exitedHandler is not null)
                {
                    _viewModel.ReceiveCommand.Exited -= _exitedHandler;
                }

                if (_enqueuedHandler is not null)
                {
                    _viewModel.ReceiveCommand.Enqueued -= _enqueuedHandler;
                }

                if (_dequeuedHandler is not null)
                {
                    _viewModel.ReceiveCommand.Dequeued -= _dequeuedHandler;
                }
            }

            StopRuntimeTicker();
            _startedHandler = null;
            _exitedHandler = null;
            _enqueuedHandler = null;
            _dequeuedHandler = null;
            _activeRuns = 0;
            Interlocked.Exchange(ref _runCount, 0);
            Interlocked.Exchange(ref _waitCount, 0);
            _ = UpdateCountersAsync(0, 0, Interlocked.Increment(ref _counterVersion));
            _uiContext = null;
            base.Uninstall(node);
            _viewModel = null;
        }

        /// <summary>
        /// Unified data-flow entry point: the compiler drives in order (IRuntimeContext), broadcast delivery (RECV),
        /// and manual Run / AI invocation (EXEC) all flow through <see cref="ReceiveAsync"/>.
        /// </summary>
        public override async Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
        {
            if (_viewModel is null)
            {
                return null;
            }

            // Compiled execution: the engine passes RuntimeContext (IRuntimeContext + ITaskContext) directly.
            // The node's number is fixed at compile time (CompileContext.Order); runtime only advances state, never rewrites the badge.
            // The engine bypasses ReceiveCommand, so command lifecycle events do not fire — self-compensate counters/running state here.
            if (context is IRuntimeContext)
            {
                Interlocked.Increment(ref _activeRuns);
                _ = UpdateCountersAsync(
                    IncrementCounter(ref _runCount),
                    ReadCounter(ref _waitCount),
                    Interlocked.Increment(ref _counterVersion));
                StartRuntimeTicker();
                try
                {
                    await ExecuteCompiledStepAsync(ct);
                }
                finally
                {
                    _ = UpdateCountersAsync(
                        DecrementCounter(ref _runCount),
                        ReadCounter(ref _waitCount),
                        Interlocked.Increment(ref _counterVersion));
                    if (Interlocked.Decrement(ref _activeRuns) <= 0)
                    {
                        Interlocked.Exchange(ref _activeRuns, 0);
                        StopRuntimeTicker();
                    }
                }
                return null;
            }

            // Stateless mode: broadcast (RECV, carrying sender/receiver) and manual Run / AI (EXEC) share the
            // NetworkFlowContext recording, distinguished only by the mode marker.
            var flow = NetworkFlowContext.From(context.Data);
            await ExecuteStepAsync(flow, context.Sender is not null ? "RECV" : "EXEC", ct);

            // Auto-forward downstream: broadcast the result along the output slot to downstream nodes' ReceiveCommand.
            // Default true (AutoBroadcast flag); unchecking falls back to manual Forward step-by-step.
            if (_viewModel.AutoBroadcast)
                await BroadcastAsync(flow, ct);

            return flow;
        }

        /// <summary>Node step under compiled execution: only updates running state/elapsed time, never touches the compile-time-fixed number badge.</summary>
        private async Task ExecuteCompiledStepAsync(CancellationToken ct)
        {
            if (_viewModel is null) return;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await UpdateViewModelStateAsync(
                    status: "Running",
                    duration: $"{_viewModel.DelayMilliseconds} ms",
                    preview: $"Compiled step: simulating {_viewModel.DelayMilliseconds} ms...",
                    error: string.Empty);

                await Task.Delay(_viewModel.DelayMilliseconds, ct);
                stopwatch.Stop();

                await UpdateViewModelStateAsync(
                    status: "Completed",
                    duration: $"{stopwatch.ElapsedMilliseconds} ms",
                    preview: $"{_viewModel.Title} completed after {stopwatch.ElapsedMilliseconds} ms.",
                    error: string.Empty);
            }
            catch (OperationCanceledException ex)
            {
                stopwatch.Stop();
                await UpdateViewModelStateAsync("Canceled", $"{stopwatch.ElapsedMilliseconds} ms", _viewModel.LastResponsePreview, ex.Message);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await UpdateViewModelStateAsync("Failed", $"{stopwatch.ElapsedMilliseconds} ms", _viewModel.LastResponsePreview, ex.Message);
            }
        }

        /// <summary>
        /// Simulates a workload: records the execution trace, updates the node UI state, and delays by DelayMilliseconds.
        /// Shared by compiler mode and stateless mode to avoid two nearly identical state-advance logic paths.
        /// </summary>
        private async Task ExecuteStepAsync(NetworkFlowContext context, string mode, CancellationToken ct)
        {
            if (_viewModel is null)
            {
                return;
            }

            // Non-compiler paths use a global monotonic sequence number, only for the trace text in the execution log.
            // The node's number badge (LastExecutionOrder / LastExecutionTrace) is the compiled run's own order
            // (CompileContext.Order); standalone starts (EXEC/RECV) only record activity logs and never disturb it.
            var executionOrder = (_viewModel.Parent as TreeViewModel)?.NextExecutionSequence()
                ?? context.NextOrder();
            var executionTrace = context.RecordExecution($"{mode} {_viewModel.Title}", out _, executionOrder);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await AppendExecutionLogAsync(executionTrace);

                await UpdateViewModelStateAsync(
                    status: "Running",
                    duration: $"{_viewModel.DelayMilliseconds} ms",
                    preview: $"{mode}: simulating {_viewModel.DelayMilliseconds} ms workload...",
                    error: string.Empty);

                await Task.Delay(_viewModel.DelayMilliseconds, ct);
                stopwatch.Stop();
                context.Set("last.node", _viewModel.Title);
                context.Set("last.duration", stopwatch.ElapsedMilliseconds.ToString());

                // Under compiled execution, the Compiler handles chained delivery; here the parameter object is mutated in place

                await UpdateViewModelStateAsync(
                    status: "Completed",
                    duration: $"{stopwatch.ElapsedMilliseconds} ms",
                    preview: $"{_viewModel.Title} completed after {stopwatch.ElapsedMilliseconds} ms.",
                    error: string.Empty);
            }
            catch (OperationCanceledException ex)
            {
                stopwatch.Stop();
                await UpdateViewModelStateAsync("Canceled", $"{stopwatch.ElapsedMilliseconds} ms", _viewModel.LastResponsePreview, ex.Message);
                Debug.WriteLine($"Request canceled: {ex.Message}");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await UpdateViewModelStateAsync("Failed", $"{stopwatch.ElapsedMilliseconds} ms", _viewModel.LastResponsePreview, ex.Message);
                Debug.WriteLine($"Request failed: {ex.Message}");
            }
        }

        private Task UpdateViewModelStateAsync(string status, string duration, string preview, string error)
        {
            if (_viewModel is null)
            {
                return Task.CompletedTask;
            }

            void UpdateState()
            {
                _viewModel.LastStatus = status;
                _viewModel.LastDuration = duration;
                _viewModel.LastResponsePreview = preview;
                _viewModel.LastError = error;
            }

            return RunOnUiThreadAsync(UpdateState);
        }

        private Task UpdateCountersAsync(int runCount, int waitCount, long version)
        {
            if (_viewModel is null)
            {
                return Task.CompletedTask;
            }

            void UpdateState()
            {
                if (version < Interlocked.Read(ref _appliedCounterVersion))
                {
                    return;
                }

                Interlocked.Exchange(ref _appliedCounterVersion, version);
                _viewModel.RunCount = runCount;
                _viewModel.WaitCount = waitCount;
                if (_viewModel.Parent is TreeViewModel tree)
                {
                    tree.RefreshWorkflowRunningState();
                }
            }

            return RunOnUiThreadAsync(UpdateState);
        }

        private Task AppendExecutionLogAsync(string executionTrace)
        {
            if (_viewModel?.Parent is not TreeViewModel tree)
            {
                return Task.CompletedTask;
            }

            void UpdateState() => tree.AppendExecutionLog(executionTrace);

            return RunOnUiThreadAsync(UpdateState);
        }

        private void StartRuntimeTicker()
        {
            var previous = Interlocked.Exchange(ref _runtimeTickerCts, new CancellationTokenSource());
            previous?.Cancel();
            previous?.Dispose();

            var cts = _runtimeTickerCts;
            if (cts is not null)
            {
                _ = TrackRuntimeAsync(cts.Token);
            }
        }

        private void StopRuntimeTicker()
        {
            var cts = Interlocked.Exchange(ref _runtimeTickerCts, null);
            cts?.Cancel();
            cts?.Dispose();
            _ = UpdateRunningStateAsync(false, null, preserveStatus: true);
        }

        private async Task TrackRuntimeAsync(CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await UpdateRunningStateAsync(true, FormatDuration(stopwatch.Elapsed), preserveStatus: false);
                    await Task.Delay(120, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private Task UpdateRunningStateAsync(bool isRunning, string? duration, bool preserveStatus)
        {
            if (_viewModel is null)
            {
                return Task.CompletedTask;
            }

            var version = Interlocked.Increment(ref _runningStateVersion);

            void UpdateState()
            {
                if (version < Interlocked.Read(ref _appliedRunningStateVersion))
                {
                    return;
                }

                Interlocked.Exchange(ref _appliedRunningStateVersion, version);
                _viewModel.IsRunning = isRunning;
                if (duration is not null)
                {
                    _viewModel.LastDuration = duration;
                }

                if (!preserveStatus && isRunning)
                {
                    _viewModel.LastStatus = "Running";
                }
            }

            return RunOnUiThreadAsync(UpdateState);
        }

        private static int IncrementCounter(ref int value) => Interlocked.Increment(ref value);

        private static int ReadCounter(ref int value) => Interlocked.CompareExchange(ref value, 0, 0);

        private static int DecrementCounter(ref int value)
        {
            while (true)
            {
                var current = ReadCounter(ref value);
                if (current <= 0)
                {
                    return 0;
                }

                var next = current - 1;
                if (Interlocked.CompareExchange(ref value, next, current) == current)
                {
                    return next;
                }
            }
        }

        private Task RunOnUiThreadAsync(Action action)
        {
            if (action is null)
            {
                return Task.CompletedTask;
            }

            var context = _uiContext;
            if (context is null || ReferenceEquals(SynchronizationContext.Current, context))
            {
                action();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Post(_ =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);
            return tcs.Task;
        }

        private static string FormatDuration(TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds < 1)
            {
                return $"{elapsed.TotalMilliseconds:0} ms";
            }

            return $"{elapsed.TotalSeconds:0.00} s";
        }
    }
}
