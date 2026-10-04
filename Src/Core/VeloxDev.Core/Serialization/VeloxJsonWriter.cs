using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VeloxDev.Serialization;

/// <summary>
/// Writes an object graph as the archive JSON the workflow documents use.
/// </summary>
/// <remarks>
/// <para>
/// This is not a general-purpose JSON writer. It reproduces, byte for byte, the output the framework produced
/// before this module existed — the same indentation, the same member separators, the same number spellings,
/// and the same <c>$id</c>/<c>$type</c>/<c>$ref</c> bookkeeping. The frozen documents in the test suite are the
/// contract; <c>SerializationGoldenTests</c> holds this writer to them.
/// </para>
/// <para>
/// Generated code drives it. A writer for one type starts an object, writes its members in the order the
/// contract reports them, and ends the object; the primitives here decide everything about shape.
/// </para>
/// <para>
/// The writer is single-use and not thread-safe: one instance owns one output and one reference table.
/// </para>
/// </remarks>
public sealed class VeloxJsonWriter
{
    private readonly TextWriter _writer;
    private readonly Dictionary<object, int> _references = new(ReferenceComparer.Instance);
    private int _nextReferenceId = 1;
    private readonly bool _indented;
    private int _depth;
    private bool _hasMember;

    // 缩进按层预生成到 64 层。原先的写法是每行每层写一次 "  "，一层一次虚调用；现在每行固定两次写
    // （换行、缩进），与深度无关。超过 64 层就退回循环 —— 极深的文档要的是正确，不是快。
    private const int CachedIndentDepth = 64;
    private static readonly string[] Indents = BuildIndents();

    private static string[] BuildIndents()
    {
        var indents = new string[CachedIndentDepth + 1];
        indents[0] = string.Empty;
        for (var i = 1; i <= CachedIndentDepth; i++) indents[i] = new string(' ', i * 2);
        return indents;
    }

