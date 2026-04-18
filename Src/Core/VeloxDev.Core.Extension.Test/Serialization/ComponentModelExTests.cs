using System.ComponentModel;
using VeloxDev.MVVM.Serialization;

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
    public async Task TryDeserializeAsync_ValidJson_Succeeds()
    {
        var original = new TestModel { Name = "Try", Count = 7 };
        var json = await original.SerializeAsync();

        var (success, result) = await json.TryDeserializeAsync<TestModel>();
        Assert.IsTrue(success);
        Assert.IsNotNull(result);
        Assert.AreEqual("Try", result!.Name);
    }

    [TestMethod]
    public async Task TryDeserializeAsync_InvalidJson_ReturnsFalse()
    {
        var (success, _) = await "not valid json {{{".TryDeserializeAsync<TestModel>();
        Assert.IsFalse(success);
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
    public async Task TryDeserializeFromStreamAsync_ValidStream_Succeeds()
    {
        var original = new TestModel { Name = "TryStream", Count = 456 };

        using var stream = new MemoryStream();
        await original.SerializeToStreamAsync(stream);
        stream.Position = 0;

        var (success, result) = await stream.TryDeserializeFromStreamAsync<TestModel>();
        Assert.IsTrue(success);
        Assert.AreEqual("TryStream", result!.Name);
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
    public void Serialize_NullProperty_IncludesNull()
    {
        var model = new TestModel { Name = null, Count = 0 };
        var json = model.Serialize();
        Assert.IsTrue(json.Contains("null"));
    }
}
