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
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static NetworkFlowContext Create(object? seed = default)
    {
        var context = new NetworkFlowContext();
        if (seed is not null)
        {
            context.Variables["seed"] = seed.ToString() ?? string.Empty;
        }

        return context;
    }

    public static NetworkFlowContext From(object? parameter)
        => parameter as NetworkFlowContext ?? Create(parameter);
}
