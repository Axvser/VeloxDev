using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// <c>[Archive(ArchiveOptions.EnumName)]</c>: an enum written as the name of its value, while every other enum in
/// the document keeps the number it has always carried.
/// </summary>
[TestClass]
public partial class EnumNameTests
{
    internal sealed partial class EnumNameModel
    {
        [VeloxProperty] private int count;

        [VeloxProperty]
        [Archive(ArchiveOptions.EnumName)]
        private ShapeRoundTripTests.ScalarKind named;

        [VeloxProperty]
        [Archive(ArchiveOptions.EnumName)]
        private ShapeRoundTripTests.ScalarKind? maybe;

        /// <summary>Unmarked, so it keeps the numeric spelling the format has always used.</summary>
        [VeloxProperty] private ShapeRoundTripTests.ScalarKind numeric;
    }

    private static EnumNameModel Sample() => new()
    {
        Named = ShapeRoundTripTests.ScalarKind.Second,
        Maybe = ShapeRoundTripTests.ScalarKind.First,
        Numeric = ShapeRoundTripTests.ScalarKind.Second,
    };

    [TestMethod]
    public void OnlyTheMarkedMember_IsWrittenByName()
    {
        var json = VeloxJsonSerializer.Serialize(Sample());

        StringAssert.Contains(json, "\"Named\": \"Second\"", "the name, not the number");
        StringAssert.Contains(json, "\"Numeric\": 1", "an unmarked enum is unchanged");
    }

    [TestMethod]
    public void ANamedEnum_RoundTrips()
    {
        var restored = VeloxJsonSerializer.Deserialize<EnumNameModel>(VeloxJsonSerializer.Serialize(Sample()))!;

        Assert.AreEqual(ShapeRoundTripTests.ScalarKind.Second, restored.Named);
        Assert.AreEqual(ShapeRoundTripTests.ScalarKind.First, restored.Maybe);
        Assert.AreEqual(ShapeRoundTripTests.ScalarKind.Second, restored.Numeric);
    }

    [TestMethod]
    public void ANullableNamedEnum_WritesAndReadsTheJsonLiteral()
    {
        var model = Sample();
        model.Maybe = null;

        var json = VeloxJsonSerializer.Serialize(model);
        StringAssert.Contains(json, "\"Maybe\": null", "an absent value is the literal, not an empty string");

        Assert.IsNull(VeloxJsonSerializer.Deserialize<EnumNameModel>(json)!.Maybe);
    }
}
