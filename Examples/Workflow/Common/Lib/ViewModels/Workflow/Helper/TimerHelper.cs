using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

/// <summary>
/// Timer helper: produces the current timestamp as a serializable payload each time the engine drives it.
/// The fan-out to downstream nodes is declared by <see cref="TimerNodeViewModel.GetRouteTable"/> (router);
/// every branch receives this same payload.
/// </summary>
public class TimerHelper : NodeHelper<TimerNodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is null) return Task.FromResult<object?>(null);

        var tick = new Dictionary<string, object?>
        {
            ["time"] = DateTime.Now.ToString("O"),
        };
        Component.LastTick = tick["time"]?.ToString() ?? "-";
        if (ctx is IRuntimeContext rc)
            rc.Log($"→ Timer tick at {tick["time"]}");

        return Task.FromResult<object?>(tick);
    }
}
