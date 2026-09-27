using System;
using System.ComponentModel;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

[TestClass]
public class ComponentModelExTests
{
    private sealed class TestModel : INotifyPropertyChanged
    {
        private string? _name;
        private int _count;

        public string? Name
        {
            get => _name;
            set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
        }

        public int Count
        {
            get => _count;
            set { _count = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private enum ProbeKind { Alpha, Beta, Gamma }

    /// <summary>Stands in for <c>BranchSegment.CompileKey</c> / <c>BranchOption.Key</c>: an enum in an object member.</summary>
    private sealed class KeyHolder : INotifyPropertyChanged
    {
        private object? _key;

        public object? Key
        {
            get => _key;
            set { _key = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Key))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>Mirrors the fix the compiled graph's branch keys need: remember the key's type, restore it on load.</summary>
    private sealed class KeyHolderWithTypeName : INotifyPropertyChanged
    {
        private object? _key;
        private string? _keyTypeName;

        public object? Key
        {
            get => _key;
            set { _key = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Key))); }
        }

        public string? KeyTypeName
        {
            get => _keyTypeName;
            set { _keyTypeName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KeyTypeName))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        [OnDeserialized]
        internal void NormalizeKey(StreamingContext _)
        {
            if (_key is long number
                && _keyTypeName is { Length: > 0 } name
                && Type.GetType(name) is { IsEnum: true } type)
            {
                _key = Enum.ToObject(type, number);
            }
        }
    }

    /// <summary>
    /// Establishes, against the real serializer, the fact the compiled-graph document is built around: an enum in an
    /// <c>object?</c> member comes back as its number. This is why <c>BranchSegment.CompileKey</c> and
    /// <c>BranchOption.Key</c> carry a type-name side channel — without one, a restored graph's <i>dynamic</i>
    /// branch compares a live enum against a restored <c>long</c> and never matches.
    /// </summary>
    /// <remarks>
    /// The repo already recorded the same behaviour for <c>ConditionalSlot.Value</c>
    /// (<c>Core.Test/WorkflowSystem/SlotEnumeratorTests.cs:384-387</c>), which is why <c>SlotEnumerator</c> has
    /// <c>SelectorTypeName</c>. This pins it for an <c>object?</c> member at the serializer level.
    /// </remarks>
    [TestMethod]
    public void AnEnumInAnObjectMember_ComesBackAsItsNumber()
    {
        var json = new KeyHolder { Key = ProbeKind.Beta }.Serialize();

        Assert.IsTrue(json.TryDeserialize<KeyHolder>(out var restored));
        Assert.AreEqual(typeof(long), restored!.Key!.GetType(),
            $"measured behaviour: the enum degrades to its underlying number; json was: {json}");
        Assert.AreEqual(1L, restored.Key);
    }

    /// <summary>
    /// A dead end worth pinning so nobody "simplifies" to it: asking for type names on every value does not rescue
    /// the enum either — measured, the round trip still yields <see cref="long"/>.
    /// </summary>
    [TestMethod]
    public void AnEnumInAnObjectMember_IsNotRescuedByTypeNamesOnEveryValue()
    {
        var options = SerializationOptions.Create().WithTypeNameHandling(TypeNameHandling.All);
        var json = new KeyHolder { Key = ProbeKind.Beta }.Serialize(options);

        Assert.IsTrue(json.TryDeserialize(options, out KeyHolder? restored));
        Assert.AreEqual(typeof(long), restored!.Key!.GetType(),
            $"WithTypeNameHandling(All) does not help; json was: {json}");
    }

    /// <summary>The type-name side channel does work — proved in miniature before being applied to the segment types.</summary>
    [TestMethod]
    public void AnEnumInAnObjectMember_WithATypeNameBesideIt_RoundTrips()
    {
        var original = new KeyHolderWithTypeName
        {
            Key = ProbeKind.Beta,
            KeyTypeName = typeof(ProbeKind).AssemblyQualifiedName,
        };

        var restored = original.Serialize().Deserialize<KeyHolderWithTypeName>();

        Assert.AreEqual(ProbeKind.Beta, restored.Key,
            "an [OnDeserialized] normalizer plus the type name is what the compiled graph's keys rely on");
    }

    [TestMethod]
    public void Serialize_And_TryDeserialize_RoundTrips()
    {
        var original = new TestModel { Name = "Test", Count = 42 };
        var json = original.Serialize();

        Assert.IsFalse(string.IsNullOrWhiteSpace(json));

        Assert.IsTrue(json.TryDeserialize<TestModel>(out var restored));
        Assert.IsNotNull(restored);
        Assert.AreEqual("Test", restored!.Name);
        Assert.AreEqual(42, restored.Count);
    }

