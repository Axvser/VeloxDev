using BenchmarkDotNet.Attributes;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks;

// S2：每次布局 pass、每个节点都会走的「指纹捕获 + 请求」路径。
// 大部分时候锚点没有变化，测量会被指纹挡掉；这里把「捕获」（CountSlots + Stamp.Capture）
// 与「请求后被挡掉」分开量，回答「每节点每趟是否 < 1 µs」。
[MemoryDiagnoser]
public class SlotFingerprintBenchmarks
{
    [Params(2, 4, 6)]
    public int SlotCount { get; set; }

    private SlotLayoutPipelineCore pipeline = null!;
    private NodeDefaultViewModel node = null!;
    private FixedLocator locator = null!;
    private SlotLayoutConfiguration configuration = null!;

    [GlobalSetup]
    public void Setup()
    {
        node = new NodeDefaultViewModel
        {
            Anchor = new Anchor(40d, 40d, 0),
            Size = new VeloxDev.WorkflowSystem.Size(160d, 90d),
        };
        locator = new FixedLocator(SlotCount);
        configuration = new SlotLayoutConfiguration(locator.NamedCsv, "PART_Outputs", null, null);
        pipeline = new SlotLayoutPipelineCore(node, new object(), locator, new FixedHost(), configuration);

        // 先走一趟，让批次记下指纹；后面的 Unchanged 路径才会被挡掉。
        pipeline.RequestMeasure(SlotLayoutTrigger.Layout);
    }

    // 指纹不变时的完整请求：Capture → Request（命中指纹）→ 提前返回，无测量、无 Flush 工作。
    [Benchmark(Baseline = true)]
    public void RequestUnchanged() => pipeline.RequestMeasure(SlotLayoutTrigger.Layout);

    // 只量捕获本身：CountSlots + ReadNodeSize + Stamp.Capture，不含批次与队列。
    [Benchmark]
    public int CaptureOnly()
    {
        var (width, height) = (160d, 90d);
        var expected = locator.CountSlots(configuration.SlotNames, configuration.EnumeratorNames);
        var stamp = WorkflowLayoutStamp.Capture(node, width, height, expected);
        return stamp.GetHashCode();
    }

    // 对照：锚点变了，指纹不同，走完整一趟（含测量与写回）。
    [Benchmark]
    public void RequestAfterStampChange()
    {
        var anchor = node.Anchor;
        node.Anchor = new Anchor(anchor.Horizontal + 1d, anchor.Vertical, anchor.Layer);
        pipeline.RequestMeasure(SlotLayoutTrigger.Layout);
    }

    // 有代表性的 locator：具名槽查表、枚举槽逐项确认容器已在（与适配器的 CountSlots 同形，但不碰平台树）。
    private sealed class FixedLocator : ISlotPartLocator
    {
        private readonly Dictionary<string, ISlotPart> named = [];
        private readonly List<ISlotPart> enumerated = [];
        private readonly bool[] realized;

        public FixedLocator(int slotCount)
        {
            var namedCount = Math.Max(1, slotCount / 2);
            for (var i = 0; i < namedCount; i++)
            {
                named[$"PART_Slot{i}"] = new FixedPart(i);
            }

            for (var i = namedCount; i < slotCount; i++)
            {
                enumerated.Add(new FixedPart(i));
            }

            realized = new bool[enumerated.Count];
            Array.Fill(realized, true);
            NamedCsv = string.Join(",", named.Keys);
        }

        public string NamedCsv { get; }

        public SlotCoordinateSpace CoordinateSpace => SlotCoordinateSpace.NodeLocal;

        public ISlotPart? FindNamedSlot(string name) => named.GetValueOrDefault(name);

        public IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name) => enumerated;

        public object? ResolveCoordinateHost(string? hostName, Type? hostType) => null;

        public int CountSlots(IReadOnlyList<string> slotNames, IReadOnlyList<string> enumeratorNames)
        {
            // 与适配器同形的计数工作：具名逐个查表，枚举项逐个确认容器已实例化。
            var count = 0;
            foreach (var name in slotNames)
            {
                if (named.ContainsKey(name))
                {
                    count++;
                }
            }

            foreach (var _ in enumeratorNames)
            {
                for (var i = 0; i < realized.Length; i++)
                {
                    if (realized[i])
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }

    private sealed class FixedPart(int index) : ISlotPart
    {
        public ISlotComponent? Slot { get; } = new SlotDefaultViewModel();

        public bool IsMeasured => true;

        public SlotCenter? MeasureCenter(object? relativeTo) => new(index * 4d, index * 3d);
    }

    private sealed class FixedHost : ISlotMeasurementHost
    {
        public (double Width, double Height) ReadNodeSize() => (160d, 90d);

        public Anchor ReadAnchor(ISlotComponent slot) => slot.Anchor;

        public void WriteAnchor(ISlotComponent slot, Anchor anchor) => slot.Anchor = anchor;

        public void InvalidateMeasure()
        {
        }

        public bool Post(Action flush)
        {
            flush();
            return true;
        }
    }
}
