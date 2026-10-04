using System;
using System.Collections.Generic;
using System.Globalization;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The shape-driven value tree: turning a live value into a node, and the object node's own bookkeeping.
/// </summary>
/// <remarks>
/// <c>VeloxJsonValue.From</c> is the one bridge a tool result crosses into the document format, and its scalar
/// fan-out is a table of spellings nothing else pins. The object node's mutators are the other half: a parsed
/// document is read through it, and removing a member has to renumber the members after it.
/// </remarks>
[TestClass]
public class JsonValueTests
{
    private static string TextOf(object? value)
    {
        var node = VeloxJsonValue.From(value);
        return ((VeloxJsonScalar)node).Text!;
    }

    [TestMethod]
    public void EveryScalarShape_BecomesTheSpellingTheFormatUses()
    {
        Assert.IsTrue(VeloxJsonValue.From(null).IsNull, "null is the JSON literal, not an empty node");
        Assert.AreEqual("text", TextOf("text"));
        Assert.AreEqual("true", TextOf(true));
        Assert.AreEqual("7", TextOf(7));
        Assert.AreEqual("7", TextOf(7L));
        Assert.AreEqual("7", TextOf((byte)7));
        Assert.AreEqual("7", TextOf((short)7));
        Assert.AreEqual("1.5", TextOf(1.5d));
        Assert.AreEqual("1.5", TextOf(1.5f));
        Assert.AreEqual("1.5", TextOf(1.5m));
        Assert.AreEqual("c", TextOf('c'));
        Assert.IsTrue(TextOf(false) is "false");
    }

    [TestMethod]
    public void TheThreeShapesWithTheirOwnSpelling_KeepIt()
    {
        var id = Guid.NewGuid();
        Assert.AreEqual(id.ToString("D"), TextOf(id), "a Guid is written in the D form");

        var moment = new DateTime(2026, 10, 5, 13, 45, 0, DateTimeKind.Utc);
        Assert.AreEqual(moment.ToString("O", CultureInfo.InvariantCulture), TextOf(moment), "a DateTime is round-trippable");

        var span = TimeSpan.FromMinutes(90);
        Assert.AreEqual(span.ToString(), TextOf(span));

        Assert.AreEqual(((int)DayOfWeek.Wednesday).ToString(CultureInfo.InvariantCulture), TextOf(DayOfWeek.Wednesday), "an enum is written as its number");
    }

    [TestMethod]
    public void ANodeThatIsAlreadyANode_ComesBackAsItself()
    {
        var node = VeloxJsonValue.Parse("{\"a\": 1}");

        Assert.AreSame(node, VeloxJsonValue.From(node), "From must not re-wrap a node");
    }

    [TestMethod]
    public void BothMapShapes_BecomeObjectNodes()
    {
        // 以接口形状传进来的字典（配置袋就是这样）：IReadOnlyDictionary 不是 IDictionary。
        var readOnly = VeloxJsonValue.From(new Dictionary<string, object?> { ["a"] = 1 });
        Assert.IsInstanceOfType<VeloxJsonObject>(readOnly);
        Assert.AreEqual("1", ((VeloxJsonScalar)((VeloxJsonObject)readOnly)["a"]!).Text);

        var mutable = VeloxJsonValue.From(new System.Collections.Hashtable { ["b"] = "two" });
        Assert.AreEqual("two", ((VeloxJsonScalar)((VeloxJsonObject)mutable)["b"]!).Text);
    }

    [TestMethod]
    public void AShapeTheFormatCannotCarry_IsRefused()
    {
        // 既不是标量、也不是容器、也不可归档 —— 静默丢值比抛出去糟得多。
        Assert.ThrowsExactly<InvalidOperationException>(() => VeloxJsonValue.From(new object()));
    }

    [TestMethod]
    public void RemovingAMember_RenumbersTheMembersAfterIt()
    {
        var node = (VeloxJsonObject)VeloxJsonValue.Parse("{\"a\": 1, \"b\": 2, \"c\": 3}");

        node["b"] = null;   // 置 null 就是删

        Assert.IsFalse(node.Has("b"), "the removed member is gone");
        Assert.AreEqual("1", ((VeloxJsonScalar)node["a"]!).Text, "the member before it is untouched");
        Assert.AreEqual("3", ((VeloxJsonScalar)node["c"]!).Text, "the member after it is still findable");

        node.Add("d", 4);
        Assert.IsTrue(node.Has("d"), "an added member is findable");
        Assert.AreEqual("4", ((VeloxJsonScalar)node["d"]!).Text);
    }

    [TestMethod]
    public void ReplacingAMember_KeepsItsPosition()
    {
        var node = (VeloxJsonObject)VeloxJsonValue.Parse("{\"a\": 1, \"b\": 2}");

        node["a"] = 9;

        var order = new List<string>();
        foreach (var member in node) order.Add(member.Key);

        CollectionAssert.AreEqual(new[] { "a", "b" }, order, "replacing does not move the member");
        Assert.AreEqual("9", ((VeloxJsonScalar)node["a"]!).Text);
    }

    [TestMethod]
    public void ObjectEquality_CountsMembers_NotOrder()
    {
        var one = VeloxJsonValue.Parse("{\"a\": 1, \"b\": 2}");
        var reordered = VeloxJsonValue.Parse("{\"b\": 2, \"a\": 1}");
        var shorter = VeloxJsonValue.Parse("{\"a\": 1}");
        var different = VeloxJsonValue.Parse("{\"a\": 1, \"b\": 3}");

        Assert.IsTrue(one.DeepEquals(reordered), "order does not make them different");
        Assert.IsFalse(one.DeepEquals(shorter), "a member this one has and the other does not counts");
        Assert.IsFalse(one.DeepEquals(different), "a member with another value counts");
    }
}
