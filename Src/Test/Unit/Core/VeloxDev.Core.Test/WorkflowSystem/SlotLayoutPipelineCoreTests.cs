using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Contracts of the shared slot-measurement pipeline: declared measurement order, the three coordinate spaces and
/// their fallback, duplicate suppression, and the layout-versus-forced re-measure schedule.
/// </summary>
[TestClass]
public class SlotLayoutPipelineCoreTests
{
    [TestMethod]
    public void NamedSlots_AreMeasuredInDeclaredOrder_AndWrittenBack()
    {
        var node = Node(40d, 40d);
        var first = new SlotDefaultViewModel();
        var second = new SlotDefaultViewModel();
        var (pipeline, host) = Create(node, new SlotLayoutConfiguration("PART_B,PART_A", null, null, null), locator =>
        {
            locator.Named["PART_A"] = Part(first, nodeCenter: new SlotCenter(11d, 12d));
            locator.Named["PART_B"] = Part(second, nodeCenter: new SlotCenter(21d, 22d));
        });

        pipeline.Measure();

        CollectionAssert.AreEqual(new ISlotComponent[] { second, first }, host.WrittenSlots.ToArray());
        AssertAnchor(51d, 52d, first.Anchor);   // 40 + 11, 40 + 12
        AssertAnchor(61d, 62d, second.Anchor);  // 40 + 21, 40 + 22
    }

    [TestMethod]
    public void EnumeratorItems_AreMeasuredAfterNamedSlots_InItemOrder()
    {
        var node = Node(0d, 0d);
        var named = new SlotDefaultViewModel();
        var item0 = new SlotDefaultViewModel();
        var item1 = new SlotDefaultViewModel();
        var host = new FakeHost { RunPostedImmediately = true };
        var locator = new FakeLocator();
        locator.Named["PART_Input"] = Part(named, nodeCenter: new SlotCenter(1d, 1d));
        locator.Enumerated["PART_Outputs"] =
        [
            Part(item0, nodeCenter: new SlotCenter(2d, 2d)),
            Part(item1, nodeCenter: new SlotCenter(3d, 3d)),
        ];
        var pipeline = new SlotLayoutPipelineCore(
            node,
            new object(),
            locator,
            host,
            new SlotLayoutConfiguration("PART_Input", "PART_Outputs", null, null));

        pipeline.Measure();

        CollectionAssert.AreEqual(new ISlotComponent[] { named, item0, item1 }, host.WrittenSlots.ToArray());
        CollectionAssert.AreEqual(new[] { "PART_Input", "PART_Outputs" }, locator.Calls.ToArray());
    }

    [TestMethod]
    public void CanvasLocalHost_DoesNotSubtractActualOffset()
    {
        var tree = new TreeDefaultViewModel();
        tree.Layout.NegativeOffset = new Offset(10d, 20d);
        var node = Node(40d, 40d);
        tree.GetHelper().CreateNode(node);
        var slot = new SlotDefaultViewModel();
        var (pipeline, _) = Create(
            node,
            new SlotLayoutConfiguration("PART_A", null, null, null),
            locator =>
            {
                locator.CoordinateSpace = SlotCoordinateSpace.CanvasLocal;
                locator.CoordinateHost = new object();
                locator.Named["PART_A"] = Part(slot, hostCenter: new SlotCenter(56d, 56d));
            });

        pipeline.Measure();

        AssertAnchor(56d, 56d, slot.Anchor);
    }

    [TestMethod]
    public void ScrollSpaceHost_SubtractsActualOffset()
    {
        var tree = new TreeDefaultViewModel();
        tree.Layout.NegativeOffset = new Offset(10d, 20d);
        var node = Node(40d, 40d);
        tree.GetHelper().CreateNode(node);
        var slot = new SlotDefaultViewModel();
        var (pipeline, _) = Create(
            node,
            new SlotLayoutConfiguration("PART_A", null, null, null),
            locator =>
            {
                locator.CoordinateSpace = SlotCoordinateSpace.ScrollSpace;
                locator.CoordinateHost = new object();
                locator.Named["PART_A"] = Part(slot, hostCenter: new SlotCenter(56d, 56d));
            });

        pipeline.Measure();

        AssertAnchor(46d, 36d, slot.Anchor);
    }

    [TestMethod]
    public void ScrollSpaceHost_WithoutLayout_FallsBackToNodeLocal()
    {
        // 没有父树就拿不到 ActualOffset：必须退回节点局部空间，而不是把 0 当成偏移。
        var node = Node(40d, 40d);
        var slot = new SlotDefaultViewModel();
        var (pipeline, _) = Create(
            node,
            new SlotLayoutConfiguration("PART_A", null, null, null),
            locator =>
            {
                locator.CoordinateSpace = SlotCoordinateSpace.ScrollSpace;
                locator.CoordinateHost = new object();
                locator.Named["PART_A"] = Part(
                    slot, hostCenter: new SlotCenter(56d, 56d), nodeCenter: new SlotCenter(16d, 16d));
            });

        pipeline.Measure();

        AssertAnchor(56d, 56d, slot.Anchor); // 40 + 16
    }

