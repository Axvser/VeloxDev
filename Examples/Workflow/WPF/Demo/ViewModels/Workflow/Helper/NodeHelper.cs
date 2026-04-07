using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem.StandardEx;

namespace Demo.ViewModels.Workflow.Helper;

public partial class NodeHelper : WorkflowHelper.ViewModel.Node
{
    private static readonly HttpClient HttpClient = new();
    private static readonly Regex TemplateRegex = new("\\{\\{\\s*([a-zA-Z0-9_\\.-]+)\\s*\\}\\}", RegexOptions.Compiled);
    private NodeViewModel? _viewModel;
    private CommandEventHandler? _startedHandler;
    private CommandEventHandler? _exitedHandler;
    private CommandEventHandler? _enqueuedHandler;
    private CommandEventHandler? _dequeuedHandler;
    private CancellationTokenSource? _runtimeTickerCts;
    private int _activeRuns;

    public override void Install(IWorkflowNodeViewModel node)
    {
        base.Install(node);
        _viewModel = node as NodeViewModel;

        if (_viewModel is null)
        {
            return;
        }

        _startedHandler = e =>
        {
            Interlocked.Increment(ref _activeRuns);
            _ = UpdateCountersAsync(runDelta: 1, waitDelta: 0);
            StartRuntimeTicker();
        };
        _exitedHandler = e =>
        {
            _ = UpdateCountersAsync(runDelta: -1, waitDelta: 0);
            if (Interlocked.Decrement(ref _activeRuns) <= 0)
            {
                Interlocked.Exchange(ref _activeRuns, 0);
                StopRuntimeTicker();
            }
        };
        _enqueuedHandler = e =>
        {
            _ = UpdateCountersAsync(runDelta: 0, waitDelta: 1);
        };
        _dequeuedHandler = e =>
        {
            _ = UpdateCountersAsync(runDelta: 0, waitDelta: -1);
        };

        _viewModel.WorkCommand.Started += _startedHandler;
        _viewModel.WorkCommand.Exited += _exitedHandler;
        _viewModel.WorkCommand.Enqueued += _enqueuedHandler;
        _viewModel.WorkCommand.Dequeued += _dequeuedHandler;
    }

    public override void Uninstall(IWorkflowNodeViewModel node)
    {
        if (_viewModel is not null)
        {
            if (_startedHandler is not null)
            {
                _viewModel.WorkCommand.Started -= _startedHandler;
            }

            if (_exitedHandler is not null)
            {
                _viewModel.WorkCommand.Exited -= _exitedHandler;
            }

            if (_enqueuedHandler is not null)
            {
                _viewModel.WorkCommand.Enqueued -= _enqueuedHandler;
            }

            if (_dequeuedHandler is not null)
            {
                _viewModel.WorkCommand.Dequeued -= _dequeuedHandler;
            }
        }

        StopRuntimeTicker();
        _startedHandler = null;
        _exitedHandler = null;
        _enqueuedHandler = null;
        _dequeuedHandler = null;
        _activeRuns = 0;
        base.Uninstall(node);
        _viewModel = null;
    }

