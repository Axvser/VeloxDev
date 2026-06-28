using VeloxDev.TimeLine;

namespace VeloxDev.Core.Test.TimeLine;

/// <summary>
/// MonoBehaviourManager 操作共享静态状态（_channels 字典），
/// 因此测试不能并行运行。
/// </summary>
[TestClass]
[DoNotParallelize]
public class MonoBehaviourManagerTests
{
    private const string TestChannel = "UseAsyncLoopTestChannel";
    private static readonly string UniqueChannel = $"MBBTest_{Guid.NewGuid():N}";

    /// <summary>
    /// 确保每个测试结束后通道被停止，避免线程泄漏影响后续测试。
    /// </summary>
    [TestCleanup]
    public async Task Cleanup()
    {
        if (MonoBehaviourManager.IsRunning(TestChannel))
            await MonoBehaviourManager.StopAsync(TestChannel);

        // 清理所有该测试类创建的额外通道
        if (MonoBehaviourManager.IsRunning(UniqueChannel))
            await MonoBehaviourManager.StopAsync(UniqueChannel);
    }

    // ───────── SetUseAsyncLoop ─────────

    [TestMethod]
    public void SetUseAsyncLoop_BeforeStart_Succeeds()
    {
        // 通道未启动时设置覆盖 → 不应抛出异常
        MonoBehaviourManager.SetUseAsyncLoop(true, TestChannel);
    }

    [TestMethod]
    public void SetUseAsyncLoop_BeforeStart_MultipleCalls_Succeeds()
    {
        // 多次设置覆盖 → 不应抛出异常
        MonoBehaviourManager.SetUseAsyncLoop(true, TestChannel);
        MonoBehaviourManager.SetUseAsyncLoop(false, TestChannel);
        MonoBehaviourManager.SetUseAsyncLoop(true, TestChannel);
    }

    [TestMethod]
    public async Task SetUseAsyncLoop_AfterStart_ThrowsInvalidOperationException()
    {
        // 使用独立通道避免与其他测试冲突
        const string ch = "MBBTest_AfterStart_Throws";
        MonoBehaviourManager.Start(ch);

        // 通道已启动时设置覆盖 → 应抛出 InvalidOperationException
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

        // 通道停止后设置覆盖 → 不应抛出异常
        MonoBehaviourManager.SetUseAsyncLoop(true, ch);
    }

    [TestMethod]
    public async Task SetUseAsyncLoop_SameChannel_StopThenStart_UsesNewOverride()
    {
        const string ch = "MBBTest_Recycle";

        // 验证停止后修改覆盖再启动不抛异常（覆盖值在启动时生效）
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
        // 清除覆盖 → 不应抛出异常
        MonoBehaviourManager.ClearUseAsyncLoopOverride(TestChannel);
    }

    [TestMethod]
    public void ClearUseAsyncLoopOverride_WithoutSetting_DoesNotThrow()
    {
        // 从未设置覆盖时清除 → 不应抛出异常
        MonoBehaviourManager.ClearUseAsyncLoopOverride(TestChannel);
    }

    [TestMethod]
    public async Task ClearUseAsyncLoopOverride_AfterStart_ThrowsInvalidOperationException()
    {
        const string ch = "MBBTest_Clear_AfterStart";

        // 先设置覆盖
        MonoBehaviourManager.SetUseAsyncLoop(true, ch);
        MonoBehaviourManager.Start(ch);

        // 通道已启动时清除覆盖 → 应抛出 InvalidOperationException
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

        // 通道停止后清除覆盖 → 不应抛出异常
        MonoBehaviourManager.ClearUseAsyncLoopOverride(ch);
    }

    // ───────── 通道隔离 ─────────

    [TestMethod]
    public async Task SetUseAsyncLoop_ChannelIsolation_DifferentChannels()
    {
        const string chA = "MBBTest_Isolation_A";
        const string chB = "MBBTest_Isolation_B";

        MonoBehaviourManager.Start(chA);

        // 通道 A 已运行 → 应抛出异常
        Assert.Throws<InvalidOperationException>(() =>
            MonoBehaviourManager.SetUseAsyncLoop(true, chA));

        // 通道 B 未运行 → 应成功
        MonoBehaviourManager.SetUseAsyncLoop(false, chB);

        await MonoBehaviourManager.StopAsync(chA);
    }

    [TestMethod]
    public async Task ClearUseAsyncLoopOverride_ChannelIsolation_DifferentChannels()
    {
        const string chA = "MBBTest_Isolation_Clear_A";
        const string chB = "MBBTest_Isolation_Clear_B";

        // 通道 B 设置覆盖
        MonoBehaviourManager.SetUseAsyncLoop(true, chB);
        MonoBehaviourManager.Start(chA);

        // 通道 A 未设覆盖但已运行 → 清除也应抛出
        Assert.Throws<InvalidOperationException>(() =>
            MonoBehaviourManager.ClearUseAsyncLoopOverride(chA));

        // 通道 B 未启动但设了覆盖 → 清除应成功
        MonoBehaviourManager.ClearUseAsyncLoopOverride(chB);

        await MonoBehaviourManager.StopAsync(chA);
    }
}