    [TestMethod]
    public void MissingCoordinateHost_FallsBackToNodeLocal()
    {
        var node = Node(40d, 40d);
        var slot = new SlotDefaultViewModel();
        var (pipeline, _) = Create(
            node,
            new SlotLayoutConfiguration("PART_A", null, null, null),
            locator => locator.Named["PART_A"] = Part(
                slot, hostCenter: new SlotCenter(999d, 999d), nodeCenter: new SlotCenter(16d, 16d)));

        pipeline.Measure();

        AssertAnchor(56d, 56d, slot.Anchor);
    }

    [TestMethod]
    public void DuplicateSuppression_DoesNotRewriteAnUnchangedAnchor()
    {
        var node = Node(40d, 40d);
        var slot = new SlotDefaultViewModel();
        var (pipeline, host) = Create(
            node,
            new SlotLayoutConfiguration("PART_A", null, null, null),
            locator => locator.Named["PART_A"] = Part(slot, nodeCenter: new SlotCenter(16d, 16d)));

        pipeline.Measure();
        pipeline.Measure();

        Assert.HasCount(1, host.Writes, "the second pass measured the same centre and must not write again");
    }

    [TestMethod]
    public void UninitializedNaNAnchor_IsWritten()
    {
        var node = Node(0d, 0d);
        var slot = new SlotDefaultViewModel();
        Assert.IsTrue(double.IsNaN(slot.Anchor.Horizontal));

        var (pipeline, host) = Create(
            node,
            new SlotLayoutConfiguration("PART_A", null, null, null),
            locator => locator.Named["PART_A"] = Part(slot, nodeCenter: new SlotCenter(0d, 0d)));

        pipeline.Measure();

        Assert.HasCount(1, host.Writes, "NaN never compares equal, so it must be treated as a change");
    }

    [TestMethod]
    public void LayoutTrigger_SkipsWhenTheStampIsUnchanged()
    {
        var node = Node(40d, 40d);
        var slot = new SlotDefaultViewModel();
        var host = new FakeHost { RunPostedImmediately = true };
        var locator = new FakeLocator();
        locator.Named["PART_A"] = Part(slot, nodeCenter: new SlotCenter(16d, 16d));
        var pipeline = new SlotLayoutPipelineCore(
            node, new object(), locator, host, new SlotLayoutConfiguration("PART_A", null, null, null));

        pipeline.RequestMeasure(SlotLayoutTrigger.Layout);
        Assert.AreEqual(1, locator.FindNamedCalls);
        pipeline.RequestMeasure(SlotLayoutTrigger.Layout);

        Assert.AreEqual(1, locator.FindNamedCalls, "same geometry, size and slot count means nothing moved");
    }

    [TestMethod]
    public void ViewportTrigger_ForcesMeasure_EvenWhenTheStampIsUnchanged()
    {
        var node = Node(40d, 40d);
        var slot = new SlotDefaultViewModel();
        var host = new FakeHost { RunPostedImmediately = true };
        var locator = new FakeLocator
        {
            CoordinateSpace = SlotCoordinateSpace.NodeLocal,
        };
        locator.Named["PART_A"] = Part(slot, nodeCenter: new SlotCenter(16d, 16d));
        var pipeline = new SlotLayoutPipelineCore(
            node, new object(), locator, host, new SlotLayoutConfiguration("PART_A", null, null, null));

        pipeline.RequestMeasure(SlotLayoutTrigger.Viewport);
        pipeline.RequestMeasure(SlotLayoutTrigger.Viewport);

        Assert.AreEqual(2, locator.FindNamedCalls, "a transform-only change is invisible to the stamp and must still run");
    }

    [TestMethod]
    public void Stop_MakesQueuedWorkANoOp()
    {
        var node = Node(40d, 40d);
        var slot = new SlotDefaultViewModel();
        var host = new FakeHost { RunPostedImmediately = false };
        var locator = new FakeLocator();
        locator.Named["PART_A"] = Part(slot, nodeCenter: new SlotCenter(16d, 16d));
        var pipeline = new SlotLayoutPipelineCore(
            node, new object(), locator, host, new SlotLayoutConfiguration("PART_A", null, null, null));

        pipeline.RequestMeasure(SlotLayoutTrigger.Attachment);
        Assert.HasCount(1, host.Posted);

        pipeline.Stop();
        host.Posted[0]();

        Assert.AreEqual(0, host.Writes.Count);
        Assert.AreEqual(0, locator.FindNamedCalls);
    }

