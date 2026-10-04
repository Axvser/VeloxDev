using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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

    [TestMethod]
    public async Task TheTwoChains_SpellEveryScalarTheSameWay()
    {
        // 同步与异步各有一张手写的标量表（`TryWriteScalar` / `TryWriteScalarAsync`），没有东西强制它们一致。
        // 这条用例把每种标量都过一遍两条路并逐字节比 —— 那是唯一能让「只改了一侧」当场红掉的闸。
        var scalars = new ScalarModel
        {
            Text = "t", Flag = true, Small = 1, Big = 2L, Fraction = 1.5, Single = 2.5f,
            Money = 3.5m, Octet = 4, Sixteen = 5, Letter = 'x', Id = Guid.Empty,
            Moment = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            Span = TimeSpan.FromMinutes(1), Kind = ScalarKind.Second, Bytes = [9, 8],
        };

        var sync = VeloxJsonSerializer.Serialize(scalars);
        using var text = new StringWriter();
        await VeloxJsonSerializer.WriteToAsync(text, scalars);

        Assert.AreEqual(sync, text.ToString(), "the two chains must spell the same document");
    }

    /// <summary>Public because the promoted property is: an internal type behind one would not compile.</summary>
    public enum ScalarKind { First, Second }

    /// <summary>One member per scalar the format writes with a primitive of its own.</summary>
    internal sealed partial class ScalarModel
    {
        [VeloxProperty] private string? text;
        [VeloxProperty] private bool flag;
        [VeloxProperty] private int small;
        [VeloxProperty] private long big;
        [VeloxProperty] private double fraction;
        [VeloxProperty] private float single;
        [VeloxProperty] private decimal money;
        [VeloxProperty] private byte octet;
        [VeloxProperty] private short sixteen;
        [VeloxProperty] private char letter;
        [VeloxProperty] private Guid id;
        [VeloxProperty] private DateTime moment;
        [VeloxProperty] private TimeSpan span;
        [VeloxProperty] private ScalarKind kind;
        [VeloxProperty] private byte[] bytes = [];
    }

    [TestMethod]
    public async Task EveryShape_AlsoSurvivesTheAsynchronousChain()
    {
        // 同步与异步是两条独立生成的链路（readers/writers 各一对），新加的守卫与四种读法两边都要发到 ——
        // 只跑同步那一条等于只覆盖一半。
        using var stream = new MemoryStream();
        await VeloxJsonSerializer.WriteToAsync(stream, Sample());
        stream.Position = 0;

        var restored = (ShapeModel?)await VeloxJsonSerializer.DeserializeAsync(stream, typeof(ShapeModel));

        Assert.IsNotNull(restored);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, restored.Numbers);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 250 }, restored.Payload);
        CollectionAssert.AreEqual(new[] { "a", "b" }, new List<string>(restored.ReadOnly));
        Assert.AreEqual("two", restored.Map[2]);
        CollectionAssert.AreEqual(new[] { 4, 5, 6 }, restored.Lazy!);
        Assert.AreEqual(DateTimeKind.Utc, restored.Moment.Kind);
    }
}
