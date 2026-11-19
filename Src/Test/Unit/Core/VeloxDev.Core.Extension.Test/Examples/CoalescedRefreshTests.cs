using System.Collections.Generic;
using Demo.ViewModels.Workflow.Helper;

namespace VeloxDev.Core.Extension.Test.Examples;

/// <summary>
/// An Agent turn asks the surface to refresh once per tool call, and a refresh here is total — resolve the named
/// controls, run a layout, recompute the visible region. Those requests have to collapse into one per settle;
/// otherwise the editor crawls for the whole conversation, which is what was reported.
/// </summary>
[TestClass]
public class CoalescedRefreshTests
{
    [TestMethod]
    public void ABurstOfRequests_DoesTheWorkOnce()
    {
        var refreshes = 0;
        var posted = new List<System.Action>();
        var refresh = new CoalescedRefresh(() => refreshes++, posted.Add);

        for (var i = 0; i < 100; i++) refresh.Request();

        Assert.HasCount(1, posted, "a hundred tool calls in one settle queue one refresh, not a hundred");
        posted[0]();
        Assert.AreEqual(1, refreshes);
    }

    [TestMethod]
    public void ARequestAfterTheWorkRan_DoesTheWorkAgain()
    {
        var refreshes = 0;
        var posted = new List<System.Action>();
        var refresh = new CoalescedRefresh(() => refreshes++, posted.Add);

        refresh.Request();
        refresh.Request();
        Assert.HasCount(1, posted);

        posted[0]();
        refresh.Request();

        Assert.HasCount(2, posted, "a later change is a new staleness, and has to be picked up");
        posted[1]();
        Assert.AreEqual(2, refreshes);
    }

    /// <summary>
    /// The queued flag is cleared before the refresh runs, so a request that arrives while it is running is not
    /// swallowed: that request is about a change the running refresh cannot have seen.
    /// </summary>
    [TestMethod]
    public void ARequestMadeDuringTheWork_QueuesAnother()
    {
        var refreshes = 0;
        var posted = new List<System.Action>();
        CoalescedRefresh? refresh = null;
        refresh = new CoalescedRefresh(
            () => { refreshes++; if (refreshes == 1) refresh!.Request(); },
            action => posted.Add(action));

        refresh.Request();
        Assert.AreEqual(0, refreshes, "nothing runs until the surface thread is given the turn");
        Assert.HasCount(1, posted);

        posted[0]();
        Assert.AreEqual(1, refreshes);

        Assert.HasCount(2, posted, "the request made during the first refresh must have queued a second one");
        posted[1]();
        Assert.AreEqual(2, refreshes);
    }
}