    [TestMethod]
    public void WatchedPropertyNames_IncludePartNamesAndTheirStrippedForm()
    {
        var node = Node(0d, 0d);
        var pipeline = new SlotLayoutPipelineCore(
            node,
            new object(),
            new FakeLocator(),
            new FakeHost(),
            new SlotLayoutConfiguration("PART_OutputSlots", "PART_Inputs", null, null));

        Assert.IsTrue(pipeline.WatchedPropertyNames.Contains("PART_OutputSlots"));
        Assert.IsTrue(pipeline.WatchedPropertyNames.Contains("OutputSlots"));
        Assert.IsTrue(pipeline.WatchedPropertyNames.Contains("PART_Inputs"));
        Assert.IsTrue(pipeline.WatchedPropertyNames.Contains("Inputs"));
        Assert.IsTrue(pipeline.WatchedPropertyNames.Contains(nameof(INodeComponent.Anchor)));
        Assert.IsTrue(pipeline.WatchedPropertyNames.Contains(nameof(INodeComponent.Size)));
        Assert.IsTrue(pipeline.WatchedPropertyNames.Contains("InputSlot"));
    }

    [TestMethod]
    public void Configuration_TrimsAndDeduplicates_PreservingDeclaredOrder()
    {
        var configuration = new SlotLayoutConfiguration(
            " PART_B , PART_A,PART_B ", "PART_Out", "PART_Canvas", typeof(object));

        CollectionAssert.AreEqual(new[] { "PART_B", "PART_A" }, configuration.SlotNames.ToArray());
        CollectionAssert.AreEqual(new[] { "PART_Out" }, configuration.EnumeratorNames.ToArray());
        Assert.AreEqual("PART_Canvas", configuration.CoordinateHostName);
        Assert.AreEqual(typeof(object), configuration.CoordinateHostType);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static (SlotLayoutPipelineCore Pipeline, FakeHost Host) Create(
        INodeComponent node,
        SlotLayoutConfiguration configuration,
        Action<FakeLocator> configure)
    {
        var host = new FakeHost { RunPostedImmediately = true };
        var locator = new FakeLocator();
        configure(locator);
        var pipeline = new SlotLayoutPipelineCore(node, new object(), locator, host, configuration);
        return (pipeline, host);
    }

    private static NodeDefaultViewModel Node(double x, double y)
        => new() { Anchor = new Anchor(x, y, 0), Size = new Size(160d, 90d) };

    private static FakePart Part(
        ISlotComponent slot, SlotCenter? nodeCenter = null, SlotCenter? hostCenter = null)
        => new() { Slot = slot, NodeCenter = nodeCenter, HostCenter = hostCenter };

    private static void AssertAnchor(double x, double y, Anchor anchor)
    {
        Assert.AreEqual(x, anchor.Horizontal, 0.001d);
        Assert.AreEqual(y, anchor.Vertical, 0.001d);
    }

    private sealed class FakePart : ISlotPart
    {
        public ISlotComponent? Slot { get; init; }
        public bool IsMeasured { get; init; } = true;
        public SlotCenter? NodeCenter { get; init; }
        public SlotCenter? HostCenter { get; init; }

        public SlotCenter? MeasureCenter(object? relativeTo)
            => relativeTo is null ? NodeCenter : HostCenter;
    }

    private sealed class FakeLocator : ISlotPartLocator
    {
        public SlotCoordinateSpace CoordinateSpace { get; set; } = SlotCoordinateSpace.NodeLocal;
        public object? CoordinateHost { get; set; }
        public Dictionary<string, ISlotPart> Named { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<ISlotPart>> Enumerated { get; } = new(StringComparer.Ordinal);
        public List<string> Calls { get; } = [];
        public int FindNamedCalls { get; private set; }

        public ISlotPart? FindNamedSlot(string name)
        {
            FindNamedCalls++;
            Calls.Add(name);
            return Named.GetValueOrDefault(name);
        }

        public IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name)
        {
            Calls.Add(name);
            return Enumerated.TryGetValue(name, out var parts)
                ? parts
                : Array.Empty<ISlotPart>();
        }

        public object? ResolveCoordinateHost(string? hostName, Type? hostType) => CoordinateHost;

        public int CountSlots(IReadOnlyList<string> slotNames, IReadOnlyList<string> enumeratorNames)
        {
            var count = 0;
            foreach (var name in slotNames)
            {
                if (Named.ContainsKey(name)) count++;
            }

            foreach (var name in enumeratorNames)
            {
                if (Enumerated.TryGetValue(name, out var parts)) count += parts.Count;
            }

            return count;
        }
    }

    private sealed class FakeHost : ISlotMeasurementHost
    {
        public bool RunPostedImmediately { get; init; } = true;
        public List<Action> Posted { get; } = [];
        public List<ISlotComponent> WrittenSlots { get; } = [];
        public List<(ISlotComponent Slot, Anchor Anchor)> Writes { get; } = [];

        public (double Width, double Height) ReadNodeSize() => (160d, 90d);

        public Anchor ReadAnchor(ISlotComponent slot) => slot.Anchor;

        public void WriteAnchor(ISlotComponent slot, Anchor anchor)
        {
            WrittenSlots.Add(slot);
            Writes.Add((slot, anchor));
            slot.Anchor = anchor;
        }

        public void InvalidateMeasure() { }

        public bool Post(Action flush)
        {
            Posted.Add(flush);
            if (RunPostedImmediately) flush();
            return true;
        }
    }
}
