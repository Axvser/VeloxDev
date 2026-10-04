namespace Demo.ViewModels;

/// <summary>
/// The payload a node passes downstream on the non-compiler path: a bag of string variables, so a node that
/// forwards on its own can hand the next one a little more than the raw upstream value.
/// <para>
/// It used to carry an execution trail, a scheduled-node set and a record history as well. None of them had a
/// reader — <c>RecordExecution</c>'s caller discarded both its return value and its <c>out</c> parameter, so the
/// only thing it ever did was append to a list nobody looked at. What is left is the part that is used.
/// </para>
/// </summary>
public sealed class NetworkFlowContext
{
    /// <summary>The variables forwarded downstream; keys are case-insensitive.</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a context, seeding its <c>seed</c> variable when a seed is given.</summary>
    public static NetworkFlowContext Create(object? seed = default)
    {
        var context = new NetworkFlowContext();
        if (seed is not null)
        {
            context.Variables["seed"] = seed.ToString() ?? string.Empty;
        }

        return context;
    }

    /// <summary>Returns the parameter itself when it already is a context, otherwise wraps it as a seed.</summary>
    public static NetworkFlowContext From(object? parameter)
        => parameter as NetworkFlowContext ?? Create(parameter);
}
