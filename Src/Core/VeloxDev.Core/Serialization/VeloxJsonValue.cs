using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VeloxDev.Serialization;

/// <summary>
/// How a JSON tree is laid out when it is turned back into text.
/// </summary>
public enum VeloxJsonFormat
{
    /// <summary>No whitespace at all — what a tool result handed to a model uses.</summary>
    Compact,

    /// <summary>One member per line, indented by two spaces — what a document meant to be read uses.</summary>
    Indented,
}

/// <summary>
/// A JSON value built in memory: a scalar, a list or a map.
/// </summary>
/// <remarks>
/// <para>
/// The tools build their results as trees and hand them over as text, so the shape of that work is "make a node,
/// put things in it, render it". This is that shape without a serializer behind it: nothing here reflects over
/// anything, which is what lets the whole tool surface stay inside the trimmed world.
/// </para>
/// <para>
/// Values are immutable once placed; the containers are not. Scalars convert implicitly from the primitives so a
/// call site reads the way it did before, and a tree renders through the same spelling rules the archive format
/// uses.
/// </para>
/// </remarks>
public abstract class VeloxJsonValue
{
    /// <summary>Renders the tree as JSON text.</summary>
    /// <param name="format">Whether to lay it out over lines.</param>
    /// <returns>The document.</returns>
    public string ToJson(VeloxJsonFormat format = VeloxJsonFormat.Compact)
    {
        var builder = new StringBuilder();
        using var writer = new StringWriter(builder);
        WriteTo(writer, format, 0);
        return builder.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => ToJson();

    /// <summary>Whether this value is the JSON <c>null</c> literal.</summary>
    public virtual bool IsNull => false;

    /// <summary>
    /// Turns the value into ordinary CLR values: a map, a list, or a scalar.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> for the JSON literal; a <see cref="string"/> for text; a <see cref="bool"/>, a
    /// <see cref="long"/> or a <see cref="double"/> for a bare literal; a
    /// <see cref="Dictionary{TKey, TValue}"/> for an object; a <see cref="List{T}"/> for an array.
    /// </returns>
    /// <remarks>
    /// For a caller that wants the payload rather than the tree — a script result, an untyped configuration
    /// value. Numbers come back as <see cref="long"/> when they are integral, which is the same widening the
    /// archive reader applies.
    /// </remarks>
    public virtual object? Materialize() => this;

    /// <summary>
    /// Whether two trees say the same thing, member for member and element for element.
    /// </summary>
    /// <param name="other">The other tree.</param>
    /// <returns><see langword="true"/> when they are equal in shape and content.</returns>
    /// <remarks>
    /// Used to tell one state snapshot from the next; the members' order is not part of the answer, because two
    /// snapshots of the same state are built in the same order anyway and a reordering is not a change.
    /// </remarks>
    public virtual bool DeepEquals(VeloxJsonValue? other) => ReferenceEquals(this, other) || Equals(other);

    /// <summary>Writes this value.</summary>
    /// <param name="writer">Where the document is written.</param>
    /// <param name="format">Whether to lay it out over lines.</param>
    /// <param name="depth">How deep the value sits, for indentation.</param>
    internal abstract void WriteTo(TextWriter writer, VeloxJsonFormat format, int depth);

    /// <summary>Writes the whitespace that precedes a nested token.</summary>
    internal static void Indent(TextWriter writer, VeloxJsonFormat format, int depth)
    {
        if (format != VeloxJsonFormat.Indented) return;

        writer.Write(Environment.NewLine);
        for (var i = 0; i < depth; i++) writer.Write("  ");
    }

    /// <summary>
    /// Turns an object into a JSON value.
    /// </summary>
    /// <param name="value">The value; <see langword="null"/> becomes the JSON literal.</param>
    /// <returns>The tree.</returns>
    /// <exception cref="InvalidOperationException">
    /// The value is neither a scalar, a container nor a type the archive serializer knows.
    /// </exception>
    /// <remarks>
    /// A tool often puts a live component in its result. Those go through the generated serializer rather than
    /// through a reflection walk, which is the whole reason a tool result can carry one at all.
    /// </remarks>
    public static VeloxJsonValue From(object? value)
    {
        switch (value)
        {
            case null: return Null;
            case VeloxJsonValue already: return already;
            case string text: return text;
            case bool flag: return flag;
            case int number: return number;
            case long number: return number;
            case double number: return number;
            case float number: return (double)number;
            case decimal number: return (double)number;
            case byte number: return (int)number;
            case short number: return (int)number;
            case char character: return character.ToString();
            case Guid id: return id.ToString("D");
            case DateTime moment: return moment.ToString("O", CultureInfo.InvariantCulture);
            case TimeSpan span: return span.ToString();
            case Enum enumeration: return Convert.ToInt64(enumeration, CultureInfo.InvariantCulture);
        }

        // 以接口形状传进来的字典（配置袋就是这样）：`IReadOnlyDictionary` 不是 `IDictionary`。
        if (value is IReadOnlyDictionary<string, object?> readOnlyMap)
        {
            var node = new VeloxJsonObject();
            foreach (var member in readOnlyMap) node[member.Key] = From(member.Value);
            return node;
        }

        if (value is IDictionary map)
        {
            var node = new VeloxJsonObject();
            foreach (var raw in map)
            {
                if (raw is not DictionaryEntry entry) continue;
                node[entry.Key?.ToString() ?? string.Empty] = From(entry.Value);
            }

            return node;
        }

        if (value is IEnumerable sequence)
        {
            var node = new VeloxJsonArray();
            foreach (var item in sequence) node.Add(From(item));
            return node;
        }

        return Parse(VeloxJsonSerializer.Serialize(value));
    }

    /// <summary>Parses JSON text into a tree.</summary>
    /// <param name="json">The document.</param>
    /// <returns>The tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The text is not valid JSON.</exception>
    public static VeloxJsonValue Parse(string json)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));

        return ReadNode(new VeloxJsonReader(json));
    }

    /// <summary>The JSON <c>null</c> literal.</summary>
    public static VeloxJsonValue Null { get; } = new VeloxJsonScalar(null);

    /// <summary>Reads one value of any shape from a reader positioned at it.</summary>
    private static VeloxJsonValue ReadNode(VeloxJsonReader reader)
    {
        if (reader.NextIsNull()) { reader.SkipValue(); return Null; }

        if (reader.NextIsArray())
        {
            var node = new VeloxJsonArray();
            reader.BeginArray();
            while (reader.NextElement()) node.Add(ReadNode(reader));
            reader.FinishArray();

            return node;
        }

        if (reader.NextIsObject())
        {
            var node = new VeloxJsonObject();

            reader.BeginObject(out _, out _);
            while (reader.NextMember(out var name))
            {
                node[name] = ReadNode(reader);
            }
            reader.FinishObject();

            return node;
        }

        // 有引号是字符串，没有就是字面量 —— 这一条不能靠内容猜：数字形状的字符串必须仍然带引号。
        return reader.NextIsString()
            ? new VeloxJsonScalar(reader.ReadString(), VeloxJsonScalarKind.Text)
            : new VeloxJsonScalar(reader.ReadText(), VeloxJsonScalarKind.Literal);
    }

    /// <summary>Converts text to a JSON value without going through a parse.</summary>
    public static implicit operator VeloxJsonValue(string? value)
        => value is null ? Null : new VeloxJsonScalar(value, VeloxJsonScalarKind.Text);
    /// <summary>Converts a boolean to a JSON value.</summary>
    public static implicit operator VeloxJsonValue(bool value)
        => new VeloxJsonScalar(value ? "true" : "false", VeloxJsonScalarKind.Literal);
    /// <summary>Converts an integer to a JSON value.</summary>
    public static implicit operator VeloxJsonValue(int value)
        => new VeloxJsonScalar(value.ToString(CultureInfo.InvariantCulture), VeloxJsonScalarKind.Literal);
    /// <summary>Converts a long integer to a JSON value.</summary>
    public static implicit operator VeloxJsonValue(long value)
        => new VeloxJsonScalar(value.ToString(CultureInfo.InvariantCulture), VeloxJsonScalarKind.Literal);
    /// <summary>Converts a double to a JSON value.</summary>
    public static implicit operator VeloxJsonValue(double value)
        => new VeloxJsonScalar(VeloxJsonText.Double(value), VeloxJsonScalarKind.Literal);
}

