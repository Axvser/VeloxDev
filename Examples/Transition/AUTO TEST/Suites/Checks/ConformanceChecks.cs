using System.Globalization;
using VeloxDev.AT.Conformance;
using VeloxDev.AT.Drivers;
using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Drives every sampler an adapter ships inside a real running app, and checks two different things about it.
/// </summary>
/// <remarks>
/// This is the layer the pure-data suite cannot reach: a sampler whose produced value is a framework object needs a
/// live runtime to construct its endpoints at all, so the only place it can be driven is a real application that
/// already has one. The app does the driving and the expectation is written here, so nothing in this project can be
/// answered by the sampler under test.
/// <para>
/// One click checks both halves, because they are the same click:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The arithmetic.</b> One frame per sampler per eased time, published on <c>over.conf</c>, compared against a
/// closed form written here from the rule.
/// </item>
/// <item>
/// <b>The junction.</b> A real <c>Transition</c> then runs that same sampler on the same on-screen control to its
/// endpoint, and what the control holds during and after the run is published on <c>over.live</c> and scanned for
/// anomalies. Nothing in the first half can see this: it drives one frame at a time and never starts an animation.
/// It matters because the sampling loop swallows whatever a sampler throws, so a sampler producing a value the
/// framework rejects freezes the element mid-animation with no signal at all — the only evidence is what the element
/// is left holding.
/// </item>
/// </list>
/// </remarks>
internal static class ConformanceChecks
{
    /// <summary>
    /// How far a produced component may be from the expected one. Both sides run the same arithmetic, but as two
    /// separately compiled copies of it, so the last bit may differ; this is far below any real rule difference.
    /// </summary>
    private const double Tolerance = 1e-6;

    /// <summary>
    /// Blazor only: the sampler's product checked through the browser's own computed style.
    /// </summary>
    /// <remarks>
    /// 这条是 Blazor 独有的形态，也是"从真实 UI 表现验证"最字面的一种：浏览器里没有一个"控件属性"可读，
    /// 真实表现就是**计算样式**。所以这里不读 app 报的载荷，而是读浏览器算出来的背景色，与采样器端点色比。
    /// </remarks>
    internal static void RunBlazorBench(IDemoDriver driver)
    {
        driver.Settle();

        driver.ActivateSampler("StringSampler");

        // 等演出跑完（800ms），浏览器把最后一帧画上去 —— 那一帧对应 t=1，也就是端点色。
        Thread.Sleep(1200);

        var painted = driver.ComputedStyle("over.bench", "background-color");
        Assert.IsNotNull(painted, "浏览器应当能给出 over.bench 的计算样式");

        // 期望值取自 StringSampler 声明的端点色 #F0B43240（R240 G180 B50），不是取自载荷。
        var declared = RgbColor.Parse("#F0B432");
        var actual = CssColor.Parse(painted);

        Assert.AreEqual(declared, actual,
            $"浏览器算出的背景色是 {painted}，而采样器的端点色是 #F0B432");
    }

    /// <summary>
    /// Capture the surface as it is right now and return a phrase to append to a failure message.
    /// </summary>
    /// <remarks>
    /// A reachability failure needs a photograph of the moment. What UI Automation reports says the handle is out of
    /// reach; what it cannot say is what the screen looked like — a window that never came forward, a list scrolled
    /// somewhere unexpected, and a control that was never laid out all report the same way, and they are three
    /// different faults.
    /// </remarks>
    private static string ScreenshotNote(IDemoDriver driver, string what)
    {
        try
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"veloxdev-at-{driver.Platform}-{what}-{DateTime.Now:HHmmss}.png");

