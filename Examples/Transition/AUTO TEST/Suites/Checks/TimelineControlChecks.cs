using VeloxDev.AT.Drivers;
using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Drives the demo's timeline-control row — pause, resume, slow, fast, reverse, next pass — and checks what the
/// running animation actually did.
/// </summary>
/// <remarks>
/// The half neither of the other suites can reach. The sampler suite verifies an <c>ISampler</c>'s arithmetic and the
/// load-mode suite verifies how a load is started; this one verifies that a running animation can be <em>steered</em>
/// while it runs, which is the part that had no surface at all until the timeline gained one.
/// <para>
/// <b>The assertions are deliberately about behaviour, not about the buttons existing.</b> A click that reached the
/// handler but did nothing would still light up a "the click worked" check, so each case reads the four fields the
/// demo publishes back — <c>paused</c>, <c>rate</c>, <c>pos</c>, <c>cycle</c> — and asks whether the animation moved
/// the way the operation says it should. What is <em>not</em> asserted here is the exact arithmetic: the pass-local
/// seek, the rewind-to-start rule and the exclusion of paused time are pinned by the unit tests, on a machine that
/// can measure them precisely. This suite is here to prove the whole path works on a real surface.
/// </para>
/// <para>
/// One case per platform, and a shared demo per platform, so the launch cost is paid once for the run.
/// </para>
/// </remarks>
internal static class TimelineControlChecks
{
    /// <summary>How long an operation may take to show up in the published state.</summary>
    private static readonly TimeSpan ReactionWindow = TimeSpan.FromSeconds(3);

    /// <summary>How long to wait before reading a frozen position, so the loop has reached its gate.</summary>
    private static readonly TimeSpan FrameSettle = TimeSpan.FromMilliseconds(250);

    /// <summary>How long apart two samples are taken when checking that something has stopped moving.</summary>
    private static readonly TimeSpan QuietWindow = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Drives the timeline-control row of one platform, through a demo the caller already owns.
    /// </summary>
    internal static void Run(IDemoDriver driver, string platform)
    {
        driver.Settle();

        var failures = new List<string>();

        // 这一排作用在加载模式那三块长动画上，所以先把它们起来 —— 一行 900ms 的过冲暂停与不暂停看不出区别。
        driver.Click("over.btn.load.main");
        driver.WaitFor(state => state.Number("pos") > 0d, ReactionWindow,
            $"{platform}: 加载模式那三块长动画没有跑起来，时间轴控制没有作用对象");

        if (!driver.HasControl("over.btn.pause"))
        {
            // 缺一个按钮就没法继续，而且这是"demo 面被动过"这一类失败，值得单独报。
            Assert.Fail($"{platform}: 时间轴控制那一排不在，over.btn.pause 找不到。");
        }

        // ── 暂停：位置必须冻住 ────────────────────────────────────────────────
        driver.Show("暂停：位置必须冻住，且保持冻住");
        driver.Click("over.btn.pause");
        driver.WaitFor(state => state.Number("paused") == 1d, ReactionWindow,
            $"{platform}: 点了暂停，但 over.state 里的 paused 没有变成 1");

        // 进去那一帧是"暂停那一刻"的位置，比上一帧略前，所以停稳了再取基准。
        Thread.Sleep((int)FrameSettle.TotalMilliseconds);
        var frozen = driver.Read();
        Thread.Sleep((int)QuietWindow.TotalMilliseconds);
        var stillFrozen = driver.Read();

        if (stillFrozen.Number("paused") != 1d)
        {
            failures.Add("暂停没有保持住：等了 " + QuietWindow.TotalMilliseconds + "ms 之后 paused 又回到了 0。");
        }
        if (Math.Abs(stillFrozen.Number("pos") - frozen.Number("pos")) > 0d)
        {
            failures.Add($"暂停期间位置仍在推进：pos {frozen.Number("pos")} → {stillFrozen.Number("pos")}。");
        }

        // ── 恢复：位置必须重新推进 ────────────────────────────────────────────
        driver.Show("恢复：位置必须重新推进");
        driver.Click("over.btn.resume");
        driver.WaitFor(state => state.Number("paused") == 0d, ReactionWindow,
            $"{platform}: 点了恢复，但 paused 没有回到 0");

        var beforeResume = driver.Read().Number("pos");
        Thread.Sleep((int)QuietWindow.TotalMilliseconds);
        var afterResume = driver.Read().Number("pos");
        if (afterResume == beforeResume)
        {
            failures.Add("恢复之后位置没有再推进。");
        }

        // ── 慢速：走同样长的墙钟时间，位移必须明显少于它 ──────────────────────
        driver.Show("慢速 ×0.25：同样长的墙钟里走得更少");
        driver.Click("over.btn.rate.slow");
        var slow = driver.WaitFor(state => Math.Abs(state.Number("rate") - 0.25d) < 1e-6, ReactionWindow,
            $"{platform}: 点了慢速，但 rate 没有回读成 0.25");

        Thread.Sleep((int)QuietWindow.TotalMilliseconds);
        var slowMoved = driver.Read().Number("pos") - slow.Number("pos");
        // 0.25 倍速下，700ms 的墙钟只该走出四分之一左右。夹在 0.6 倍以内就足以把"变速没生效"和"生效了"分开，
        // 又不受平台调度抖动的干扰；程回绕只会让位移更小，不会造成假通过之外的误判（0.25 倍速走不到一程）。
        if (slowMoved > QuietWindow.TotalMilliseconds * 0.6d)
        {
            failures.Add($"0.25 倍速下 {QuietWindow.TotalMilliseconds}ms 里走了 {slowMoved}ms，变速看起来没有生效。");
        }

        // ── 指程：程计数器必须往前走 ──────────────────────────────────────────
        //
        // 放在变速之前：0.25 倍速下走完一程要八秒，所以 seek 之后 cycle 不会自己再动，读到的就是 seek 自己写的。
        driver.Show("指到下一程：程计数器必须往前走");
        var beforeSeek = driver.Read();
        driver.Click("over.btn.seek.next");

        try
        {
            driver.WaitFor(state => state.Number("cycle") > beforeSeek.Number("cycle"), ReactionWindow,
                $"{platform}: 点了下一程，但 cycle 没有推进");
        }
        catch (Exception exception)
        {
            failures.Add($"指到下一程没有生效（cycle 从 {beforeSeek.Number("cycle")} 起没有变）：{exception.Message}");
        }

        // ── 快速与恢复原速：回读到的传输值对不对 ──────────────────────────────
        //
        // 这两条只验"按钮确实把新的传输交给了库、库也如实报了回来"；变速本身有没有生效，由上面那一条 0.25 倍速
        // 用位移钉住了。这里不试负速率 —— 时间轴只有正速率，负值在库里是被拒绝的，那属于单测的事。
        driver.Click("over.btn.rate.fast");
        driver.WaitFor(state => Math.Abs(state.Number("rate") - 4d) < 1e-6, ReactionWindow,
            $"{platform}: 点了快速，但 rate 没有回读成 4");

        driver.Click("over.btn.rate.normal");
        driver.WaitFor(state => Math.Abs(state.Number("rate") - 1d) < 1e-6, ReactionWindow,
            $"{platform}: 点了正常速，但 rate 没有回读成 1");

        // 收尾：把这一排留下的状态清干净，免得下一条用例从一个变速或暂停的半路接手。
        driver.Settle();

        Assert.AreEqual(0, failures.Count,
            $"{platform} 的时间轴控制不符：{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", failures)}");
    }
}
