using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem.CSharp;

namespace VeloxDev.Core.Extension.Test.Serialization;

[TestClass]
public class CSharpObjectSerializationTests
{
    [TestMethod]
    public void SerializeRoundTrip_PersistsWeakConfigurationOnly()
    {
        var node = new CSharpObject
        {
            HostType = typeof(SerializableHost).FullName!
        };
        node.Values.Single(value => value.Path == nameof(SerializableHost.Count)).Value = "17";
        node.SelectedMethod = node.Methods.Single(
            method => method.Name == nameof(SerializableHost.Read)).Signature;

        var json = node.Serialize();
        var restored = json.Deserialize<CSharpObject>();

        Assert.IsFalse(json.Contains("\"Host\":", StringComparison.Ordinal));
        Assert.AreEqual(typeof(SerializableHost).FullName, restored.HostType);
        Assert.IsInstanceOfType<SerializableHost>(restored.Host);
        Assert.AreEqual("17", restored.Values.Single(
            value => value.Path == nameof(SerializableHost.Count)).Value);
        Assert.AreEqual(node.SelectedMethod, restored.SelectedMethod);
    }

    public sealed class SerializableHost
    {
        public int Count { get; set; }
        public int Read() => Count;
    }
}
