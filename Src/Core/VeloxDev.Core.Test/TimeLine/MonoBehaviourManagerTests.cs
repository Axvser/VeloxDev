using VeloxDev.TimeLine;

namespace VeloxDev.Core.Test.TimeLine;

/// <summary>
/// MonoBehaviourManager operates on shared static state (the _channels dictionary),
/// so these tests cannot run in parallel.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MonoBehaviourManagerTests
{
    private const string TestChannel = "UseAsyncLoopTestChannel";
    private static readonly string UniqueChannel = $"MBBTest_{Guid.NewGuid():N}";

    /// <summary>
    /// Ensures every channel is stopped after each test, so thread leakage cannot affect later tests.
    /// </summary>
    [TestCleanup]
    public async Task Cleanup()
    {
        if (MonoBehaviourManager.IsRunning(TestChannel))
            await MonoBehaviourManager.StopAsync(TestChannel);

        // Clean up any extra channels this test class created
        if (MonoBehaviourManager.IsRunning(UniqueChannel))
            await MonoBehaviourManager.StopAsync(UniqueChannel);
    }

    // ───────── SetUseAsyncLoop ─────────

    [TestMethod]
    public void SetUseAsyncLoop_BeforeStart_Succeeds()
    {
        // Setting the override before the channel starts → must not throw
        MonoBehaviourManager.SetUseAsyncLoop(true, TestChannel);
    }

    [TestMethod]
    public void SetUseAsyncLoop_BeforeStart_MultipleCalls_Succeeds()
    {
        // Setting the override multiple times → must not throw
        MonoBehaviourManager.SetUseAsyncLoop(true, TestChannel);
        MonoBehaviourManager.SetUseAsyncLoop(false, TestChannel);
        MonoBehaviourManager.SetUseAsyncLoop(true, TestChannel);
    }

    [TestMethod]
    public async Task SetUseAsyncLoop_AfterStart_ThrowsInvalidOperationException()
    {
        // Use a dedicated channel to avoid colliding with other tests
        const string ch = "MBBTest_AfterStart_Throws";
        MonoBehaviourManager.Start(ch);

        // Setting the override after the channel started → must throw InvalidOperationException
        Assert.Throws<InvalidOperationException>(() =>
            MonoBehaviourManager.SetUseAsyncLoop(true, ch));

        await MonoBehaviourManager.StopAsync(ch);
    }

    [TestMethod]
    public async Task SetUseAsyncLoop_AfterStop_Succeeds()
    {
        const string ch = "MBBTest_AfterStop";
        MonoBehaviourManager.Start(ch);
        await MonoBehaviourManager.StopAsync(ch);

        // Setting the override after the channel stopped → must not throw
        MonoBehaviourManager.SetUseAsyncLoop(true, ch);
    }

    [TestMethod]
    public async Task SetUseAsyncLoop_SameChannel_StopThenStart_UsesNewOverride()
    {
        const string ch = "MBBTest_Recycle";

        // Verifies that modifying the override after a stop and restarting does not throw (the override takes effect at start)
        MonoBehaviourManager.SetUseAsyncLoop(true, ch);
        MonoBehaviourManager.Start(ch);
        await MonoBehaviourManager.StopAsync(ch);

        MonoBehaviourManager.SetUseAsyncLoop(false, ch);
        MonoBehaviourManager.Start(ch);
        await MonoBehaviourManager.StopAsync(ch);
    }

    // ───────── ClearUseAsyncLoopOverride ─────────

    [TestMethod]
    public void ClearUseAsyncLoopOverride_BeforeStart_Succeeds()
    {
        MonoBehaviourManager.SetUseAsyncLoop(true, TestChannel);
        // Clearing the override → must not throw
        MonoBehaviourManager.ClearUseAsyncLoopOverride(TestChannel);
    }

    [TestMethod]
    public void ClearUseAsyncLoopOverride_WithoutSetting_DoesNotThrow()
    {
        // Clearing when no override was ever set → must not throw
        MonoBehaviourManager.ClearUseAsyncLoopOverride(TestChannel);
    }

    [TestMethod]
    public async Task ClearUseAsyncLoopOverride_AfterStart_ThrowsInvalidOperationException()
    {
        const string ch = "MBBTest_Clear_AfterStart";

        // Set the override first
        MonoBehaviourManager.SetUseAsyncLoop(true, ch);
        MonoBehaviourManager.Start(ch);

        // Clearing the override after the channel started → must throw InvalidOperationException
        Assert.Throws<InvalidOperationException>(() =>
            MonoBehaviourManager.ClearUseAsyncLoopOverride(ch));

        await MonoBehaviourManager.StopAsync(ch);
    }

    [TestMethod]
    public async Task ClearUseAsyncLoopOverride_AfterStop_Succeeds()
    {
        const string ch = "MBBTest_Clear_AfterStop";

        MonoBehaviourManager.SetUseAsyncLoop(true, ch);
        MonoBehaviourManager.Start(ch);
        await MonoBehaviourManager.StopAsync(ch);

        // Clearing the override after the channel stopped → must not throw
        MonoBehaviourManager.ClearUseAsyncLoopOverride(ch);
    }

    // ───────── Channel isolation ─────────

    [TestMethod]
    public async Task SetUseAsyncLoop_ChannelIsolation_DifferentChannels()
    {
        const string chA = "MBBTest_Isolation_A";
        const string chB = "MBBTest_Isolation_B";

        MonoBehaviourManager.Start(chA);

        // Channel A is running → must throw
        Assert.Throws<InvalidOperationException>(() =>
            MonoBehaviourManager.SetUseAsyncLoop(true, chA));

        // Channel B is not running → must succeed
        MonoBehaviourManager.SetUseAsyncLoop(false, chB);

        await MonoBehaviourManager.StopAsync(chA);
    }

    [TestMethod]
    public async Task ClearUseAsyncLoopOverride_ChannelIsolation_DifferentChannels()
    {
        const string chA = "MBBTest_Isolation_Clear_A";
        const string chB = "MBBTest_Isolation_Clear_B";

        // Set an override on channel B
        MonoBehaviourManager.SetUseAsyncLoop(true, chB);
        MonoBehaviourManager.Start(chA);

        // Channel A has no override but is running → clearing must also throw
        Assert.Throws<InvalidOperationException>(() =>
            MonoBehaviourManager.ClearUseAsyncLoopOverride(chA));

        // Channel B is not started but has an override → clearing must succeed
        MonoBehaviourManager.ClearUseAsyncLoopOverride(chB);

        await MonoBehaviourManager.StopAsync(chA);
    }
}
