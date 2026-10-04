using System.Collections.Concurrent;
using System.Threading;
using VeloxDev.TimeLine;

namespace Demo;

// 两条泵与窗口必须一致的常量。
internal static class DemoChannel
{
    // 窗口注册的通道，只有一个，只启动一次。
    // 旧版演示驱动三个注册在默认通道上的组件，却去 SetTargetFPS(30, "game") —— 一个从没启动过的第二通道，
    // 那次调用没有读者，配的帧率从未存在。这里通道在此命名、在此启动、在屏幕上读回，下面那些数才有意义。
    public const string Name = TickManager.DEFAULT_CHANNEL;

    // 球积分所受的重力，m/s²。取值让一次下落约 1.6 秒。
    public const double Gravity = 1.2;

    // 球落到的那条线，单位米。
    public const double FallHeight = 1.5;

    // 舞台缩放，固定值，屏幕上的一段距离才能换算回长度。
    public const double PixelsPerMetre = 200.0;

    // 球下方那根镜像条把两球距离放大的倍数。
    public const double Mirror = 10.0;

    // 画布高度（像素）：整段下落再加上起点线上方一点余量。
    public const double StageHeight = FallHeight * PixelsPerMetre + 20;

    // 地面线在舞台像素中的位置。
    public const double GroundY = FallHeight * PixelsPerMetre;
}

// 一只球，在 DemoChannel.Gravity 下积分。
// 两条泵跑的是同一个类型、同一个 Step()，差别全在交给它的 dt —— 没有第二套积分规则可供差异藏身，
// 所以两球不一致只可能是循环不同。
// 全程不加锁：恰好一条泵线程独占每个实例，窗口从不碰它；跨线程的只有整份发布的 BallReport。
internal sealed class BallBody(double gravity)
{
    // 离地高度（米）。
    public double Height;

    // 速度（米/秒，向下为正）。
    public double Velocity;

    // 当前这次下落已经过的秒数。随下落重置，因为闭式解是拿它比的。
    public double FallTime;

    // 通道启动以来喂入的秒数，落地不重置。
    // 这是每条泵对自己拿到多少时间的记录，也是未交付时间读数的基准：总线位置减去它，就是那条泵还没驱动的部分。
    // 落地绝不能清它——两球落地时刻不同，随每次下落重置的时钟只在其中两个之间可比。
    public double DriveTime;

    private double _dtSum;
    private double _dtSquares;

    // 球被放回线上的次数。
    public int Falls { get; private set; }

    // 这只球真正被驱动的 dt：Σdt² / Σdt。
    // 不是算术平均，差别正是要点而非精度：从静止展开半隐式欧拉得 Height - (g/2)t² = -(g/2)·Σdt²，
    // 对任意 dt 序列都精确（见 Drift），所以这是能反推积分的 dt。普通平均只在步长均匀时吻合，
    // 而 update 泵恰恰不均匀。
    public double EffectiveDt => _dtSum > 0 ? _dtSquares / _dtSum : 0;

    // 低于闭式解 (g/2)t² 的米数。恒为负，且恰为 -(g/2)Σdt²。
    public double Drift => -0.5 * gravity * _dtSquares;

    // 让球前进一个区间，无论泵是怎么量出来的。
    public void Step(double seconds)
    {
        Velocity += gravity * seconds;
        Height += Velocity * seconds;
        FallTime += seconds;
        DriveTime += seconds;
        _dtSum += seconds;
        _dtSquares += seconds * seconds;
    }

    // 球到达下落高度、该调用 RestartFall 时为真。
    public bool HasLanded => Height >= DemoChannel.FallHeight;

    // 把球放回线上、静止，并清空本次下落的累计。
    // 越过线的那一步整段丢弃，而不是裁剪到穿越瞬间：裁剪需要那个瞬间，而本类的读者只需要一次从静止、
    // 已知高度的下落。代价诚实可见——dt 越粗过冲越多，update 球在台外待的时间比 fixed 球略长。
    public void RestartFall()
    {
        Height = 0;
        Velocity = 0;
        FallTime = 0;
        _dtSum = 0;
        _dtSquares = 0;
        Falls++;
    }
}

// 一条日志来自哪个钩子。
internal enum HookKind
{
    Awake,
    Start,
    Update,
    LateUpdate,
    FixedUpdate,
}

// 关于一次钩子调用、即便不记录每一帧也值得记一行的事。
internal enum HookNote
{
    None,

    // 这次调用故意睡了。Update 欠一帧，FixedUpdate 欠几步。
    Slept,

    // 这次调用承担上一次调用睡觉的后果。
    AfterSleep,
}

