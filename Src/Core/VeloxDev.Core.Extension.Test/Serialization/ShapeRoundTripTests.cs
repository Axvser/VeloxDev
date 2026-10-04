using System;
using System.Collections.Generic;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The shapes that used to be written but not read back: arrays, read-only collections, the other whitelisted
/// collections, and the two values whose round trip was lossy.
/// </summary>
[TestClass]
public partial class ShapeRoundTripTests
{
    /// <summary>One member per shape, so a single round trip covers the lot.</summary>
    internal sealed partial class ShapeModel
    {
        [VeloxProperty] private int[] numbers = [1, 2, 3];
        [VeloxProperty] private byte[] payload = [1, 2, 250];
        [VeloxProperty] private IReadOnlyList<string> readOnly = new List<string> { "a", "b" };
        [VeloxProperty] private IEnumerable<int> sequence = new List<int> { 7, 8 };
        [VeloxProperty] private HashSet<int> set = [1, 2, 2];
        [VeloxProperty] private Dictionary<int, string> map = new() { [1] = "one", [2] = "two" };
        [VeloxProperty] private Dictionary<double, string> fractional = new() { [1.5] = "half" };

        // 没有初值：读的时候成员是 null，而文档里有内容 —— 这正是过去会被静默跳过的那种。
        [VeloxProperty] private List<int>? lazy;

        [VeloxProperty] private DateTime moment = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    }

    private static ShapeModel Sample() => new() { Lazy = [4, 5, 6] };

    [TestMethod]
    public void EveryShape_SurvivesTheRoundTrip()
    {
        var restored = VeloxJsonSerializer.Deserialize<ShapeModel>(VeloxJsonSerializer.Serialize(Sample()))!;

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, restored.Numbers, "an array reads back as an array");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 250 }, restored.Payload, "byte[] reads back as bytes");
        CollectionAssert.AreEqual(
            new[] { "a", "b" }, new List<string>(restored.ReadOnly), "a read-only list reads back");
        CollectionAssert.AreEqual(
            new[] { 7, 8 }, new List<int>(restored.Sequence), "IEnumerable<T> reads back");
        Assert.AreEqual(2, restored.Set.Count, "a set reads back");
        Assert.AreEqual("two", restored.Map[2], "a map reads back");
        Assert.AreEqual("half", restored.Fractional[1.5], "a fractional key reads back");
    }

    [TestMethod]
    public void AMemberThatHeldNothing_IsFilledRatherThanSteppedOver()
    {
        // 过去这一条是静默丢数据：成员为 null 时生成代码直接跳过整个值。现在它会造一个实例再读。
        var restored = VeloxJsonSerializer.Deserialize<ShapeModel>(VeloxJsonSerializer.Serialize(Sample()))!;

        CollectionAssert.AreEqual(new[] { 4, 5, 6 }, restored.Lazy!);
    }

    [TestMethod]
    public void Bytes_AreWrittenAsBase64_NotAsAnArrayOfNumbers()
    {
        var json = VeloxJsonSerializer.Serialize(Sample());

        StringAssert.Contains(json, "\"Payload\": \"AQL6\"", "base64, the spelling STJ and Json.NET agree on");
    }

    [TestMethod]
    public void AUtcMoment_ComesBackAsTheSameInstant()
    {
        var restored = VeloxJsonSerializer.Deserialize<ShapeModel>(VeloxJsonSerializer.Serialize(Sample()))!;

        Assert.AreEqual(DateTimeKind.Utc, restored.Moment.Kind, "the kind is part of the value, not a display detail");
        Assert.AreEqual(new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc), restored.Moment);
    }
}