    [TestMethod]
    public async Task SerializeAsync_And_DeserializeAsync_RoundTrips()
    {
        var original = new TestModel { Name = "Async", Count = 99 };
        var json = await original.SerializeAsync();

        var restored = await json.DeserializeAsync<TestModel>();
        Assert.AreEqual("Async", restored.Name);
        Assert.AreEqual(99, restored.Count);
    }

    [TestMethod]
    public void TryDeserialize_InvalidJson_ReturnsFalse()
    {
        var success = "not valid json {{{".TryDeserialize<TestModel>(out var restored);
        Assert.IsFalse(success);
        Assert.IsNull(restored);
    }

    [TestMethod]
    public void CanvasLayout_Scale_RoundTrips()
    {
        var original = new CanvasLayout
        {
            Scale = new Scale(2.5, 3.5),
            NegativeOffset = new Offset(50, 30),
        };
        var json = original.Serialize();

        var restored = json.Deserialize<CanvasLayout>();
        Assert.AreEqual(new Scale(2.5, 3.5), restored.Scale);
        Assert.AreEqual(new Offset(50, 30), restored.NegativeOffset);
        Assert.AreEqual(2.5, restored.Scale.Horizontal);
        Assert.AreEqual(3.5, restored.Scale.Vertical);
    }

    [TestMethod]
    public void Deserialize_RoundTrips()
    {
        var original = new TestModel { Name = "Sync", Count = 11 };
        var json = original.Serialize();

        var restored = json.Deserialize<TestModel>();
        Assert.AreEqual("Sync", restored.Name);
        Assert.AreEqual(11, restored.Count);
    }

    [TestMethod]
    public async Task SerializeToStreamAsync_And_DeserializeFromStreamAsync_RoundTrips()
    {
        var original = new TestModel { Name = "Stream", Count = 123 };

        using var stream = new MemoryStream();
        await original.SerializeToStreamAsync(stream);

        stream.Position = 0;
        var restored = await stream.DeserializeFromStreamAsync<TestModel>();
        Assert.AreEqual("Stream", restored.Name);
        Assert.AreEqual(123, restored.Count);
    }

    [TestMethod]
    public async Task SerializeToUtf8Bytes_And_DeserializeFromUtf8Bytes_RoundTrips()
    {
        var original = new TestModel { Name = "Bytes", Count = 314 };

        var bytes = original.SerializeToUtf8Bytes();
        var restored = bytes.DeserializeFromUtf8Bytes<TestModel>();

        Assert.AreEqual("Bytes", restored.Name);
        Assert.AreEqual(314, restored.Count);
    }

    [TestMethod]
    public async Task SerializeToTextWriterAsync_And_DeserializeFromTextReaderAsync_RoundTrips()
    {
        var original = new TestModel { Name = "Text", Count = 2718 };

        using var writer = new StringWriter();
        await original.SerializeToTextWriterAsync(writer);

        using var reader = new StringReader(writer.ToString());
        var restored = await reader.DeserializeFromTextReaderAsync<TestModel>();

        Assert.AreEqual("Text", restored.Name);
        Assert.AreEqual(2718, restored.Count);
    }

    [TestMethod]
    public async Task SerializeAsync_Null_Throws()
    {
        TestModel? model = null;
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await model!.SerializeAsync());
    }

    [TestMethod]
    public async Task DeserializeAsync_Empty_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () => await "".DeserializeAsync<TestModel>());
    }

    [TestMethod]
    public async Task SerializeToStreamAsync_NullStream_Throws()
    {
        var model = new TestModel { Name = "x" };
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await model.SerializeToStreamAsync(null!));
    }

    [TestMethod]
    public async Task DeserializeFromStreamAsync_NullStream_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await ((Stream)null!).DeserializeFromStreamAsync<TestModel>());
    }

    [TestMethod]
    public async Task SerializeAsync_Canceled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var model = new TestModel { Name = "Canceled" };
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await model.SerializeAsync(cts.Token));
    }

    [TestMethod]
    public async Task DeserializeFromTextReaderAsync_Canceled_Throws()
    {
        using var cts = new CancellationTokenSource();
        using var reader = new StringReader("{\"Name\":\"Value\",\"Count\":1}");
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await reader.DeserializeFromTextReaderAsync<TestModel>(cancellationToken: cts.Token));
    }

    [TestMethod]
    public void Serialize_NullProperty_IncludesNull()
    {
        var model = new TestModel { Name = null, Count = 0 };
        var json = model.Serialize();
        Assert.Contains("null", json);
    }

    [TestMethod]
    public void DeserializeFromUtf8Bytes_Empty_Throws()
    {
        try
        {
            Array.Empty<byte>().DeserializeFromUtf8Bytes<TestModel>();
            Assert.Fail();
        }
        catch (ArgumentException)
        {
        }
    }
}
