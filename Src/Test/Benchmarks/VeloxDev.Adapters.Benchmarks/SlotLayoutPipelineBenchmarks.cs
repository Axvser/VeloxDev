using BenchmarkDotNet.Attributes;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks;

// S1：Core 插槽流水线单趟开销。8 个具名插槽 + 16 个枚举项，固定 locator/host，
// 隔离平台测量，只量编排本身：遍历顺序、坐标换算与重复写抑制。
[MemoryDiagnoser]
public class SlotLayoutPipelineBenchmarks
{
    private const int NamedCount = 8;
    private const int EnumeratedCount = 16;

    private SlotDefaultViewModel[] _slots = null!;
    private SlotLayoutPipelineCore _pipeline = null!;

    [GlobalSetup]
    public void Setup()
    {
        var node = new NodeDefaultViewModel
        {
            Anchor = new Anchor(40d, 40d, 0),
            Size = new VeloxDev.WorkflowSystem.Size(160d, 90d),
        };

        _slots = new SlotDefaultViewModel[NamedCount + EnumeratedCount];
        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new SlotDefaultViewModel();
        }

        var locator = new FixedLocator();
        var named = string.Join(",", Enumerable.Range(0, NamedCount).Select(i => $"PART_Slot{i}"));
        for (var i = 0; i < NamedCount; i++)
        {
            locator.Named[$"PART_Slot{i}"] = new FixedPart(_slots[i], i);
        }

        for (var i = 0; i < EnumeratedCount; i++)
        {
            locator.Enumerated.Add(new FixedPart(_slots[NamedCount + i], i));
        }

        _pipeline = new SlotLayoutPipelineCore(
            node,
            new object(),
            locator,
            new FixedHost(),
            new SlotLayoutConfiguration(named, "PART_Outputs", null, null));

        // 先写一轮，让 StablePass 走「锚点没变」的抑制路径。
        _pipeline.Measure();
    }

    [Benchmark]
    public void StablePass() => _pipeline.Measure();

    [Benchmark]
    public void FirstPass()
    {
        foreach (var slot in _slots)
        {
            slot.Anchor = new Anchor(double.NaN, double.NaN, 0);
        }

        _pipeline.Measure();
    }

    private sealed class FixedPart(ISlotComponent slot, int index) : ISlotPart
    {
        public ISlotComponent? Slot { get; } = slot;

        public bool IsMeasured => true;

        public SlotCenter? MeasureCenter(object? relativeTo)
            => new(index * 4d, index * 3d);
    }

    private sealed class FixedLocator : ISlotPartLocator
    {
        public SlotCoordinateSpace CoordinateSpace => SlotCoordinateSpace.NodeLocal;

        public Dictionary<string, ISlotPart> Named { get; } = new(StringComparer.Ordinal);

        public List<ISlotPart> Enumerated { get; } = [];

        public ISlotPart? FindNamedSlot(string name) => Named.GetValueOrDefault(name);

        public IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name) => Enumerated;

        public object? ResolveCoordinateHost(string? hostName, Type? hostType) => null;

        public int CountSlots(IReadOnlyList<string> slotNames, IReadOnlyList<string> enumeratorNames)
            => Named.Count + Enumerated.Count;
    }

    private sealed class FixedHost : ISlotMeasurementHost
    {
        public (double Width, double Height) ReadNodeSize() => (160d, 90d);

        public Anchor ReadAnchor(ISlotComponent slot) => slot.Anchor;

        public void WriteAnchor(ISlotComponent slot, Anchor anchor) => slot.Anchor = anchor;

        public void InvalidateMeasure() { }

        public bool Post(Action flush) => true;
    }
}