    /// <summary>
    /// Creates a writer over <paramref name="writer"/>.
    /// </summary>
    /// <param name="writer">Where the document is written.</param>
    /// <param name="indented">Whether to lay the document out over lines, as the archive format does.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public VeloxJsonWriter(TextWriter writer, bool indented = true)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _indented = indented;
    }

    /// <summary>How many references have been handed out so far; the next one gets this id.</summary>
    internal int ReferenceCount => _nextReferenceId - 1;

    /// <summary>
    /// The id of an already-written object, or a fresh one when it has not been written yet.
    /// </summary>
    /// <remarks>
    /// An interface-keyed map is written with its keys' ids as property names, and a key that has not been
    /// written yet still needs one — the id is allocated here, on the spot, which is what makes the numbering in
    /// an existing document reproducible.
    /// </remarks>
    /// <param name="instance">The key object.</param>
    /// <returns>Its id.</returns>
    public int GetOrAddReference(object instance)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));

        if (_references.TryGetValue(instance, out var existing)) return existing;

        _references[instance] = _nextReferenceId;
        return _nextReferenceId++;
    }

    /// <summary>
    /// Starts writing an object, or writes a reference to one already written.
    /// </summary>
    /// <param name="instance">The object.</param>
    /// <param name="declaredType">
    /// The member's declared type, or <see langword="null"/> for a value whose own type is the whole story —
    /// a type name is written only when the instance's runtime type differs from this one.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the caller must write the members and then call <see cref="WriteEndObject"/>;
    /// <see langword="false"/> when the value was already written and a reference was emitted instead, in which
    /// case there is nothing more to write.
    /// </returns>
    public bool WriteStartObject(object instance, Type? declaredType)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));

        if (_references.TryGetValue(instance, out var existing))
        {
            WriteReference(existing);
            return false;
        }

        _references[instance] = _nextReferenceId++;
        StartContainer('{');
        WriteMemberName("$id");
        WriteString(_references[instance].ToString(CultureInfo.InvariantCulture));

        var runtimeType = instance.GetType();
        if (declaredType is not null && declaredType != runtimeType)
        {
            WriteMemberName("$type");
            WriteString(VeloxJsonRegistry.NameOf(runtimeType) ?? runtimeType.FullName ?? runtimeType.Name);
        }

        return true;
    }

    /// <summary>
    /// Starts an object that is not itself reference-tracked.
    /// </summary>
    /// <remarks>
    /// An interface-keyed map is written this way: the map's own identity never appears in a document — only its
    /// keys' ids do — so it gets no <c>$id</c> and no type name.
    /// </remarks>
    public void WriteStartObjectWithoutReference() => StartContainer('{');

    /// <summary>Ends an object started by <see cref="WriteStartObject"/> or <see cref="WriteStartObjectWithoutReference"/>.</summary>
    public void WriteEndObject() => EndContainer('}');

    /// <summary>Starts an array.</summary>
    public void WriteStartArray() => StartContainer('[');

    /// <summary>
    /// Moves to the next array element. Call it once before writing each element, and not at all for an empty
    /// array — that is what keeps <c>[]</c> empty and the separators between elements.
    /// </summary>
    public void WriteNextElement() => Separate();

    /// <summary>Ends an array.</summary>
    public void WriteEndArray() => EndContainer(']');

    /// <summary>Writes a property name. The value follows.</summary>
    /// <param name="name">The member's name, exactly as the contract reports it.</param>
    public void WriteMemberName(string name)
    {
        Separate();
        WriteRaw('"');
        WriteEscaped(name);
        WriteRaw('"');
        WriteRaw(':');
        if (_indented) WriteRaw(' ');
    }

    /// <summary>Writes a string value.</summary>
    /// <param name="value">The value; <see langword="null"/> is written as the JSON literal.</param>
    public void WriteString(string? value)
    {
        if (value is null)
        {
            WriteRaw("null");
            return;
        }

        WriteRaw('"');
        WriteEscaped(value);
        WriteRaw('"');
    }

    /// <summary>Writes the JSON <c>null</c> literal.</summary>
    public void WriteNull() => WriteRaw("null");

    /// <summary>Writes a boolean.</summary>
    public void WriteBoolean(bool value) => WriteRaw(value ? "true" : "false");

    /// <summary>Writes a 32-bit integer.</summary>
    public void WriteInt32(int value) => WriteRaw(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Writes a 64-bit integer.</summary>
    public void WriteInt64(long value) => WriteRaw(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Writes a double the way the archive format spells them: shortest round-trippable, an integral value
    /// carrying a trailing <c>.0</c>, and the non-finite values as strings.
    /// </summary>
    public void WriteDouble(double value)
    {
        if (double.IsNaN(value)) { WriteString("NaN"); return; }
        if (double.IsPositiveInfinity(value)) { WriteString("Infinity"); return; }
        if (double.IsNegativeInfinity(value)) { WriteString("-Infinity"); return; }

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0) text += ".0";
        WriteRaw(text);
    }

    /// <summary>Writes a single-precision value with the same spelling rules as a double.</summary>
    public void WriteSingle(float value) => WriteDouble(value);

    /// <summary>Writes a decimal.</summary>
    public void WriteDecimal(decimal value) => WriteRaw(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Writes a globally unique identifier in its hyphenated, lower-case form.</summary>
    public void WriteGuid(Guid value) => WriteString(value.ToString("D"));

    /// <summary>Writes a value that already carries its own JSON spelling.</summary>
    /// <param name="json">A complete JSON token — used for the few scalars with no dedicated method.</param>
    public void WriteRawValue(string json) => WriteRaw(json);

    // ── Shape ────────────────────────────────────────────────────────────────────────────────────────

    private void StartContainer(char open)
    {
        WriteRaw(open);
        _depth++;
        _hasMember = false;
    }

    private void EndContainer(char close)
    {
        var wasEmpty = !_hasMember;
        _depth--;
        // 空容器写成 `{}`/`[]`：契约就是这么写空 LinksMap 与空集合的。
        if (!wasEmpty) NewLine();
        WriteRaw(close);
        _hasMember = true;
    }

    /// <summary>
    /// Emits whatever must precede the next token in a container.
    /// </summary>
    /// <remarks>
    /// Separation belongs to the container, not to the value: a value method is written by generated code that
    /// has no idea whether it is the first entry, and a separator emitted there would either double up or land
    /// between a member's name and its value.
    /// </remarks>
    private void Separate()
    {
        if (_hasMember)
        {
            WriteRaw(',');
            NewLine();
        }
        else if (_depth > 0)
        {
            NewLine();
        }

        _hasMember = true;
    }

    private void NewLine()
    {
        if (!_indented) return;

        // 必须是 Environment.NewLine，不能图快写成 "\n"：黄金文件在索引里是 LF、工作区里是 CRLF，
        // 精确比较的那道闸（SerializationGoldenTests）会当场发现。见 memory/modules/Serialization。
        _writer.Write(Environment.NewLine);

        if (_depth < Indents.Length) _writer.Write(Indents[_depth]);
        else for (var i = 0; i < _depth; i++) _writer.Write("  ");
    }

    /// <summary>
    /// Writes the <c>{"$ref": "n"}</c> form.
    /// </summary>
    /// <remarks>
    /// Built as a real object rather than a pasted literal so it is laid out like every other object — the
    /// archive format indents it, and a one-line literal would show up as a difference on every repeated
    /// reference.
    /// </remarks>
    private void WriteReference(int id)
    {
        StartContainer('{');
        WriteMemberName("$ref");
        WriteString(id.ToString(CultureInfo.InvariantCulture));
        EndContainer('}');
        _hasMember = true;
    }

    private void WriteRaw(char value) => _writer.Write(value);
    private void WriteRaw(string value) => _writer.Write(value);

    // 转义规则只有一份，在 VeloxJsonText —— JSON 树那条路走的是同一个函数。之前这里与它各有一份拷贝，
    // 也就是说「文档怎么拼」有两个地方可以开始分叉。
    private void WriteEscaped(string value) => VeloxJsonText.Escape(_writer, value);

    /// <summary>Compares by identity: the reference table tracks the object, not its equality.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
