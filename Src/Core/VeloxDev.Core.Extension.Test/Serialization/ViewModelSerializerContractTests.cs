using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// What each convenience entry promises when it is handed nothing: the exception it names, the stream it
/// refuses, and the token it honours.
/// </summary>
/// <remarks>
/// These are the branches a caller only meets on the unhappy path, so nothing else in the suite reaches them:
/// an empty document, a null source, a stream that cannot be read, a canceled token, and the one async entry
/// that throws where its synchronous twin hands back a null.
/// </remarks>
[TestClass]
public partial class ViewModelSerializerContractTests
{
    internal sealed partial class ContractModel
    {
        [VeloxProperty] private int count;
    }

    private static ContractModel Sample() => new() { Count = 1 };

    private static string Document() => VeloxJsonSerializer.Serialize(Sample());

    /// <summary>A stream that answers one direction only — the shape the guards exist for.</summary>
    private sealed class OneWayStream(bool canRead, bool canWrite) : MemoryStream
    {
        public override bool CanRead => canRead;
        public override bool CanWrite => canWrite;
    }

    private static CancellationToken Canceled()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        return source.Token;
    }

    [TestMethod]
    public void AnEmptyDocument_IsRefusedByEveryStringEntry()
    {
        foreach (var blank in new[] { string.Empty, "   " })
        {
            Assert.IsFalse(blank.TryDeserialize<ContractModel>(out var tried), "TryDeserialize answers false");
            Assert.IsNull(tried);
            Assert.IsFalse(blank.TryDeserialize<ContractModel>(SerializationOptions.Create(), out _), "the options overload too");

            Assert.ThrowsExactly<ArgumentException>(() => blank.Deserialize<ContractModel>(), "Deserialize names the argument");
            Assert.ThrowsExactly<ArgumentException>(() => blank.Deserialize<ContractModel>(SerializationOptions.Create()), "options overload");
            Assert.ThrowsExactly<ArgumentException>(() => blank.DeserializeAsync<ContractModel>(), "DeserializeAsync");
            Assert.ThrowsExactly<ArgumentException>(() => blank.DeserializeAsync<ContractModel>(SerializationOptions.Create()), "async options overload");
        }
    }

    [TestMethod]
    public void AValueTree_RefusesNothingAndReadsIntoAType()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ViewModelSerializer.DeserializeToType(null!, typeof(ContractModel)));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonValue.Parse("{}").DeserializeToType(null!));

        Assert.IsNull(VeloxJsonValue.Null.DeserializeToType(typeof(ContractModel)), "the JSON literal reads back as nothing");
    }

    [TestMethod]
    public void EveryStreamEntry_RefusesANullSource()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Sample().SerializeToTextWriter((TextWriter)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Sample().SerializeToStream((Stream)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ((TextReader)null!).DeserializeFromTextReader<ContractModel>());
        Assert.ThrowsExactly<ArgumentNullException>(() => ((Stream)null!).DeserializeFromStream<ContractModel>());

        Assert.ThrowsExactly<ArgumentNullException>(() => ((ContractModel)null!).SerializeToTextWriter(new StringWriter()));
        Assert.ThrowsExactly<ArgumentNullException>(() => ((ContractModel)null!).SerializeToStream(new MemoryStream()));
        Assert.ThrowsExactly<ArgumentNullException>(() => ((ContractModel)null!).SerializeToUtf8Bytes());
    }

    [TestMethod]
    public void AStreamPointingTheWrongWay_IsRefused()
    {
        using var writeOnly = new OneWayStream(canRead: false, canWrite: true);
        Assert.ThrowsExactly<InvalidOperationException>(() => writeOnly.DeserializeFromStream<ContractModel>(), "cannot read");

        using var readOnly = new OneWayStream(canRead: true, canWrite: false);
        Assert.ThrowsExactly<InvalidOperationException>(() => Sample().SerializeToStream(readOnly), "cannot write");
    }

    [TestMethod]
    public async Task AStreamPointingTheWrongWay_IsRefusedOnTheAsyncFace()
    {
        using var writeOnly = new OneWayStream(canRead: false, canWrite: true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writeOnly.DeserializeFromStreamAsync<ContractModel>());

        using var readOnly = new OneWayStream(canRead: true, canWrite: false);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Sample().SerializeToStreamAsync(readOnly));
    }

    [TestMethod]
    public async Task EveryAsyncEntry_RefusesANullSource()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((ContractModel)null!).SerializeAsync());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((ContractModel)null!).SerializeToTextWriterAsync(new StringWriter()));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((ContractModel)null!).SerializeToStreamAsync(new MemoryStream()));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => Sample().SerializeToTextWriterAsync((TextWriter)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => Sample().SerializeToStreamAsync((Stream)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((TextReader)null!).DeserializeFromTextReaderAsync<ContractModel>());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Stream)null!).DeserializeFromStreamAsync<ContractModel>());
    }

    [TestMethod]
    public async Task ACanceledToken_StopsEveryAsyncEntry()
    {
        var canceled = Canceled();
        var document = Document();
        var bytes = Encoding.UTF8.GetBytes(document);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Sample().SerializeAsync(cancellationToken: canceled));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Sample().SerializeAsync(SerializationOptions.Create(), canceled));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => document.DeserializeAsync<ContractModel>(canceled));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => document.DeserializeAsync<ContractModel>(SerializationOptions.Create(), canceled));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Sample().SerializeToUtf8BytesAsync(cancellationToken: canceled));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => bytes.DeserializeFromUtf8BytesAsync<ContractModel>(cancellationToken: canceled));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Sample().SerializeToTextWriterAsync(new StringWriter(), cancellationToken: canceled));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new StringReader(document).DeserializeFromTextReaderAsync<ContractModel>(cancellationToken: canceled));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Sample().SerializeToStreamAsync(new MemoryStream(), cancellationToken: canceled));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new MemoryStream(bytes).DeserializeFromStreamAsync<ContractModel>(cancellationToken: canceled));
    }

    [TestMethod]
    public void TheByteEntry_RefusesNothingAndReadsBackWhatItWrote()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ((byte[])null!).DeserializeFromUtf8Bytes<ContractModel>());
        Assert.ThrowsExactly<ArgumentException>(() => Array.Empty<byte>().DeserializeFromUtf8Bytes<ContractModel>());

        var bytes = Sample().SerializeToUtf8Bytes();
        Assert.AreEqual(1, bytes.DeserializeFromUtf8Bytes<ContractModel>()!.Count, "the options overload");
        Assert.AreEqual(1, bytes.DeserializeFromUtf8Bytes<ContractModel>(SerializationOptions.Create())!.Count);
    }

    [TestMethod]
    public async Task TheStreamingEntries_ReadBackWhatTheyWrote()
    {
        using var text = new StringWriter();
        await Sample().SerializeToTextWriterAsync(text, SerializationOptions.Create());
        Assert.AreEqual(1, (await new StringReader(text.ToString()).DeserializeFromTextReaderAsync<ContractModel>())!.Count);

        using var stream = new MemoryStream();
        await Sample().SerializeToStreamAsync(stream, SerializationOptions.Create());
        stream.Position = 0;
        Assert.AreEqual(1, (await stream.DeserializeFromStreamAsync<ContractModel>())!.Count);
    }

    [TestMethod]
    public async Task ADocumentThatHoldsNothing_IsANullFromTheSyncEntryAndAThrowFromTheAsyncOne()
    {
        // 两条入口在这里刻意不同：同步那条把 null 交回给调用方，异步那条抛 —— 写下来免得下次「统一」它们。
        Assert.IsNull(new StringReader("null").DeserializeFromTextReader<ContractModel>(), "the sync entry hands back the null");
        Assert.IsNull(new MemoryStream(Encoding.UTF8.GetBytes("null")).DeserializeFromStream<ContractModel>(), "the same for a stream");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => new StringReader("null").DeserializeFromTextReaderAsync<ContractModel>());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => new MemoryStream(Encoding.UTF8.GetBytes("null")).DeserializeFromStreamAsync<ContractModel>());
    }

    [TestMethod]
    public async Task TheByteEntry_ReadsBackWhatItWroteOnTheAsyncFace()
    {
        var bytes = await Sample().SerializeToUtf8BytesAsync();

        var restored = await bytes.DeserializeFromUtf8BytesAsync<ContractModel>();

        Assert.AreEqual(1, restored!.Count);
    }
}