            var written = driver.CaptureScreenshot(path);
            return written is null ? string.Empty : $" [screenshot: {written}]";
        }
        catch (Exception exception)
        {
            return $" [screenshot failed: {exception.Message}]";
        }
    }

    /// <summary>
    /// Checks every sampler of one platform against its closed form, through a demo the caller already owns.
    /// </summary>
    internal static void Run(IDemoDriver driver, string platform)
    {
        driver.Settle();

        var table = ConformanceCatalog.For(platform);

        // 收集全部不符再一次性报出：一个采样器错在哪一步，比"第一个失败就停"有用得多。
        var failures = new List<string>();
        var closedFormFrames = 0;
        var liveRuns = 0;

        // 1. 每一行够不够得着 —— 只滚、只断言，**不点**。批量那一路不需要点行，但"行没人够得着"仍然要挡：
        //    UIA 的 Invoke 对一个滚出视野的控件照样生效，少了这一道，一排人够不着的行会绿着过去。
        foreach (var entry in table)
        {
            var token = $"over.sampler.{entry.Sampler}";
            try
            {
                driver.BringIntoView(token);

                if (!driver.HasControl(token))
                    failures.Add($"{entry.Sampler}：界面上没有 {token} 这个把手 —— 列表少了一条案例。");
                else if (!driver.IsControlInView(token))
                    failures.Add($"{entry.Sampler}：{token} 滚进视野之后仍然在窗口外，人够不着。");
            }
            catch (Exception exception)
            {
                failures.Add($"{entry.Sampler}：可达性检查失败：{exception.Message}");
            }
        }

        // 2. 顶栏那三个按钮（重置 / 全部启动 / 停止全部）—— 也是"还在动"唯一看得见的那个窗口。
        ScanToolbar(driver, table, failures);

        // 3. 批量：一次"全部启动"把所有行一起跑，一次读取收全部结果。
        ScanBatch(driver, table, failures, ref closedFormFrames, ref liveRuns);

        // 4. 抽样一行，走一遍完整的"点它的把手 → 它写自己的载荷 → 读回"。
        SpotCheck(driver, table, failures, ref closedFormFrames, ref liveRuns);

        // 无论成败都留一行：套件安静通过时，这一行是"真动画确实跑了这么多条"的唯一证据。
        Console.WriteLine(
            $"{platform}：{table.Count} 个采样器；闭式解 {closedFormFrames} 帧，真动画 {liveRuns} 条；异常 {failures.Count} 条。");

        CollectionAssert.AreEqual(Array.Empty<string>(), failures.ToArray(),
            $"{platform} 有 {failures.Count} 处异常：{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    /// <summary>
    /// 批量那一路：一次"全部启动"把每一行都跑起来，一次读取把每一行的闭式解与观察结果都收回来。
    /// </summary>
    /// <remarks>
    /// 逐行点的话，十几行就是十几条真动画、十几段等待 —— 那是这个套件最慢的部分。批量把它们压成一次点击，
    /// 而每一行验的东西一字未变：同样的五个缓动时间、同一份闭式解、同一套观察规则。
    /// </remarks>
    private static void ScanBatch(
        IDemoDriver driver,
        IReadOnlyList<ConformanceEntry> table,
        List<string> anomalies,
        ref int closedFormFrames,
        ref int liveRuns)
    {
        long before;
        try
        {
            before = driver.ReadBatch().Sequence;
        }
        catch (Exception exception)
        {
            anomalies.Add(
                $"这个 demo 的 over.batch 读不出来（{exception.Message}）—— 批量那一路没接上，"
                + "十几行就只剩下面那一条抽样的覆盖。");
            return;
        }

        driver.Click("over.btn.start.all");

        BatchPayload? batch;
        try
        {
            batch = driver.WaitForBatch(before);
        }
        catch (Exception exception)
        {
            anomalies.Add($"读 over.batch 失败：{exception.Message}");
            return;
        }

        if (batch is null)
        {
            anomalies.Add(
                $"批量：全部启动之后在 {DemoDriverBase.BatchTimeout.TotalSeconds:F0}s 内没有报 done=1。");
            return;
        }

        if (batch.Version != 1)
            anomalies.Add($"批量：载荷版本是 {batch.Version}，这个套件只认 1。");

        if (batch.Rows != table.Count)
            anomalies.Add($"批量：demo 声明跑了 {batch.Rows} 行，一致性表里有 {table.Count} 条。");

        var reported = batch.Frames.Frames.Select(frame => frame.Sampler).Distinct().ToList();
        var missing = table.Select(entry => entry.Sampler).Where(name => !reported.Contains(name)).ToList();
        if (missing.Count > 0)
            anomalies.Add($"批量：这些行没有报回闭式解帧（列表少了几条案例）：{string.Join(", ", missing)}");

        foreach (var entry in table)
        {
            var frames = batch.Frames.Frames.Where(frame => frame.Sampler == entry.Sampler).ToList();

            // 整条没报回来已经在上面点过名了，不必再逐帧说五遍"这一帧也没有"。
            if (frames.Count > 0) CheckFrames(entry, frames, anomalies, ref closedFormFrames);

            var live = batch.Lives.FirstOrDefault(row => row.Sampler == entry.Sampler);
            if (live is null)
            {
                anomalies.Add($"{entry.Sampler}：批量载荷里没有这一行的观察结果。");
                continue;
            }

            if (!live.Done)
            {
                anomalies.Add($"{entry.Sampler}：整份批量已经报 done，这一行却没报完。");
                continue;
            }

            liveRuns++;
            ScanLive(entry, live, anomalies);
        }
    }

    /// <summary>
    /// 抽样一行，走一遍完整的"点它的把手 → 它写自己的载荷 → 读回"。
    /// </summary>
    /// <remarks>
    /// 批量那一路不点行，所以这条交互路径如果没有抽样就没人走了 —— 而"点某一行"本身正是这个界面要保证的事。
    /// 只抽一行是刻意的：每平台一次点击足以证明它还通，又不会把批量省下来的时间花回去。
    /// </remarks>
    private static void SpotCheck(
        IDemoDriver driver,
        IReadOnlyList<ConformanceEntry> table,
        List<string> anomalies,
        ref int closedFormFrames,
        ref int liveRuns)
    {
        var entry = table[0];

        long liveBefore;
        try
        {
            liveBefore = driver.ReadLive().Sequence;
        }
        catch (Exception exception)
        {
            anomalies.Add($"抽样那一行的 over.live 读不出来：{exception.Message}");
            return;
        }

        ConformancePayload payload;
        try
        {
            payload = driver.ActivateSampler(entry.Sampler);
        }
        catch (Exception exception)
        {
            anomalies.Add($"抽样点 {entry.Sampler} 失败：{exception.Message}{ScreenshotNote(driver, entry.Sampler)}");
            return;
        }

        if (payload.Version != 1)
            anomalies.Add($"{entry.Sampler}：逐行载荷版本是 {payload.Version}，这个套件只认 1。");

        // 点的是谁，报回来的就该是谁 —— 载荷里混进另一条采样器的帧，说明把手与探针表不同步。
        var reported = payload.Frames.Select(frame => frame.Sampler).Distinct().ToList();
        if (reported.Count != 1 || reported[0] != entry.Sampler)
        {
            anomalies.Add($"{entry.Sampler}：点它却报回了 [{string.Join(", ", reported)}]。");
        }
        else
        {
            CheckFrames(entry, payload.Frames, anomalies, ref closedFormFrames);
        }

        LivePayload? live;
        try
        {
            live = driver.WaitForLive(liveBefore);
        }
        catch (Exception exception)
        {
            anomalies.Add($"抽样那一行读 over.live 失败：{exception.Message}");
            return;
        }

        if (live is null)
        {
            anomalies.Add(
                $"{entry.Sampler}：逐行那一半在 {DemoDriverBase.LiveTimeout.TotalSeconds:F0}s 内没有报 done=1。");
            return;
        }

        liveRuns++;
        ScanLive(entry, live, anomalies);
    }

    /// <summary>一条采样器的五个固定缓动时间，逐帧对着闭式解比。</summary>
    private static void CheckFrames(
        ConformanceEntry entry,
        IReadOnlyList<ConformancePayload.Frame> frames,
        List<string> anomalies,
        ref int closedFormFrames)
    {
        for (var index = 0; index < ConformanceCatalog.Times.Length; index++)
        {
            var t = ConformanceCatalog.Times[index];
            var frame = frames.FirstOrDefault(candidate => candidate.TimeIndex == index);

            if (frame is null)
            {
                anomalies.Add($"{entry.Sampler} 在 t={Format(t)} 上没有帧。");
                continue;
            }

            if (frame.TypeTag != entry.TypeTag)
            {
                anomalies.Add($"{entry.Sampler} t={Format(t)}：产物类型是 {frame.TypeTag}，期望 {entry.TypeTag}。");
                continue;
            }

            var expected = entry.Expected(t);
            closedFormFrames++;
            if (!Matches(expected, frame.Components))
            {
                anomalies.Add(
                    $"{entry.Sampler} t={Format(t)}：期望 [{Format(expected)}]，实际 [{Format(frame.Components)}]。");
            }
        }
    }

    /// <summary>
    /// The token the live payload is published under. Fixed in the driver base for every platform, and repeated here
    /// only so this suite can ask whether a demo has it at all before clicking anything.
    /// </summary>
    private const string LiveAutomationId = "over.live";

    /// <summary>How long to wait for the toolbar's effect to show up in the readout.</summary>
    private static readonly TimeSpan ToolbarWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 顶栏那三个按钮：全部启动 / 停止全部 / 重置，一次动到列表里的每一行。
    /// </summary>
    /// <remarks>
    /// 可观测量只有 <c>over.state</c> 里的 <c>rows</c>/<c>away</c>/<c>moving</c>，断言全是无模型的 ——
    /// 不解释某一行该到哪，只说明有几行离开了起点、有几行这一拍还在变。
    /// <para>
    /// <b>把节拍临时关掉是有意的。</b> "还在动"这件事情只在一条动画没跑完时存在，而点击之后那口气
    /// （<c>VELOXDEV_AT_PACE</c>）的默认值比一条动画还长 —— 不关掉的话，等套件开始读，动画早就跑完了，
    /// "停止之后不再有行在动"就成了一句恒真的废话。这里只在本地关掉、读完恢复，不动驱动的公共形状。
    /// </para>
    /// </remarks>
    private static void ScanToolbar(IDemoDriver driver, IReadOnlyList<ConformanceEntry> table, List<string> anomalies)
    {
        // 表里声明会从 t=0 变到 t=1 的那些行 —— 离散的几条整个网格都停在起点，它们永远不会"离开起点"。
        var movable = table.Count(entry => !Same(entry.Expected(0d), entry.Expected(1d)));

        var pace = Environment.GetEnvironmentVariable("VELOXDEV_AT_PACE");
        Environment.SetEnvironmentVariable("VELOXDEV_AT_PACE", "0");

        try
        {
            driver.Click("over.btn.reset.all");
            var rested = driver.WaitFor(
                payload => payload.Number("away") == 0 && payload.Number("moving") == 0,
                ToolbarWindow,
                "点重置之后仍有行偏离声明的起点");

            if (rested.Number("rows") != table.Count)
            {
                anomalies.Add(
                    $"顶栏：载荷报 {rested.Number("rows")} 行，一致性表里有 {table.Count} 条 —— 列表少了案例。");
            }

            driver.Click("over.btn.start.all");

            // **等**两件事一起成立，而不是在某一拍上断言。
            //
            // "有行在动"与"所有该动的行都离开了起点"不是同一刻成立的：一条动画的头一帧里，某个产物的分量可能
            // 与它声明的起点**逐位相同**（按字节截断的颜色尤其如此），于是那一拍上"已经离开起点的行数"会短暂
            // 少一个。那是采样时刻的巧合，不是没动 —— 在那一拍断言等于用一次竞态去判一行有没有跑起来。
            var running = driver.WaitFor(
                payload => payload.Number("moving") > 0 && payload.Number("away") == movable,
                ToolbarWindow,
                $"全部启动之后应当有 {movable} 行离开起点、且至少一行在动");

            driver.Click("over.btn.stop.all");
            var frozen = driver.WaitFor(
                payload => payload.Number("moving") == 0,
                ToolbarWindow,
                "停止全部之后仍有行在变");

            // 停在半路而不是被放回起点：Exit 是原地冻结，不是重置。这一条把两者区分开。
            if (frozen.Number("away") == 0)
            {
                anomalies.Add("顶栏：停止全部把每一行都放回了声明的起点 —— 原地冻结与重置被做成了同一件事。");
            }
        }
        catch (Exception exception)
        {
            anomalies.Add($"顶栏三个按钮：{exception.Message}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VELOXDEV_AT_PACE", pace);
        }
    }

    /// <summary>
    /// Scans one live run for the things a sampler's arithmetic cannot show: what the control was actually left
    /// holding, over the whole run.
    /// </summary>
    /// <remarks>
    /// None of these restate a sampler's rule. They are the properties a transition has whatever curve it draws:
    /// it writes finite numbers, it respects the ranges its own type contracts define, it moves, and it finishes on
    /// the endpoint. The last one is the load-bearing check — the sampling loop swallows anything a sampler throws,
    /// so an animation that died halfway leaves no other trace than a control frozen short of its target.
    /// </remarks>
    private static void ScanLive(ConformanceEntry entry, LivePayload live, List<string> anomalies)
    {
        if (live.Sampler != entry.Sampler)
            anomalies.Add($"{entry.Sampler}：真动画报的是 {live.Sampler}。");

        if (live.Error is not null)
            anomalies.Add($"{entry.Sampler}：真动画没起来 —— {live.Error}");

        if (live.TypeTag != entry.TypeTag)
            anomalies.Add($"{entry.Sampler}：真动画跑完后控件持有的是 {live.TypeTag}，期望 {entry.TypeTag}。");

        if (live.DeclaredComponents != live.Last.Count)
            anomalies.Add(
                $"{entry.Sampler}：真动画声明写了 {live.DeclaredComponents} 个分量，实际报回 {live.Last.Count} 个。");

        // NaN / ±Inf 是后端数据异常里最没有歧义的一种：它不可能是有意的过冲。
        if (live.Bad > 0)
            anomalies.Add($"{entry.Sampler}：真动画期间有 {live.Bad} 次采样拿到非有限值（NaN / ±Inf）。");

        if (live.Samples < 2)
            anomalies.Add($"{entry.Sampler}：真动画期间只采到 {live.Samples} 拍 —— 值根本没落到控件上。");

        // 一条只跑一趟、不自动反向的动画，末帧精确落在终点；控件没停在那里，说明它没跑完。
        var endpoint = entry.Expected(1d);
        if (!Matches(endpoint, live.Last))
            anomalies.Add(
                $"{entry.Sampler}：真动画跑完后控件持有 [{Format(live.Last)}]，而 t=1 应当是 [{Format(endpoint)}]。");

        for (var index = 0; index < live.Last.Count; index++)
        {
            if (LiveContract.Bounds(live.TypeTag, index) is not { } bounds) continue;

            CheckBounded(entry, "终值", index, live.Last, bounds, anomalies);
            CheckBounded(entry, "最小值", index, live.Min, bounds, anomalies);
            CheckBounded(entry, "最大值", index, live.Max, bounds, anomalies);
        }

        // 表说它从 t=0 到 t=1 会变（离散的那几条不会），它就真的得动过 —— 采样器成了空转也是异常。
        if (LiveContract.ComponentsAreQuantities(live.TypeTag)
            && !Same(endpoint, entry.Expected(0d))
            && !Moved(live))
        {
            anomalies.Add(
                $"{entry.Sampler}：真动画期间值一动没动（最小值 [{Format(live.Min)}]，最大值 [{Format(live.Max)}]）。");
        }
    }

    private static void CheckBounded(
        ConformanceEntry entry,
        string label,
        int index,
        IReadOnlyList<double> values,
        (double Min, double Max) bounds,
        List<string> anomalies)
    {
        if (index >= values.Count) return;

        var value = values[index];

        // 非有限值已经由 bad 单独报过了，别在这里报第二遍。
        if (!double.IsFinite(value)) return;
        if (value >= bounds.Min && value <= bounds.Max) return;

        anomalies.Add(
            $"{entry.Sampler}：真动画的{label}第 {index} 个分量是 {Format(value)}，"
            + $"超出 {entry.TypeTag} 允许的 [{Format(bounds.Min)}, {Format(bounds.Max)}]。");
    }

    /// <summary>Whether any component was seen to hold two different values during the run.</summary>
    private static bool Moved(LivePayload live)
    {
        for (var index = 0; index < live.Min.Count && index < live.Max.Count; index++)
        {
            if (live.Min[index] != live.Max[index]) return true;
        }

        return false;
    }

    /// <summary>Exact component-wise equality. Used to compare tables with each other, never a measurement.</summary>
    private static bool Same(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count != right.Count) return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index]) return false;
        }

        return true;
    }

    /// <summary>
    /// Compares component vectors. A length difference is a failure, never a comparison of the common prefix: a
    /// sampler that started producing a different shape has to be reported, not partially matched.
    /// </summary>
    /// <remarks>
    /// Non-finite is checked before the tolerance, and that order is load-bearing: <c>Math.Abs(NaN - x)</c> is NaN,
    /// and every comparison against NaN is false, so a bare <c>&gt; Tolerance</c> test would report a NaN as a match.
    /// A sampler that starts producing NaN would then sail through both halves of this suite.
    /// </remarks>
    private static bool Matches(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        if (expected.Count != actual.Count) return false;

        for (var i = 0; i < expected.Count; i++)
        {
            if (!double.IsFinite(actual[i])) return false;
            if (Math.Abs(expected[i] - actual[i]) > Tolerance) return false;
        }

        return true;
    }

    private static string Format(double value) => value.ToString("G6", CultureInfo.InvariantCulture);

    private static string Format(IReadOnlyList<double> values)
        => string.Join(", ", values.Select(Format));
}
