using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

/// <summary>
/// Logic gate helper: the pure boolean-reduction + gate-operation logic shared by the node's router
/// (<see cref="LogicGateNodeViewModel.ResolveRouteKey"/>) and tests. The node itself is routing-only —
/// ReceiveAsync passes the incoming payload through so the selected branch reads the gate's input.
/// </summary>
public class LogicGateHelper : NodeHelper<LogicGateNodeViewModel>
{
    /// <summary>Routing-only: pass the incoming payload through to the selected branch (do not null the Data).</summary>
    public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
        => Task.FromResult<object?>(ctx.Data);

    /// <summary>Reduces an arbitrary payload to a boolean and applies the gate operation.</summary>
    public static bool Evaluate(object? value, GateOp op)
    {
        var b = ReduceToBool(value);
        return op == GateOp.Not ? !b : b;
    }

    /// <summary>
    /// Payload → bool: bool/number → nonzero; string → parse; a dictionary is searched for a decision field
    /// ("pass", "result", "ok", "value"); anything else → false.
    /// </summary>
    private static bool ReduceToBool(object? value) => value switch
    {
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        float f => f != 0,
        double d => d != 0,
        decimal m => m != 0,
        string s => ParseBoolish(s),
        IDictionary<string, object?> dict => ReadField(dict) ?? false,
        _ => false,
    };

    private static bool ParseBoolish(string s)
    {
        var t = s.Trim();
        if (bool.TryParse(t, out var b)) return b;
        return t is "1" or "true" or "pass" or "ok" or "yes";
    }

    private static bool? ReadField(IDictionary<string, object?> dict)
    {
        foreach (var key in new[] { "pass", "result", "ok", "value" })
            if (dict.TryGetValue(key, out var v) && v is not null && TryBool(v, out var b))
                return b;
        return null;
    }

    private static bool TryBool(object value, out bool result)
    {
        switch (value)
        {
            case bool b: result = b; return true;
            case int i: result = i != 0; return true;
            case long l: result = l != 0; return true;
            case float f: result = f != 0; return true;
            case double d: result = d != 0; return true;
            case decimal m: result = m != 0; return true;
            case string s: result = ParseBoolish(s); return true;
            default: result = false; return false;
        }
    }
}