// 一次被记录的钩子调用。
// 用 struct，且构建时不碰字符串：默认帧率下每秒约 120 次钩子，按次分配的日志会把 GC 开销加在它正在度量的循环上。
internal readonly record struct HookEntry(
    HookKind Kind,
    int Index,
    double DtMilliseconds,
    int Batch,
    bool Handled,
    HookNote Note,
    long WallMs);

// 一只球与驱动它的泵的不可变快照，由拥有它的钩子发布。
// 一次引用、每次钩子调用写一次，读者要么读到整份要么读不到。若就地写字段，窗口可能显示上一帧的位置配下一帧的 dt ——
// 跨线程 bug 就是这样变成「球有时跳一下」且永不复现的。
// 这也是报告用类而不用结构体的原因：那个大小的结构体不是原子发布的，Volatile.Write 也没有重载。
internal sealed record BallReport(
    double Height,
    double FallTime,
    double DriveTime,
    double EffectiveDt,
    double Drift,
    double DtMilliseconds,
    double UnspentMs,
    int Index,
    int Batch,
    int Falls,
    string Pump,
    HookNote Note,
    long WallMs)
{
    // 泵还没跑过一次时读者看到的值。
    public static readonly BallReport Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "—", HookNote.None, 0);
}

// 两条泵线程写、窗口读的全部状态。
// 规则（这些都不由类型强制）：计数器各由一个写者用 Interlocked 递增、用 Volatile 读，且从不重置——
// 它们说的是通道启动以来发生了什么；球各由一个泵线程独占，从不离开自己的钩子；
// 标志方向相反，窗口写、泵读，各一个写者，volatile int 就够；日志是 ConcurrentQueue<T>，
// 引擎自己在这两个线程之间交接用的也是它。
internal sealed class DemoState
{
    // 由窗口的 Update 钩子驱动——可变 dt，每帧一次。
    public readonly BallBody UpdateBall = new(DemoChannel.Gravity);

    // 由窗口的 FixedUpdate 钩子驱动——固定 dt，每个欠下的步一次。
    public readonly BallBody FixedBall = new(DemoChannel.Gravity);

    private BallReport _updateReport = BallReport.Empty;
    private BallReport _fixedReport = BallReport.Empty;

    // update 球及其泵，截至该泵最后一次调用。
    public BallReport UpdateReport => Volatile.Read(ref _updateReport);

    // fixed 球及其泵，截至该泵最后一次调用。
    public BallReport FixedReport => Volatile.Read(ref _fixedReport);

    public void PublishUpdate(BallReport report) => Volatile.Write(ref _updateReport, report);

    public void PublishFixed(BallReport report) => Volatile.Write(ref _fixedReport, report);

    public int AwakeCount;
    public int StartCount;
    public int UpdateCount;
    public int LateUpdateCount;
    public int FixedUpdateCount;

    // 本帧的 Update 没先跑、LateUpdate 却跑了的帧。必须恒为 0。
    public int OrderViolations;

    // 因 Update 置了 e.Handled 而被跳过的 LateUpdate 帧。
    public int SkippedLateUpdates;

    // Awake、随后 Start 运行时 UpdateCount 的值。
    // 0 就是「生命周期先于第一帧体」的全部证据：它们由循环的主线程排空驱动，而排空发生在第一帧的 Update 之前。
    public int AwakeAtUpdateCount = -1;

    public int StartAtUpdateCount = -1;

    // Awake 运行时已完成的帧数，直接来自引擎而非我们自己的计数器。
    public long AwakeFrameOrdinal = -1;

    // 最后一次 LateUpdate 的挂钟。旧值意味着钩子不再被调用。
    public long LateUpdateWallMs;

    // 最后一次 LateUpdate 把跟随环放在的米数。冻结意味着 LateUpdate 没在跑。
    public double LateUpdateHeight;

    // 窗口 → Update。非零要求下一次 Update 置 e.Handled。
    public int HandledRequested;

    // 窗口 → Update：下一次 Update 内要睡的毫秒数，只一次。
    public int UpdateHitchMs;

    // 窗口 → FixedUpdate：下一次 FixedUpdate 内要睡的毫秒数，只一次。
    public int FixedHitchMs;

    // 窗口 → 两条泵：加一要求各自把自己的球放回线上。
    public int RestartGeneration;

    // 窗口 → 两条泵：非零则记录每一次钩子调用，而不是只记值得注意的。
    public int RecordEveryHook;

    // 未写入日志的钩子调用数。显示出来，过滤过的日志就不会被误当成安静的循环。
    public int OmittedCount;

    public readonly ConcurrentQueue<HookEntry> Log = new();

    public void Record(HookKind kind, int index, double dtMilliseconds, int batch, bool handled, HookNote note)
        => Log.Enqueue(new HookEntry(kind, index, dtMilliseconds, batch, handled, note, Environment.TickCount64));
}
