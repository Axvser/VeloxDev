using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The number spellings the format pins: non-finite values, the values where two numbers differ by one bit,
/// and the rounding extremes.
/// </summary>
/// <remarks>
/// Two of these are the cases where "the value came back" is not the assertion. A negative zero equals zero
/// under <c>==</c> and differs in one bit, and the non-finite values are written as JSON <b>strings</b>, so a
/// reader that took them for text would still look right in a log. The assertion is the document: what is
/// written back out must be the bytes that came in.
/// </remarks>
[TestClass]
public partial class NumberSpellingTests
{
    internal sealed partial class NumberModel
    {
        [VeloxProperty] private double notANumber;
        [VeloxProperty] private double above;
        [VeloxProperty] private double below;
        [VeloxProperty] private double negativeZero;

        // 可空那一条走的是守卫的另一个分支；`float` 的负零按值拓宽后同样要保住符号。
        [VeloxProperty] private double? maybeNegativeZero;
        [VeloxProperty] private float negativeZeroSingle;

        // 集合元素走的是引擎而不是属性守卫 —— 作为对照，它本来就是对的。
        [VeloxProperty] private List<double> awryInAList = [];

        [VeloxProperty] private double subnormal;
        [VeloxProperty] private double largest;
        [VeloxProperty] private double third;
        [VeloxProperty] private float single;
        [VeloxProperty] private float singleNotANumber;
        [VeloxProperty] private decimal money;
        [VeloxProperty] private decimal scale;
    }

    private static NumberModel Sample() => new()
    {
        NotANumber = double.NaN,
        Above = double.PositiveInfinity,
        Below = double.NegativeInfinity,
        NegativeZero = -0.0,
        MaybeNegativeZero = -0.0,
        NegativeZeroSingle = -0.0f,
        AwryInAList = [-0.0],
        Subnormal = double.Epsilon,
        Largest = double.MaxValue,
        Third = 1.0 / 3.0,
        Single = 0.1f,
        SingleNotANumber = float.NaN,
        Money = 1.00m,
        Scale = decimal.MaxValue,
    };



    [TestMethod]
    public void NonFiniteValues_AreWrittenAsStrings()
    {
        // JSON has no spelling for them，所以它们写成字符串 —— 读侧认三种字面量。
        var document = VeloxJsonSerializer.Serialize(Sample());

        StringAssert.Contains(document, "\"NotANumber\": \"NaN\"");
        StringAssert.Contains(document, "\"Above\": \"Infinity\"");
        StringAssert.Contains(document, "\"Below\": \"-Infinity\"");
    }

    [TestMethod]
    public void EverySpelling_SurvivesTheRoundTrip()
    {
        var restored = VeloxJsonSerializer.Deserialize<NumberModel>(VeloxJsonSerializer.Serialize(Sample()))!;

        Assert.IsTrue(double.IsNaN(restored.NotANumber), "NaN is not a number, so it cannot be compared");
        Assert.IsTrue(double.IsPositiveInfinity(restored.Above));
        Assert.IsTrue(double.IsNegativeInfinity(restored.Below));
        Assert.AreEqual(double.Epsilon, restored.Subnormal);
        Assert.AreEqual(double.MaxValue, restored.Largest);
        Assert.AreEqual(1.0 / 3.0, restored.Third);
        Assert.AreEqual(0.1f, restored.Single);
        Assert.IsTrue(float.IsNaN(restored.SingleNotANumber));
        Assert.AreEqual(1.00m, restored.Money);
        Assert.AreEqual(decimal.MaxValue, restored.Scale);
    }

    [TestMethod]
    public void ANegativeZero_KeepsItsSignBit()
    {
        // `-0.0 == 0.0` 为真，所以只有位模式能说明它是不是回来了。
        var document = VeloxJsonSerializer.Serialize(Sample());

        StringAssert.Contains(document, "\"NegativeZero\": -0.0", "the writer must spell the sign out");

        var restored = VeloxJsonSerializer.Deserialize<NumberModel>(document)!;

        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(-0.0),
            BitConverter.DoubleToInt64Bits(restored.NegativeZero),
            "the sign bit of a negative zero must survive");

        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(-0.0),
            BitConverter.DoubleToInt64Bits(restored.MaybeNegativeZero!.Value),
            "and so must a nullable one's");

        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(-0.0),
            BitConverter.DoubleToInt64Bits(restored.NegativeZeroSingle),
            "and a float's, on the way through the widening");

        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(-0.0),
            BitConverter.DoubleToInt64Bits(restored.AwryInAList[0]),
            "a collection element has no property guard in the way");
    }

    [TestMethod]
    public void EverySpelling_ReproducesTheDocumentByteForByte()
    {
        var document = VeloxJsonSerializer.Serialize(Sample());

        var restored = VeloxJsonSerializer.Deserialize<NumberModel>(document)!;

        Assert.AreEqual(document, VeloxJsonSerializer.Serialize(restored), "a number was respelled on the way back");
    }

    [TestMethod]
    public async Task EverySpelling_ReproducesTheDocumentOnTheAsyncFace()
    {
        var document = VeloxJsonSerializer.Serialize(Sample());

        var restored = await VeloxJsonSerializer.DeserializeAsync<NumberModel>(new ChunkedTextReader(document, 1));

        Assert.AreEqual(document, VeloxJsonSerializer.Serialize(restored!), "the async face respelled a number");
    }
}
