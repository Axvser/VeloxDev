using VeloxDev.TimeLine;

namespace VeloxDev.Core.Test.TimeLine;

/// <summary>
/// TickManager operates on shared static state (the _channels dictionary),
/// so these tests cannot run in parallel.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TickManagerTests
{
    private const string TestChannel = "UseAsyncLoopTestChannel";
    private static readonly string UniqueChannel = $"MBBTest_{Guid.NewGuid():N}";

    /// <summary>
    /// Ensures every channel is stopped after each test, so thread leakage cannot affect later tests.
    /// </summary>
    [TestCleanup]
    public async Task Cleanup()
    {
        if (TickManager.IsRunning(TestChannel))
            await TickManager.StopAsync(TestChannel);

        // Clean up any extra channels this test class created
        if (TickManager.IsRunning(UniqueChannel))
            await TickManager.StopAsync(UniqueChannel);
    }

    // ───────── SetUseAsyncLoop ─────────

    [TestMethod]
    public void SetUseAsyncLoop_BeforeStart_Succeeds()
    {
        // Setting the override before the channel starts → must not throw
        TickManager.SetUseAsyncLoop(true, TestChannel);
    }

    [TestMethod]
    public void SetUseAsyncLoop_BeforeStart_MultipleCalls_Succeeds()
    {
        // Setting the override multiple times → must not throw
        TickManager.SetUseAsyncLoop(true, TestChannel);
        TickManager.SetUseAsyncLoop(false, TestChannel);
        TickManager.SetUseAsyncLoop(true, TestChannel);
    }

    [TestMethod]
    public async Task SetUseAsyncLoop_AfterStart_ThrowsInvalidOperationException()
    {
        // Use a dedicated channel to avoid colliding with other tests
        const string ch = "MBBTest_AfterStart_Throws";
        TickManager.Start(ch);

        // Setting the override after the channel started → must throw InvalidOperationException
        Assert.Throws<InvalidOperationException>(() =>
            TickManager.SetUseAsyncLoop(true, ch));

        await TickManager.StopAsync(ch);
    }

    [TestMethod]
    public async Task SetUseAsyncLoop_AfterStop_Succeeds()
    {
        const string ch = "MBBTest_AfterStop";
        TickManager.Start(ch);
        await TickManager.StopAsync(ch);

        // Setting the override after the channel stopped → must not throw
        TickManager.SetUseAsyncLoop(true, ch);
    }

    [TestMethod]
    public async Task SetUseAsyncLoop_SameChannel_StopThenStart_UsesNewOverride()
    {
        const string ch = "MBBTest_Recycle";

        // Verifies that modifying the override after a stop and restarting does not throw (the override takes effect at start)
        TickManager.SetUseAsyncLoop(true, ch);
        TickManager.Start(ch);
        await TickManager.StopAsync(ch);

        TickManager.SetUseAsyncLoop(false, ch);
        TickManager.Start(ch);
        await TickManager.StopAsync(ch);
    }

    // ───────── ClearUseAsyncLoopOverride ─────────

    [TestMethod]
    public void ClearUseAsyncLoopOverride_BeforeStart_Succeeds()
    {
        TickManager.SetUseAsyncLoop(true, TestChannel);
        // Clearing the override → must not throw
        TickManager.ClearUseAsyncLoopOverride(TestChannel);
    }

    [TestMethod]
    public void ClearUseAsyncLoopOverride_WithoutSetting_DoesNotThrow()
    {
        // Clearing when no override was ever set → must not throw
        TickManager.ClearUseAsyncLoopOverride(TestChannel);
    }

    [TestMethod]
    public async Task ClearUseAsyncLoopOverride_AfterStart_ThrowsInvalidOperationException()
    {
        const string ch = "MBBTest_Clear_AfterStart";

        // Set the override first
        TickManager.SetUseAsyncLoop(true, ch);
        TickManager.Start(ch);

        // Clearing the override after the channel started → must throw InvalidOperationException
        Assert.Throws<InvalidOperationException>(() =>
            TickManager.ClearUseAsyncLoopOverride(ch));

        await TickManager.StopAsync(ch);
    }

    [TestMethod]
    public async Task ClearUseAsyncLoopOverride_AfterStop_Succeeds()
    {
        const string ch = "MBBTest_Clear_AfterStop";

        TickManager.SetUseAsyncLoop(true, ch);
        TickManager.Start(ch);
        await TickManager.StopAsync(ch);

        // Clearing the override after the channel stopped → must not throw
        TickManager.ClearUseAsyncLoopOverride(ch);
    }

    // ───────── Channel isolation ─────────

    [TestMethod]
    public async Task SetUseAsyncLoop_ChannelIsolation_DifferentChannels()
    {
        const string chA = "MBBTest_Isolation_A";
        const string chB = "MBBTest_Isolation_B";

        TickManager.Start(chA);

        // Channel A is running → must throw
        Assert.Throws<InvalidOperationException>(() =>
            TickManager.SetUseAsyncLoop(true, chA));

        // Channel B is not running → must succeed
        TickManager.SetUseAsyncLoop(false, chB);

        await TickManager.StopAsync(chA);
    }

    [TestMethod]
    public async Task ClearUseAsyncLoopOverride_ChannelIsolation_DifferentChannels()
    {
        const string chA = "MBBTest_Isolation_Clear_A";
        const string chB = "MBBTest_Isolation_Clear_B";

        // Set an override on channel B
        TickManager.SetUseAsyncLoop(true, chB);
        TickManager.Start(chA);

        // Channel A has no override but is running → clearing must also throw
        Assert.Throws<InvalidOperationException>(() =>
            TickManager.ClearUseAsyncLoopOverride(chA));

        // Channel B is not started but has an override → clearing must succeed
        TickManager.ClearUseAsyncLoopOverride(chB);

        await TickManager.StopAsync(chA);
    }
}
