using System.Collections.Concurrent;
using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// A <see cref="SynchronizationContext"/> the test drives by hand, so "the events were handed to the context, in
/// order" becomes an assertion instead of a race.
/// </summary>
internal sealed class PumpableContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

    internal int PendingCount => _queue.Count;

    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    internal int Pump()
    {
        var pumped = 0;
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
            pumped++;
        }

        return pumped;
    }
}

/// <summary>
/// <see cref="VeloxCommand.EventContext"/> is off by default, so the default path must stay byte-for-byte what it
/// was — that is what the rest of this folder's 60-odd tests already pin. With it set, events must reach the
/// handlers through the context, still in lifecycle order.
/// </summary>
[TestClass]
public class VeloxCommandEventContextTests
{
    [TestMethod]
    public async Task WithoutAnEventContext_EventsAreRaisedInlineAndNothingIsPosted()
    {
        var context = new PumpableContext();
        var command = new VeloxCommand(() => Task.CompletedTask);
        var recorder = new CommandEventRecorder(command);

        await command.ExecuteAsync(null);
        await recorder.FirstExit;

        Assert.AreEqual(0, context.PendingCount, "with EventContext unset there is no context to post to");
        CollectionAssert.AreEqual(
            new[] { CommandEventType.Created, CommandEventType.Started, CommandEventType.Completed, CommandEventType.Exited },
            recorder.Types);
    }

    [TestMethod]
    public async Task WithAnEventContext_EventsArePostedAndReachHandlersInLifecycleOrder()
    {
        var context = new PumpableContext();
        var command = new VeloxCommand(() => Task.CompletedTask) { EventContext = context };
        var recorder = new CommandEventRecorder(command);

        await command.ExecuteAsync(null).WaitAsync(CommandTestKit.Timeout);

        Assert.IsTrue(context.PendingCount > 0, "the events must have been handed to the context");
        Assert.IsEmpty(recorder.Types, "posting is asynchronous, so none may have run yet");

        context.Pump();

        CollectionAssert.AreEqual(
            new[] { CommandEventType.Created, CommandEventType.Started, CommandEventType.Completed, CommandEventType.Exited },
            recorder.Types,
            "posting must preserve the order the events were raised in");
    }

    [TestMethod]
    public async Task WithAnEventContext_WhenAlreadyOnThatContext_EventsAreRaisedInline()
    {
        var context = new PumpableContext();
        var command = new VeloxCommand(() => Task.CompletedTask) { EventContext = context };
        var recorder = new CommandEventRecorder(command);

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            await command.ExecuteAsync(null);
            await recorder.FirstExit;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.AreEqual(0, context.PendingCount, "already on the target context, so there is nothing to marshal");
    }
}
