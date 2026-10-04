using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The shape-driven paths on the async face: containers inside containers, an <c>object</c> member, and the one
/// document that is refused for being a top-level array.
/// </summary>
/// <remarks>
/// Each of these has a synchronous twin with a test of its own; the async face reaches its copies of them only
/// through the corpus, which carries none of these shapes. The two implementations are separate — the point of
/// this file is that they answer the same.
/// </remarks>
[TestClass]
public partial class AsyncShapeRoundTripTests
{
    /// <summary>A map of lists, and members declared <see cref="object"/> so the reader has to decide by itself.</summary>
    internal sealed partial class AsyncShapeModel
    {
        [VeloxProperty] private Dictionary<string, List<int>>? nested = new()
        {
            ["first"] = [1, 2],
            ["second"] = [3],
        };

        [VeloxProperty] private object? number = 12;
        [VeloxProperty] private object? flag = true;
        [VeloxProperty] private object? words = "text";
    }

    private static AsyncShapeModel Sample() => new();

    private static void AssertSame(AsyncShapeModel restored, string entry)
    {
        Assert.AreEqual(2, restored.Nested!.Count, entry + ": the outer map lost an entry");
        CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Nested["first"], entry + ": the inner list");
        CollectionAssert.AreEqual(new[] { 3 }, restored.Nested["second"], entry + ": the second inner list");

        Assert.AreEqual(12L, restored.Number, entry + ": a number read as object comes back as a long");
        Assert.AreEqual(true, restored.Flag, entry + ": a literal reads back as a bool");
        Assert.AreEqual("text", restored.Words, entry + ": text reads back as text");
    }

    [TestMethod]
    public void EveryShape_SurvivesTheRoundTripOnTheSyncFace()
    {
        var document = VeloxJsonSerializer.Serialize(Sample());

        AssertSame(VeloxJsonSerializer.Deserialize<AsyncShapeModel>(document)!, "string");
    }

    [TestMethod]
    public async Task EveryShape_SurvivesTheRoundTripOnTheAsyncFace()
    {
        var document = VeloxJsonSerializer.Serialize(Sample());

        AssertSame(await VeloxJsonSerializer.DeserializeAsync<AsyncShapeModel>(new ChunkedTextReader(document, 1))!, "chunked TextReader");
        AssertSame(await VeloxJsonSerializer.DeserializeAsync<AsyncShapeModel>(new MemoryStream(Encoding.UTF8.GetBytes(document)))!, "stream");
    }

    [TestMethod]
    public async Task TheAsyncFace_ReproducesItsOwnDocument()
    {
        var document = VeloxJsonSerializer.Serialize(Sample());

        var restored = await VeloxJsonSerializer.DeserializeAsync<AsyncShapeModel>(new ChunkedTextReader(document, 1));

        Assert.AreEqual(document, VeloxJsonSerializer.Serialize(restored!));
    }

    [TestMethod]
    public async Task ATopLevelArray_IsRefusedRatherThanMisread()
    {
        // 声明类型不是数组时，顶层数组是「读错了」而不是「读成了一棵树」—— 两个面都要说同一句话。
        Assert.ThrowsExactly<InvalidOperationException>(
            () => VeloxJsonSerializer.Deserialize("[1, 2]", typeof(TreeDefaultViewModel)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => VeloxJsonSerializer.DeserializeAsync(new ChunkedTextReader("[1, 2]", 1), typeof(TreeDefaultViewModel)));
    }
}
