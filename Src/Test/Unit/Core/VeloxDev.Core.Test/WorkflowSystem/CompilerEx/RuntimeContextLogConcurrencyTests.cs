using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// The run keeps writing log lines while something else reads them — a host's UI, and the Agent's
/// <c>GetCompiledRunStatus</c>, which polls a run that is still going.
/// <para>
/// That read is what broke: <c>Logs</c> is an <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>,
/// and enumerating it across a write throws — <c>Enumerable.ToList</c> sizes its array from <c>Count</c> and then
/// <c>CopyTo</c>s, so a line appended (or, with <see cref="RuntimeContext.MaxRetainedLogs"/>, removed) in between
/// leaves the destination array too small:
/// <c>ArgumentOutOfRangeException: Source array was not long enough … (Parameter 'sourceArray')</c>.
/// Wrapped as a failed tool call, that made a <em>finished</em> run look like one that never ends.
/// </para>
/// <para>
/// The trim is turned on here on purpose: dropping the oldest line is what shrinks the collection under a reader,
/// and an append-only list hides half the race.
/// </para>
/// </summary>
[TestClass]
public class RuntimeContextLogConcurrencyTests
{
    [TestMethod]
    public async Task SnapshottingWhileTheRunLogs_NeverThrows()
    {
        var context = new RuntimeContext { MaxRetainedLogs = 16 };
        using var stop = new CancellationTokenSource();

        var writer = Task.Run(() =>
        {
            for (var i = 0; !stop.IsCancellationRequested; i++)
            {
                context.Log("line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        });

        var reads = 0;
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            // 快照必须能在写入进行中被反复取到，且永远不抛。
            _ = context.SnapshotLogs().Length;
            reads++;
        }

        stop.Cancel();
        await writer;

        Assert.IsTrue(reads > 0, "the reader has to have actually run");
        Assert.IsTrue(context.SnapshotLogs().Length <= 16, "the cap still holds under contention");
    }
}
