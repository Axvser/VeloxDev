using System;
using System.Text.Json.Serialization;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// Members the document has to carry: C#'s <c>required</c> and <c>System.Text.Json</c>'s <c>[JsonRequired]</c>.
/// </summary>
/// <remarks>
/// A type with a required member used to be unserializable outright — the generated factory wrote <c>new T()</c>,
/// and C# refuses to compile that when a required member is left unassigned.
/// </remarks>
[TestClass]
public partial class RequiredMemberTests
{
    internal sealed partial class RequiredModel
    {
        [VeloxProperty] private int count;

        public required string Tag { get; set; }
    }

    internal sealed partial class JsonRequiredModel
    {
        [VeloxProperty] private int count;

        [JsonRequired]
        public string? Tag { get; set; }
    }

    /// <summary>A document that carries everything the models below need.</summary>
    private const string Complete = """
        {
          "$id": "1",
          "Count": 3,
          "Tag": "t"
        }
        """;

    /// <summary>The same document with the one member both models refuse to do without.</summary>
    private const string WithoutTag = """
        {
          "$id": "1",
          "Count": 3
        }
        """;

    [TestMethod]
    public void ATypeWithARequiredMember_SerializesAtAll()
    {
        var restored = VeloxJsonSerializer.Deserialize<RequiredModel>(
            VeloxJsonSerializer.Serialize(new RequiredModel { Count = 1, Tag = "t" }))!;

        Assert.AreEqual("t", restored.Tag);
        Assert.AreEqual(1, restored.Count);
    }

    [TestMethod]
    public void ADocumentWithoutARequiredMember_IsRefused()
    {
        StringAssert.Contains(Complete, "\"Tag\"");

        Assert.ThrowsExactly<InvalidOperationException>(() => VeloxJsonSerializer.Deserialize<RequiredModel>(WithoutTag));
        Assert.AreEqual(3, VeloxJsonSerializer.Deserialize<RequiredModel>(Complete)!.Count, "the complete one loads");
    }

    [TestMethod]
    public void JsonRequired_IsRefusedTheSameWay()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => VeloxJsonSerializer.Deserialize<JsonRequiredModel>(WithoutTag));
        Assert.AreEqual("t", VeloxJsonSerializer.Deserialize<JsonRequiredModel>(Complete)!.Tag);
    }
}
