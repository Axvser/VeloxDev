using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.Pipelines;

/// <summary>
/// Runs a pipeline's own bookkeeping on the thread that owns the bound state.
/// <para>
/// Events arrive on whichever thread the agent run is on, but a transcript is usually bound to a UI thread.
/// Awaiting the post (rather than firing it off) is deliberate: two fragments published in order must land
/// in that order, and a host that reads the transcript straight after a run has to see the finished text
/// rather than a queue that has not drained. It is also what lets the tests assert without a dispatcher —
/// with no context captured everything runs inline.
/// </para>
/// </summary>
internal static class PipelineDispatch
{
    /// <summary>Runs <paramref name="action"/> on <paramref name="context"/>, or inline when there is none.</summary>
    public static async ValueTask RunAsync(SynchronizationContext? context, Action action)
    {
        if (context is null || ReferenceEquals(context, SynchronizationContext.Current))
        {
            action();
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(_ =>
        {
            try
            {
                action();
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, null);

        await completion.Task.ConfigureAwait(false);
    }
}
