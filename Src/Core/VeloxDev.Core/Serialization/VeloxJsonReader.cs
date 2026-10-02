using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VeloxDev.Serialization;

/// <summary>
/// Reads the archive JSON back into an object graph.
/// </summary>
/// <remarks>
/// <para>
/// A cursor over one document, with the object protocol the generated readers use: begin an object, take its
/// members by name until it ends, and let <see cref="RegisterReference"/> make a repeated value resolvable while
/// it is still being read. The reference table is what makes <c>$ref</c> work and what keeps a cycle from
/// running away.
/// </para>
/// <para>
/// Order-independent on purpose: members are dispatched by name, so a document whose properties are in a
/// different order still reads — the archive format's ordering is the writer's business, not the reader's.
/// </para>
/// <para>
/// One instance reads one document and is not thread-safe.
/// </para>
/// </remarks>
public sealed class VeloxJsonReader
{
    private readonly string _text;
    private readonly Dictionary<int, object> _references = [];
    private int _position;

    // 每个嵌套层各有一份「已经读过几项」的计数：用一个字段的话，内层读完会把外层的逗号状态清掉，
    // 外层就会在逗号上找引号。
    private int[] _counts = new int[16];
    private int _depth;

    /// <summary>
    /// Creates a reader over <paramref name="json"/>.
    /// </summary>
    /// <param name="json">The document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="json"/> is empty.</exception>
    public VeloxJsonReader(string json)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));
        if (json.Length == 0) throw new ArgumentException("The document is empty.", nameof(json));

        _text = json;
    }

    /// <summary>Whether the whole document has been consumed.</summary>
    public bool AtEnd
    {
        get
        {
            SkipWhitespace();
            return _position >= _text.Length;
        }
    }

    // ── Objects ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts an object, taking its <c>$id</c> and <c>$type</c> when the document carries them.
    /// </summary>
    /// <param name="referenceId">The object's id, or <c>0</c> when it has none.</param>
    /// <param name="typeName">The written type name, or <see langword="null"/> when it has none.</param>
    /// <returns>
    /// <see langword="true"/> when the caller must read the members and then call <see cref="FinishObject"/>;
    /// <see langword="false"/> when the document held a reference to an object already read, in which case the
    /// object is reachable through <see cref="ResolveReference"/> and the cursor has moved past it.
    /// </returns>
    public bool BeginObject(out int referenceId, out string? typeName)
    {
        Expect('{');
        SkipWhitespace();

        // `$ref` 是一个只有一项的对象 —— 读到它就没有成员可读了。
        if (TryReadReference(out referenceId))
        {
            typeName = null;
            return false;
        }

        referenceId = 0;
        typeName = null;
        EnterLevel();

        if (TryReadMetadataMember("$id", out var idText))
            referenceId = int.Parse(idText!, CultureInfo.InvariantCulture);

        if (TryReadMetadataMember("$type", out var writtenType))
            typeName = writtenType;

        return true;
    }

    /// <summary>
    /// Reads the next member's name.
    /// </summary>
    /// <param name="name">The member's name.</param>
    /// <returns><see langword="false"/> when the object has no more members.</returns>
    public bool NextMember(out string name)
    {
        SkipWhitespace();
        if (Peek() == '}') { name = string.Empty; return false; }

        if (Count() > 0) Expect(',');
        Count(Count() + 1);

        name = ReadQuoted();
        SkipWhitespace();
        Expect(':');
        return true;
    }

    /// <summary>Consumes an object's closing brace.</summary>
    public void FinishObject()
    {
        LeaveLevel();
        Expect('}');
    }

    // ── Arrays ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Starts an array.</summary>
    public void BeginArray()
    {
        Expect('[');
        EnterLevel();
    }

    /// <summary>Moves to the next element.</summary>
    /// <returns><see langword="false"/> when the array has no more elements.</returns>
    public bool NextElement()
    {
        SkipWhitespace();
        if (Peek() == ']') return false;

        if (Count() > 0) Expect(',');
        Count(Count() + 1);
        return true;
    }

    /// <summary>Consumes an array's closing bracket.</summary>
    public void FinishArray()
    {
        LeaveLevel();
        Expect(']');
    }

    // ── References ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Makes an object resolvable by id before its members have been read.</summary>
    /// <param name="referenceId">The id from <see cref="BeginObject"/>; <c>0</c> does nothing.</param>
    /// <param name="instance">The instance being filled.</param>
    public void RegisterReference(int referenceId, object instance)
    {
        if (referenceId <= 0) return;

        _references[referenceId] = instance;
    }

    /// <summary>The object an id stands for, or <see langword="null"/> when nothing registered it.</summary>
    /// <param name="referenceId">The id.</param>
    /// <returns>The instance, or <see langword="null"/>.</returns>
    public object? ResolveReference(int referenceId)
        => _references.TryGetValue(referenceId, out var instance) ? instance : null;

    // ── Scalars ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Whether the next token opens an object.</summary>
    public bool NextIsObject()
    {
        SkipWhitespace();
        return Peek() == '{';
    }

    /// <summary>Whether the next token is the JSON <c>null</c> literal.</summary>
    public bool NextIsNull()
    {
        SkipWhitespace();
        return _text.Length - _position >= 4 && string.CompareOrdinal(_text, _position, "null", 0, 4) == 0;
    }

    /// <summary>Reads a string; a JSON <c>null</c> reads as <see langword="null"/>.</summary>
    /// <returns>The value.</returns>
    public string? ReadString()
    {
        if (NextIsNull()) { _position += 4; return null; }
        return ReadQuoted();
    }

    /// <summary>Reads a value's text form, whether the document wrote it as a string or bare.</summary>
    /// <returns>The token's text.</returns>
    public string ReadText()
    {
        SkipWhitespace();
        return Peek() == '"' ? ReadQuoted() : ReadBareToken();
    }

    /// <summary>Reads a boolean.</summary>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The token is neither <c>true</c> nor <c>false</c>.</exception>
    public bool ReadBoolean()
    {
        if (ReadLiteral("true")) return true;
        if (ReadLiteral("false")) return false;

        throw Malformed("expected a boolean");
    }

    /// <summary>Reads a 32-bit integer.</summary>
    /// <returns>The value.</returns>
    public int ReadInt32() => int.Parse(ReadText(), CultureInfo.InvariantCulture);

    /// <summary>Reads a 64-bit integer.</summary>
    /// <returns>The value.</returns>
    public long ReadInt64() => long.Parse(ReadText(), CultureInfo.InvariantCulture);

    /// <summary>Reads a double, including the string spellings the format uses for non-finite values.</summary>
    /// <returns>The value.</returns>
    public double ReadDouble()
        => ReadText() switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            var text => double.Parse(text, CultureInfo.InvariantCulture),
        };

    /// <summary>Reads a single-precision value.</summary>
    /// <returns>The value.</returns>
    public float ReadSingle() => (float)ReadDouble();

    /// <summary>Reads a decimal.</summary>
    /// <returns>The value.</returns>
    public decimal ReadDecimal() => decimal.Parse(ReadText(), CultureInfo.InvariantCulture);

    /// <summary>Reads a globally unique identifier from its hyphenated text form.</summary>
    /// <returns>The value.</returns>
    public Guid ReadGuid() => Guid.Parse(ReadText());

    /// <summary>
    /// Skips one value of any shape — a scalar, an object or an array, at any depth.
    /// </summary>
    /// <remarks>
    /// Used for a member this reader has no home for, so an older build reading a newer document steps over what
    /// it does not understand instead of failing.
    /// </remarks>
    public void SkipValue()
    {
        SkipWhitespace();

        switch (Peek())
        {
            case '{':
                BeginObject(out _, out _);
                while (NextMember(out _)) SkipValue();
                FinishObject();
                return;

            case '[':
                BeginArray();
                while (NextElement()) SkipValue();
                FinishArray();
                return;

            case '"':
                ReadQuoted();
                return;

            default:
                ReadBareToken();
                return;
        }
    }

    // ── Tokenizer ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Reads a bare token — a number or one of the keyword literals — and returns its text.</summary>
    private string ReadBareToken()
    {
        var start = _position;
        while (_position < _text.Length)
        {
            var c = _text[_position];
            if (char.IsWhiteSpace(c) || c is ',' or '}' or ']') break;
            _position++;
        }

        if (_position == start) throw Malformed("expected a value");
        return _text.Substring(start, _position - start);
    }

    private bool ReadLiteral(string literal)
    {
        SkipWhitespace();
        if (_text.Length - _position < literal.Length) return false;
        if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0) return false;

        _position += literal.Length;
        return true;
    }

    /// <summary>Reads the <c>{"$ref":"n"}</c> form, leaving the cursor untouched when it is not that.</summary>
    private bool TryReadReference(out int referenceId)
    {
        var checkpoint = _position;

        if (TryReadMetadataMember("$ref", out var text)
            && PeekAfterWhitespace() == '}')
        {
            _position++;
            referenceId = int.Parse(text!, CultureInfo.InvariantCulture);
            // 这里没有进过层 —— BeginObject 是在探测之后才 EnterLevel 的，所以也不能在这里退层。
            return true;
        }

        _position = checkpoint;
        referenceId = 0;
        return false;
    }

    /// <summary>
    /// Reads <c>"name": "text"</c> when the object's next member is exactly that, leaving the cursor untouched
    /// otherwise. The separating comma, if any, is left for the next member to consume.
    /// </summary>
    private bool TryReadMetadataMember(string name, out string? text)
    {
        text = null;

        var checkpoint = _position;
        SkipWhitespace();

        // 前面已经读过一项时会有一个分隔逗号挡在这里；读不到就整体回退。
        if (Count() > 0 && Peek() == ',')
        {
            _position++;
            SkipWhitespace();
        }

        if (Peek() != '"')
        {
            _position = checkpoint;
            return false;
        }

        var candidate = ReadQuoted();
        if (!string.Equals(candidate, name, StringComparison.Ordinal))
        {
            _position = checkpoint;
            return false;
        }

        SkipWhitespace();
        Expect(':');
        SkipWhitespace();

        text = Peek() == '"' ? ReadQuoted() : ReadBareToken();
        Count(Count() + 1);
        return true;
    }

    private string ReadQuoted()
    {
        Expect('"');

        var builder = new StringBuilder();
        while (true)
        {
            if (_position >= _text.Length) throw Malformed("unterminated string");

            var c = _text[_position++];
            if (c == '"') return builder.ToString();

            if (c != '\\') { builder.Append(c); continue; }

            if (_position >= _text.Length) throw Malformed("unterminated escape");
            var escape = _text[_position++];
            switch (escape)
            {
                case '"': builder.Append('"'); break;
                case '\\': builder.Append('\\'); break;
                case '/': builder.Append('/'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'u':
                    if (_position + 4 > _text.Length) throw Malformed("truncated escape");
                    builder.Append((char)int.Parse(
                        _text.Substring(_position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    _position += 4;
                    break;
                default: throw Malformed($"unknown escape '\\{escape}'");
            }
        }
    }

    private char Peek()
    {
        if (_position >= _text.Length) throw Malformed("unexpected end of document");
        return _text[_position];
    }

    private char PeekAfterWhitespace()
    {
        SkipWhitespace();
        return Peek();
    }

    private void Expect(char expected)
    {
        SkipWhitespace();
        if (Peek() != expected) throw Malformed($"expected '{expected}'");
        _position++;
    }

    private void SkipWhitespace()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
    }

    /// <summary>Enters a container, giving it its own item count.</summary>
    private void EnterLevel()
    {
        if (_depth == _counts.Length) System.Array.Resize(ref _counts, _depth * 2);
        _counts[_depth++] = 0;
    }

    /// <summary>Leaves a container.</summary>
    private void LeaveLevel()
    {
        if (_depth > 0) _depth--;
    }

    private int Count() => _depth > 0 ? _counts[_depth - 1] : 0;

    private void Count(int value)
    {
        if (_depth > 0) _counts[_depth - 1] = value;
    }

    /// <summary>
    /// The error a malformed document produces, quoting the spot — a bare offset is not enough to see what the
    /// reader tripped over.
    /// </summary>
    private FormatException Malformed(string what)
    {
        var start = _position > 40 ? _position - 40 : 0;
        var length = System.Math.Min(80, _text.Length - start);
        var context = _text.Substring(start, length).Replace("\r", "\\r").Replace("\n", "\\n");

        return new FormatException(
            $"Malformed archive document: {what} (at offset {_position}). Around there: …{context}…");
    }
}
