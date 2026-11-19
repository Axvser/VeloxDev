using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// What <c>[Archive]</c> changes about one member's part in the document — and what it leaves alone.
/// </summary>
[TestClass]
public partial class ArchiveAttributeTests
{
    /// <summary>
    /// Four members, each reaching the document by a different route: the promoted default, a computed property,
    /// a field with no property beside it, and a property written under another name.
    /// </summary>
    internal sealed partial class ArchiveModel
    {
        [VeloxProperty] private int count;

        /// <summary>Computed, so the default rules drop it; <c>KeepProperty</c> is what puts it back.</summary>
        [Archive(ArchiveOptions.KeepProperty)]
        public int Doubled => count * 2;

        /// <summary>No property corresponds to this field, so the default rules drop it too.</summary>
        [Archive(ArchiveOptions.KeepField)]
        internal string? note;

        /// <summary>An ordinary property, written under a name of its own choosing.</summary>
        [Archive(ArchiveOptions.ReName, "label")]
        public string? Name { get; set; }
    }

    /// <summary>Two promoted fields, one of which is excluded outright.</summary>
    internal sealed partial class IgnoredFieldModel
    {
        [VeloxProperty] private int kept;

        [VeloxProperty]
        [Archive(ArchiveOptions.IgnoreField)]
        private int dropped;
    }

    private static ArchiveModel Sample() => new() { Count = 3, note = "kept", Name = "renamed" };

    [TestMethod]
    public void EachOption_PutsItsMemberInTheDocument()
    {
        var json = VeloxJsonSerializer.Serialize(Sample());

        StringAssert.Contains(json, "\"Count\"", "the default rules still take a promoted property");
        StringAssert.Contains(json, "\"Doubled\"", "KeepProperty admits a computed property");
        StringAssert.Contains(json, "\"note\"", "KeepField admits a field with no property beside it");
        StringAssert.Contains(json, "\"label\"", "ReName writes the member under the given name");
        Assert.IsFalse(json.Contains("\"Name\""), "the member's own name is replaced, not joined");
    }

    [TestMethod]
    public void TheMembersThatCanBeReadBack_AreReadBack()
    {
        var restored = VeloxJsonSerializer.Deserialize<ArchiveModel>(VeloxJsonSerializer.Serialize(Sample()))!;

        Assert.AreEqual(3, restored.Count, "a promoted property survives the round trip");
        Assert.AreEqual("kept", restored.note, "a kept field survives the round trip");
        Assert.AreEqual("renamed", restored.Name, "a renamed property survives under its CLR name");
    }

    [TestMethod]
    public void IgnoreField_LeavesTheMemberOutOfTheDocument()
    {
        var json = VeloxJsonSerializer.Serialize(new IgnoredFieldModel { Kept = 4, Dropped = 9 });
        var restored = VeloxJsonSerializer.Deserialize<IgnoredFieldModel>(json)!;

        StringAssert.Contains(json, "\"Kept\"");
        Assert.IsFalse(json.Contains("\"Dropped\""), "an excluded member is not written at all");
        Assert.AreEqual(4, restored.Kept);
        Assert.AreEqual(0, restored.Dropped, "nothing restored it, because nothing wrote it");
    }
}
