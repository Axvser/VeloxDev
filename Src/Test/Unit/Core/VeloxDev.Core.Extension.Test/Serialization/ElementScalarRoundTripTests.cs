using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// Scalars that appear as a collection's element — the one place their read is decided at run time by
/// <c>TryReadScalar</c> rather than by the member's generated reader.
/// </summary>
/// <remarks>
/// Both chains keep a handwritten table here, and nothing forces the two to agree. The async table was missing
/// <c>byte[]</c> altogether and parsed <see cref="DateTime"/> without <c>RoundtripKind</c> for as long as no test
/// put either into a collection — so both members below are the guard, one per defect.
/// </remarks>
[TestClass]
public partial class ElementScalarRoundTripTests
{
    internal sealed partial class ElementModel
    {
        [VeloxProperty] private List<byte[]> payloads = [[1, 2, 250], []];
        [VeloxProperty] private List<DateTime> moments = [new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc)];
        [VeloxProperty] private List<TimeSpan> spans = [TimeSpan.FromMinutes(45)];
        [VeloxProperty] private List<Guid> ids = [Guid.Empty];
        [VeloxProperty] private List<char> letters = ['x', 'y'];
        [VeloxProperty] private List<decimal> moneys = [1.5m];
        [VeloxProperty] private List<float> singles = [2.5f];
        [VeloxProperty] private List<short> sixteens = [5];
    }

    private static void AssertSurvived(ElementModel restored)
    {
        CollectionAssert.AreEqual(new byte[] { 1, 2, 250 }, restored.Payloads[0], "base64 back to bytes");
        CollectionAssert.AreEqual(Array.Empty<byte>(), restored.Payloads[1], "and an empty one");

        Assert.AreEqual(new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc), restored.Moments[0]);
        Assert.AreEqual(DateTimeKind.Utc, restored.Moments[0].Kind, "RoundtripKind keeps the instant");
        Assert.AreEqual(TimeSpan.FromMinutes(45), restored.Spans[0]);
        Assert.AreEqual(Guid.Empty, restored.Ids[0]);
        CollectionAssert.AreEqual(new[] { 'x', 'y' }, restored.Letters);
        Assert.AreEqual(1.5m, restored.Moneys[0]);
        Assert.AreEqual(2.5f, restored.Singles[0]);
        Assert.AreEqual((short)5, restored.Sixteens[0]);
    }

    [TestMethod]
    public void EveryRuntimeTypedElement_SurvivesTheRoundTrip()
        => AssertSurvived(VeloxJsonSerializer.Deserialize<ElementModel>(VeloxJsonSerializer.Serialize(new ElementModel()))!);

    [TestMethod]
    public async Task EveryRuntimeTypedElement_SurvivesTheAsynchronousChain()
    {
        using var stream = new MemoryStream();
        await VeloxJsonSerializer.WriteToAsync(stream, new ElementModel());
        stream.Position = 0;

        AssertSurvived((ElementModel)(await VeloxJsonSerializer.DeserializeAsync(stream, typeof(ElementModel)))!);
    }
}