    public override async Task WorkAsync(object? parameter, CancellationToken ct)
    {
        if (_viewModel is null)
        {
            return;
        }

        var context = NetworkFlowContext.From(parameter);
        var executionTrace = context.RecordExecution($"EXEC {_viewModel.Title}", out var executionOrder);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await UpdateExecutionTraceAsync(executionOrder, executionTrace);
            await AppendExecutionLogAsync(executionTrace);

            var requestUrl = RenderTemplate(_viewModel.Url, context);
            using var request = new HttpRequestMessage(CreateMethod(_viewModel.Method), requestUrl);

            ApplyHeaders(request, _viewModel.Headers, context);
            ApplyBody(request, _viewModel.BodyTemplate, context);

            using var response = await HttpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var responseSummary = Summarize(responseBody);
            stopwatch.Stop();

            var captureKey = string.IsNullOrWhiteSpace(_viewModel.CaptureKey)
                ? NormalizeCaptureKey(_viewModel.Title)
                : NormalizeCaptureKey(_viewModel.CaptureKey);

            context.SetNodeResult(captureKey, _viewModel.Method.ToString().ToUpperInvariant(), requestUrl, (int)response.StatusCode, responseBody, responseSummary);

            await UpdateViewModelStateAsync(
                status: $"{(int)response.StatusCode} {response.StatusCode}",
                duration: $"{stopwatch.ElapsedMilliseconds} ms",
                preview: responseSummary,
                error: string.Empty);

            if (_viewModel.AutoBroadcast && !WorkflowNodeEx.IsOrderedBroadcastInProgress())
            {
                await _viewModel.StandardBroadcastAsync(context, _viewModel.ResolveConfiguredBroadcastMode(WorkflowBroadcastMode.Parallel), ct);
            }
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

    private static HttpMethod CreateMethod(NetworkRequestMethod method)
        => method switch
        {
            NetworkRequestMethod.Get => HttpMethod.Get,
            NetworkRequestMethod.Post => HttpMethod.Post,
            NetworkRequestMethod.Put => HttpMethod.Put,
            NetworkRequestMethod.Patch => HttpMethod.Patch,
            NetworkRequestMethod.Delete => HttpMethod.Delete,
            _ => HttpMethod.Get
        };

    private static void ApplyHeaders(HttpRequestMessage request, string headersText, NetworkFlowContext context)
    {
        foreach (var rawLine in headersText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = RenderTemplate(rawLine, context);
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content ??= new StringContent(string.Empty);
                request.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }
    }

    private static void ApplyBody(HttpRequestMessage request, string bodyTemplate, NetworkFlowContext context)
    {
        if (string.IsNullOrWhiteSpace(bodyTemplate) || request.Method == HttpMethod.Get || request.Method == HttpMethod.Delete)
        {
            return;
        }

        request.Content = new StringContent(RenderTemplate(bodyTemplate, context), Encoding.UTF8, "application/json");
    }

    private static string RenderTemplate(string template, NetworkFlowContext context)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        return TemplateRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return context.Variables.TryGetValue(key, out var value) ? value : string.Empty;
        });
    }

    private static string Summarize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty response)";
        }

        var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 220 ? normalized : normalized[..220] + " ...";
    }

    private static string NormalizeCaptureKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "request";
        }

        return string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? char.ToLowerInvariant(ch) : '_'));
    }

    private Task UpdateViewModelStateAsync(string status, string duration, string preview, string error)
        => RunOnUiThreadAsync(() =>
        {
            if (_viewModel is null)
            {
                return;
            }

            _viewModel.LastStatus = status;
            _viewModel.LastDuration = duration;
            _viewModel.LastResponsePreview = preview;
            _viewModel.LastError = error;
        });

    private Task UpdateExecutionTraceAsync(int executionOrder, string executionTrace)
        => RunOnUiThreadAsync(() =>
        {
            if (_viewModel is null)
            {
                return;
            }

            _viewModel.LastExecutionOrder = executionOrder;
            _viewModel.LastExecutionTrace = executionTrace;
        });

    private Task AppendExecutionLogAsync(string executionTrace)
        => RunOnUiThreadAsync(() =>
        {
            if (_viewModel?.Parent is TreeViewModel tree)
            {
                tree.AppendExecutionLog(executionTrace);
            }
        });

    private Task UpdateCountersAsync(int runDelta, int waitDelta)
        => RunOnUiThreadAsync(() =>
        {
            if (_viewModel is null)
            {
                return;
            }

            if (runDelta != 0)
            {
                _viewModel.RunCount = Math.Max(0, _viewModel.RunCount + runDelta);
            }

            if (waitDelta != 0)
            {
                _viewModel.WaitCount = Math.Max(0, _viewModel.WaitCount + waitDelta);
            }
        });

    private void StartRuntimeTicker()
    {
        var previous = Interlocked.Exchange(ref _runtimeTickerCts, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();

        var cts = _runtimeTickerCts;
        _ = TrackRuntimeAsync(cts.Token);
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
        => RunOnUiThreadAsync(() =>
        {
            if (_viewModel is null)
            {
                return;
            }

            _viewModel.IsRunning = isRunning;
            if (duration is not null)
            {
                _viewModel.LastDuration = duration;
            }

            if (!preserveStatus && isRunning)
            {
                _viewModel.LastStatus = "Running";
            }
        });

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
        {
            return $"{elapsed.TotalMilliseconds:0} ms";
        }

        return $"{elapsed.TotalSeconds:0.00} s";
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}