/// <summary>What a scalar's text means.</summary>
internal enum VeloxJsonScalarKind
{
    /// <summary>The JSON <c>null</c> literal.</summary>
    Null,

    /// <summary>A string: always quoted and escaped.</summary>
    Text,

    /// <summary>A number or a boolean: written as it stands.</summary>
    Literal,
}

/// <summary>
/// A JSON scalar: a string, a number, a boolean or <c>null</c>, kept as the text that will be written.
/// </summary>
public sealed class VeloxJsonScalar : VeloxJsonValue
{
    private readonly string? _text;
    private readonly VeloxJsonScalarKind _kind;

    internal VeloxJsonScalar(string? text, VeloxJsonScalarKind kind = VeloxJsonScalarKind.Text)
    {
        _text = text;
        _kind = text is null ? VeloxJsonScalarKind.Null : kind;
    }

    /// <inheritdoc />
    public override bool IsNull => _kind == VeloxJsonScalarKind.Null;

    /// <summary>
    /// The text as it will be written, or <see langword="null"/> for the JSON literal.
    /// </summary>
    /// <remarks>
    /// A scalar read from a document comes back as this text rather than as a typed value, which is what the
    /// tools want: they hand the text on, or read it with <see cref="AsInt32"/>/<see cref="AsBoolean"/>.
    /// </remarks>
    public string? Text => _text;

