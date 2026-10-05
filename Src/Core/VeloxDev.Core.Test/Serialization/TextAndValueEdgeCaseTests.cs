using System;
using System.Collections;
using System.Collections.Generic;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Test.Serialization;

/// <summary>
/// The spelling table, the JSON tree's comparison arms, and the options builder — the pieces both the archive
/// writer and the tool-facing tree draw their answers from.
/// </summary>
[TestClass]
public class TextAndValueEdgeCaseTests
{
    // ── 拼法表 ───────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheEscapeTable_AnswersForEveryCharacter()
    {
        // C0 里没有具名写法的那些按 `\uXXXX` 写。
        Assert.AreEqual("\\u0001", VeloxJsonText.EscapeSequence('\u0001'));

        // 不需要转义的字符没有写法 —— 这条是「整串一次写出」快路径的前提。
        Assert.IsNull(VeloxJsonText.EscapeSequence('A'));
        Assert.IsNull(VeloxJsonText.EscapeSequence('中'));
    }

    [TestMethod]
    public void TheNumberSpellings_IncludeTheNonFiniteOnes()
    {
        Assert.AreEqual("\"NaN\"", VeloxJsonText.Double(double.NaN));
        Assert.AreEqual("\"Infinity\"", VeloxJsonText.Double(double.PositiveInfinity));
        Assert.AreEqual("\"-Infinity\"", VeloxJsonText.Double(double.NegativeInfinity));

        Assert.AreEqual("1.5", VeloxJsonText.Double(1.5));
        Assert.AreEqual("2.0", VeloxJsonText.Double(2), "an integral value carries its .0");
    }

    // ── JSON 树 ──────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AnObjectNode_ComparesByItsMembers()
    {
        var one = VeloxJsonValue.Parse("{\"a\": 1}");

        Assert.IsFalse(one.DeepEquals(VeloxJsonValue.Parse("[1]")), "an array is not an object");
        Assert.IsFalse(
            one.DeepEquals(VeloxJsonValue.Parse("{\"b\": 1}")),
            "a member of the same count but another name is not equal");
        Assert.IsTrue(one.DeepEquals(VeloxJsonValue.Parse("{\"a\": 1}")));
    }

    [TestMethod]
    public void AScalarThatIsNotANumber_AnswersZero()
        => Assert.AreEqual(0, ((VeloxJsonScalar)VeloxJsonValue.Parse("\"x\"")).AsInt32());

    [TestMethod]
    public void AMaterializedLiteral_KeepsItsTextWhenNoNumberFits()
    {
        // 裸 token 既不是布尔也不是整数，也不是能读的浮点 —— 交回它自己的文本。
        Assert.AreEqual("abc", VeloxJsonValue.Parse("abc").Materialize());
    }

    [TestMethod]
    public void AnArrayNode_RefusesNoElement()
    {
        var array = (VeloxJsonArray)VeloxJsonValue.Parse("[]");
        VeloxJsonValue? nothing = null;

        array.Add(nothing!);

        Assert.AreEqual(1, array.Count, "a null node becomes the JSON literal rather than nothing");
        Assert.IsTrue(array[0].IsNull);
    }

    [TestMethod]
    public void AnUnusualDictionary_StillBecomesAnObject()
    {
        // 枚举器交出不是 DictionaryEntry 的东西，键也可以是 null、或者 ToString 返回 null ——
        // 三种都会落到 From 的字典分支上。
        var loose = new RawHashtable();
        loose.Raw.Add("not an entry");
        loose.Raw.Add(new DictionaryEntry(null, 1));
        loose.Raw.Add(new DictionaryEntry(new NamelessKey(), 2));

        var node = (VeloxJsonObject)VeloxJsonValue.From(loose);

        Assert.AreEqual(1, node.Count, "the non-entry was skipped and both unnamed keys landed on the same name");
        Assert.AreEqual(2L, node[string.Empty]!.Materialize(), "the last one to be placed wins");
    }

    // ── 选项 ─────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheExclusionList_IsDroppedWhenItWouldBeEmpty()
    {
        var options = SerializationOptions.Create();

        Assert.IsNull(options.ExcludedPropertyTypes, "nothing is excluded by default");
        Assert.IsNull(options.WithExcludedPropertyTypes().ExcludedPropertyTypes, "an empty list means no exclusions");
        Assert.AreEqual(1, options.WithExcludedPropertyTypes(typeof(int)).ExcludedPropertyTypes!.Length);
    }

    // ── 测试替身 ─────────────────────────────────────────────────────────────────────────────────────

    private sealed class NamelessKey
    {
        public override string? ToString() => null;
    }

    private sealed class RawHashtable : Hashtable
    {
        internal readonly List<object> Raw = [];

        public override IDictionaryEnumerator GetEnumerator() => new RawEnumerator(Raw);

        private sealed class RawEnumerator(List<object> items) : IDictionaryEnumerator
        {
            private int _index = -1;

            public object? Current => items[_index];

            public DictionaryEntry Entry => (DictionaryEntry)items[_index]!;

            public object Key => Entry.Key!;

            public object Value => Entry.Value!;

            public bool MoveNext() => ++_index < items.Count;

            public void Reset() => _index = -1;
        }
    }
}