    /// <summary>This scalar's text, or <see langword="null"/>.</summary>
    /// <returns>The text.</returns>
    public string? AsString() => _text;

    /// <summary>This scalar as a 32-bit integer.</summary>
    /// <returns>The number, or <c>0</c> when it is not one.</returns>
    public int AsInt32() => int.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>This scalar as a boolean.</summary>
    /// <returns>The value, or <see langword="false"/> when it is not one.</returns>
    public bool AsBoolean() => _text == "true";

    /// <summary>Whether this scalar is a bare number rather than text.</summary>
    /// <remarks>
    /// "Bare" is the whole answer: a numeric <i>string</i> came from a document with quotes around it and stays
    /// text, which is why the kind is recorded rather than guessed from the contents.
    /// </remarks>
    public bool IsNumber => _kind == VeloxJsonScalarKind.Literal && double.TryParse(
        _text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>Whether this scalar is text rather than a bare literal.</summary>
    public bool IsText => _kind == VeloxJsonScalarKind.Text;

    /// <inheritdoc />
    public override bool DeepEquals(VeloxJsonValue? other)
        => other is VeloxJsonScalar scalar && _kind == scalar._kind && string.Equals(_text, scalar._text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override object? Materialize()
    {
        if (_kind == VeloxJsonScalarKind.Null) return null;
        if (_kind == VeloxJsonScalarKind.Text) return _text;

        if (_text == "true") return true;
        if (_text == "false") return false;
        if (long.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;

        return double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : _text;
    }

    internal override void WriteTo(TextWriter writer, VeloxJsonFormat format, int depth)
    {
        if (_kind == VeloxJsonScalarKind.Null) { writer.Write("null"); return; }

        if (_kind == VeloxJsonScalarKind.Literal) { writer.Write(_text); return; }

        writer.Write('"');
        VeloxJsonText.Escape(writer, _text!);
        writer.Write('"');
    }
}

/// <summary>A JSON array, built in order.</summary>
public sealed class VeloxJsonArray : VeloxJsonValue, IEnumerable<VeloxJsonValue>
{
    private readonly List<VeloxJsonValue> _items = [];

    /// <summary>How many elements the array holds.</summary>
    public int Count => _items.Count;

    /// <summary>The element at <paramref name="index"/>.</summary>
    /// <param name="index">The position.</param>
    /// <returns>The element.</returns>
    public VeloxJsonValue this[int index] => _items[index];

    /// <summary>Appends an element.</summary>
    /// <param name="value">The element; <see langword="null"/> becomes the JSON literal.</param>
    public void Add(VeloxJsonValue value) => _items.Add(value ?? Null);

    /// <summary>Appends an element built from an object.</summary>
    /// <param name="value">The value.</param>
    public void Add(object? value) => _items.Add(From(value));

    /// <summary>Removes every element.</summary>
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public IEnumerator<VeloxJsonValue> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    public override object? Materialize()
    {
        var items = new List<object?>(_items.Count);
        foreach (var item in _items) items.Add(item.Materialize());
        return items;
    }

    /// <inheritdoc />
    public override bool DeepEquals(VeloxJsonValue? other)
        => other is VeloxJsonArray array
           && array._items.Count == _items.Count
           && !_items.Where((item, i) => !item.DeepEquals(array._items[i])).Any();

    internal override void WriteTo(TextWriter writer, VeloxJsonFormat format, int depth)
    {
        if (_items.Count == 0) { writer.Write("[]"); return; }

        writer.Write('[');
        for (var i = 0; i < _items.Count; i++)
        {
            if (i > 0) writer.Write(',');
            Indent(writer, format, depth + 1);
            _items[i].WriteTo(writer, format, depth + 1);
        }
        Indent(writer, format, depth);
        writer.Write(']');
    }
}

/// <summary>A JSON object, keeping its members in the order they were added.</summary>
public sealed class VeloxJsonObject : VeloxJsonValue, IEnumerable<KeyValuePair<string, VeloxJsonValue>>
{
    private readonly List<KeyValuePair<string, VeloxJsonValue>> _members = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    /// <summary>How many members the object holds.</summary>
    public int Count => _members.Count;

    /// <summary>The names of the members, in order.</summary>
    public IEnumerable<string> Names => _index.Keys;

    /// <summary>
    /// Gets or sets a member. Setting one that is already there replaces its value in place; a
    /// <see langword="null"/> value removes it.
    /// </summary>
    /// <param name="name">The member's name.</param>
    /// <returns>The member's value, or <see langword="null"/> when there is none.</returns>
    public VeloxJsonValue? this[string name]
    {
        get => _index.TryGetValue(name, out var at) ? _members[at].Value : null;
        set
        {
            if (value is null)
            {
                if (_index.TryGetValue(name, out var removeAt))
                {
                    _members.RemoveAt(removeAt);
                    _index.Remove(name);
                    Reindex(removeAt);
                }
                return;
            }

            if (_index.TryGetValue(name, out var at))
            {
                _members[at] = new KeyValuePair<string, VeloxJsonValue>(name, value);
                return;
            }

            _index[name] = _members.Count;
            _members.Add(new KeyValuePair<string, VeloxJsonValue>(name, value));
        }
    }

    /// <summary>Whether the object has a member of that name.</summary>
    /// <param name="name">The member's name.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Has(string name) => _index.ContainsKey(name);

    /// <summary>Adds a member.</summary>
    /// <param name="name">The member's name.</param>
    /// <param name="value">The value.</param>
    public void Add(string name, VeloxJsonValue value) => this[name] = value;

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, VeloxJsonValue>> GetEnumerator() => _members.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _members.GetEnumerator();

    private void Reindex(int from)
    {
        // 只重编 `from` 起的那些：删掉一格，它之后的成员下标各上移一位，而它**之前**的没动。
        // 清空重来会把前面那些从索引里抹掉 —— 症状是「删掉中间一个成员，它前面的成员全部找不回来」
        // （`Has` 与索引器都走这张表，实测）。
        for (var i = from; i < _members.Count; i++) _index[_members[i].Key] = i;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A member the other object does not have makes them different; a member the other has and this one does not
    /// does too. Order does not.
    /// </remarks>
    public override bool DeepEquals(VeloxJsonValue? other)
    {
        if (other is not VeloxJsonObject obj || obj._members.Count != _members.Count) return false;

        foreach (var member in _members)
        {
            var theirs = obj[member.Key];
            if (theirs is null || !member.Value.DeepEquals(theirs)) return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override object? Materialize()
    {
        var map = new Dictionary<string, object?>(_members.Count, StringComparer.Ordinal);
        foreach (var member in _members) map[member.Key] = member.Value.Materialize();
        return map;
    }

    /// <summary>Turns this object into an ordinary map — the shape most callers actually want.</summary>
    /// <returns>The members' materialized values, keyed by name.</returns>
    public Dictionary<string, object?> ToDictionary()
        => (Dictionary<string, object?>)Materialize()!;

    internal override void WriteTo(TextWriter writer, VeloxJsonFormat format, int depth)
    {
        if (_members.Count == 0) { writer.Write("{}"); return; }

        writer.Write('{');
        for (var i = 0; i < _members.Count; i++)
        {
            if (i > 0) writer.Write(',');
            Indent(writer, format, depth + 1);

            writer.Write('"');
            VeloxJsonText.Escape(writer, _members[i].Key);
            writer.Write('"');
            writer.Write(':');
            if (format == VeloxJsonFormat.Indented) writer.Write(' ');

            _members[i].Value.WriteTo(writer, format, depth + 1);
        }
        Indent(writer, format, depth);
        writer.Write('}');
    }
}
