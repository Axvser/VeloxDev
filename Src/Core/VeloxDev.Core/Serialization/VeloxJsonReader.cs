using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
public sealed partial class VeloxJsonReader
{
    // 整份文档已在手上时用它（string 入口）。这条路上所有取值都退化成直接索引，且从不续读。
    private readonly string? _text;

    // 流出源。窗口可压缩、可增长，保证「一个 token 在窗口里始终连续」。
    private readonly TextReader? _reader;
    private char[] _window = [];
    private int _origin;          // _window[0] 对应的文档绝对偏移
    private int _length;          // 窗口里的有效字符数
    private bool _sourceComplete;

    private readonly Dictionary<int, object> _references = [];

    // 文档绝对偏移。绝对化是关键：窗口压缩会改 _origin，而所有偏移字段因此都不用跟着改。
    private int _position;

    // 每个嵌套层各有一份「已经读过几项」的计数：用一个字段的话，内层读完会把外层的逗号状态清掉，
    // 外层就会在逗号上找引号。
    private int[] _counts = new int[16];
    private int _depth;

    // 最近一次 NextMember() 读到的成员名在原文里的位置。名字不落成字符串，靠这三个字段供
    // MemberNameEquals 就地比对。
    private int _memberStart;
    private int _memberLength;
    private bool _memberHasEscape;

    // 名字还没被消费掉时置位，压缩时必须保住它。任何取值方法都要清掉 —— 否则一个长字符串值
    // 会把下限钉在名字上，内存又变回 O(整份文档)。
    private bool _memberHeld;

    // 探测（$ref / $id / $type）回退期间要保住的位置。int.MaxValue 表示没有。LIFO：探测是嵌套的。
    private int _checkpointFloor = int.MaxValue;

    /// <summary>
    /// Creates a reader over <paramref name="json"/>, which already holds the whole document.
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

    /// <summary>
    /// Creates a reader that pulls the document from <paramref name="reader"/> as the cursor advances.
    /// </summary>
    /// <param name="reader">The source. Not disposed, and not read further than the cursor needs.</param>
    /// <param name="bufferSize">How many characters to hold at once; grows for one longer token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The document is empty.</exception>
    /// <remarks>
    /// Peak memory is the buffer plus the object graph rather than the whole document. The format only ever
    /// writes a <c>$ref</c> to an object already written, so one forward pass is enough — nothing needs to be
    /// re-read.
    /// </remarks>
    public VeloxJsonReader(TextReader reader, int bufferSize = 8192)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (bufferSize < 1) throw new ArgumentOutOfRangeException(nameof(bufferSize));

        _reader = reader;
        _window = new char[bufferSize];

        // 先拉一次：空文档要抛与 string 入口同一个异常，否则同一个错误按入口变成两种。
        if (!Require(0, 1)) throw new ArgumentException("The document is empty.", nameof(reader));
    }

    /// <summary>Creates a reader over a UTF-8 stream, honouring a byte order mark.</summary>
    /// <param name="stream">The source. Left open.</param>
    /// <param name="bufferSize">How many characters to hold at once.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public VeloxJsonReader(Stream stream, int bufferSize = 8192)
        : this(
            new StreamReader(
                stream ?? throw new ArgumentNullException(nameof(stream)),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize,
                leaveOpen: true),
            bufferSize)
    {
    }

    /// <summary>Whether the whole document has been consumed.</summary>
    public bool AtEnd
    {
        get
        {
            SkipWhitespace();

            // 空的窗口不代表文档结束 —— 要先试着续读一次再下结论。
            return !Require(_position, 1);
        }
    }

    // 窗口最后一个有效字符之后的绝对偏移。string 入口就是文档长度。
    private int End => _text?.Length ?? _origin + _length;

    // 绝对偏移处取字符。string 路径上这个分支永远同向，代价只是一次可预测跳转。
    private char CharAt(int absolute) => _text is not null ? _text[absolute] : _window[absolute - _origin];

    // 取当前位置的字符；窗口用尽时先把下一段读进来。false 表示文档结束。
    private bool TryPeek(out char c)
    {
        if (!Require(_position, 1)) { c = '\0'; return false; }

        c = CharAt(_position);
        return true;
    }

    // 自 _position 起至少能看到 count 个字符吗；不够就先续读。false 表示文档结束、拿不出这么多。
    // floorAbs 是「从这里往后都不许丢」的下限：扫描不定长 token 时传它的起点。
    private bool Require(int floorAbs, int count)
    {
        // string 入口从不续读，只需判够不够。
        if (_text is not null) return _position + count <= _text.Length;

        while (!_sourceComplete && _position + count > _origin + _length) Fill(floorAbs, count);

        return _position + count <= _origin + _length;
    }

    private void Fill(int floorAbs, int count)
    {
        PrepareSpace(floorAbs, count);

        while (!_sourceComplete && _origin + _length < _position + count)
        {
            var read = _reader!.Read(_window, _length, _window.Length - _length);
            if (read == 0) { _sourceComplete = true; break; }

            _length += read;
        }
    }

    // 压缩 + 增长：让窗口从 _position 起装得下 count 个字符。同步与异步的续读共用它，
    // 两边的差别只在「怎么把数据读进来」。
    private void PrepareSpace(int floorAbs, int count)
    {
        // 保留区下限：所有还活着的位置中最靠前的那个，它之前的字符一律丢弃。
        var keep = Math.Min(_position, floorAbs);
        if (_memberHeld) keep = Math.Min(keep, _memberStart);
        if (_checkpointFloor != int.MaxValue) keep = Math.Min(keep, _checkpointFloor);

        // 先压缩。搬的是 [keep, 末尾)，_origin 随之改变；所有偏移字段是绝对的，因此都不用动。
        if (keep > _origin)
        {
            Array.Copy(_window, keep - _origin, _window, 0, End - keep);
            _length = End - keep;
            _origin = keep;
        }

        // 一个 token 比整个窗口还长就增长 —— 「O(最大单个 token)」这条预算指的就是这里。
        var needed = (_position - _origin) + count;
        if (needed > _window.Length) Array.Resize(ref _window, Math.Max(needed, _window.Length * 2));
    }

    // 取值方法都要调它：那个名字已经被消费掉了，压缩时不必再保。
    private void ReleaseMember() => _memberHeld = false;

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
        // 名字已经派发用完，可以放手了 —— 不放的话，后面一个长值会把压缩下限钉在名字上。
        ReleaseMember();

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

        if (TryBeginMetadata("$id")) referenceId = ReadReferenceId();

        if (TryBeginMetadata("$type")) typeName = Peek() == '"' ? ReadQuoted() : ReadBareToken();

        return true;
    }

    /// <summary>
    /// Reads the next member's name.
    /// </summary>
    /// <param name="name">The member's name.</param>
    /// <returns><see langword="false"/> when the object has no more members.</returns>
    public bool NextMember(out string name)
    {
        // 这条重载把名字落成字符串交出去，不需要为 MemberNameEquals 留原文 —— 顺手解除 pin，
        // 否则上面那条无参重载留下的标记会一直钉住压缩下限。
        ReleaseMember();

        SkipWhitespace();
        if (Peek() == '}') { name = string.Empty; return false; }

        if (Count() > 0) Expect(',');
        Count(Count() + 1);

        name = ReadQuoted();
        SkipWhitespace();
        Expect(':');
        return true;
    }

    /// <summary>
    /// Moves to the next member of the current object without materialising its name.
    /// </summary>
    /// <returns><see langword="false"/> when the object has no more members.</returns>
    /// <remarks>
    /// <see cref="MemberNameEquals"/> compares the name in place afterwards. A generated reader dispatches on
    /// member names, and those names are compile-time literals — allocating a string for one only to compare it
    /// against a literal and discard it is pure waste, and this is the pair that lets it not.
    /// </remarks>
    public bool NextMember()
    {
        SkipWhitespace();
        if (Peek() == '}') return false;

        if (Count() > 0) Expect(',');
        Count(Count() + 1);

        SkipWhitespace();
        Expect('"');

        _memberStart = _position;
        _memberHasEscape = false;

        // 先置位再往下走：后面的 SkipWhitespace / Expect 可能触发压缩，而这个名字还要留给
        // MemberNameEquals 比对，丢了就没法派发。
        _memberHeld = true;

        while (true)
        {
            if (!TryPeek(out var c)) throw Malformed("unterminated member name");

            if (c == '"') break;
            if (c == '\\') _memberHasEscape = true;
            _position++;
        }

        _memberLength = _position - _memberStart;
        _position++;
        SkipWhitespace();
        Expect(':');
        return true;
    }

    /// <summary>
    /// Whether the member <see cref="NextMember()"/> just moved to is the named one.
    /// </summary>
    /// <param name="name">The member name to compare against.</param>
    /// <returns><see langword="true"/> when the names are equal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool MemberNameEquals(string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));

        // 名字含转义的兜底：文档里不会出现（生成器写出的都是属性名），真遇上就先解码再比。
        if (_memberHasEscape) return string.Equals(DecodeMemberName(), name, StringComparison.Ordinal);

        if (_memberLength != name.Length) return false;

        // string 路径交给运行时的序数比较 —— 派发链每个成员都要比几次，逐字符走 CharAt 会有代价。
        if (_text is not null) return string.CompareOrdinal(_text, _memberStart, name, 0, name.Length) == 0;

        // 窗口路径逐字符比，不落成字符串。_memberHeld 保证这段还在窗口里。
        for (var i = 0; i < name.Length; i++)
        {
            if (CharAt(_memberStart + i) != name[i]) return false;
        }

        return true;
    }

    // 把刚读到的成员名按转义规则解码出来。只在名字含转义时走到。
    private string DecodeMemberName()
    {
        var saved = _position;
        var savedFloor = _checkpointFloor;

        // 回到开引号，复用慢路径的解码，再退回原处。退回的位置必须跨压缩保住，所以压一次下限。
        _position = _memberStart - 1;
        _checkpointFloor = Math.Min(_checkpointFloor, _position);
        try { return ReadQuoted(); }
        finally
        {
            _position = saved;
            _checkpointFloor = savedFloor;
        }
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
        ReleaseMember();

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

    /// <summary>Whether the next token is a quoted string.</summary>
    public bool NextIsString()
    {
        SkipWhitespace();
        return Peek() == '"';
    }

    /// <summary>Whether the next token opens an array.</summary>
    public bool NextIsArray()
    {
        SkipWhitespace();
        return Peek() == '[';
    }

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

        // 长度感知：不足四个字符就不许拿前缀去比 —— 否则分块边界上会把 "nul" 判成 null。
        if (!Require(_position, 4)) return false;

        return CharAt(_position) == 'n'
            && CharAt(_position + 1) == 'u'
            && CharAt(_position + 2) == 'l'
            && CharAt(_position + 3) == 'l';
    }

    /// <summary>
    /// Reads an enum written as the name of its value, or the JSON <c>null</c> literal.
    /// </summary>
    /// <param name="enumType">The enum's type.</param>
    /// <returns>The enum member, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="enumType"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The counterpart of <c>[Archive(ArchiveOptions.EnumName)]</c>. A name the enum does not define comes back as
    /// an undefined value rather than an error, which is what <see cref="Enum.Parse(Type, string, bool)"/> does
    /// and what the numeric spelling already did.
    /// </remarks>
    public object? ReadEnum(Type enumType)
    {
        if (enumType is null) throw new ArgumentNullException(nameof(enumType));

        var text = ReadString();
        return text is null ? null : Enum.Parse(enumType, text, ignoreCase: true);
    }

    /// <summary>Reads a string; a JSON <c>null</c> reads as <see langword="null"/>.</summary>
    /// <returns>The value.</returns>
    public string? ReadString()
    {
        ReleaseMember();

        if (NextIsNull()) { _position += 4; return null; }
        return ReadQuoted();
    }

    /// <summary>Reads a value's text form, whether the document wrote it as a string or bare.</summary>
    /// <returns>The token's text.</returns>
    public string ReadText()
    {
        ReleaseMember();

        SkipWhitespace();
        return Peek() == '"' ? ReadQuoted() : ReadBareToken();
    }

    /// <summary>Reads a boolean.</summary>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The token is neither <c>true</c> nor <c>false</c>.</exception>
    public bool ReadBoolean()
    {
        ReleaseMember();

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
        ReleaseMember();

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

        // string 路径直接索引，不走 Require/CharAt —— 这个循环按字符跑，间接层在这里看得见。
        if (_text is not null)
        {
            while (_position < _text.Length)
            {
                var c = _text[_position];
                if (char.IsWhiteSpace(c) || c is ',' or '}' or ']') break;
                _position++;
            }

            if (_position == start) throw Malformed("expected a value");
            return _text.Substring(start, _position - start);
        }

        // 窗口路径：起点当下限，token 可以比整个窗口还长，压缩时不许丢掉它。
        while (Require(start, 1))
        {
            var c = CharAt(_position);
            if (char.IsWhiteSpace(c) || c is ',' or '}' or ']') break;
            _position++;
        }

        if (_position == start) throw Malformed("expected a value");
        return Slice(start, _position - start);
    }

    private bool ReadLiteral(string literal)
    {
        SkipWhitespace();

        // 长度感知：不足这么多字符就不比 —— 前缀比较会在分块边界上把 "tru" 判成 true。
        if (!Require(_position, literal.Length)) return false;

        for (var i = 0; i < literal.Length; i++)
        {
            if (CharAt(_position + i) != literal[i]) return false;
        }

        _position += literal.Length;
        return true;
    }

    // 从绝对偏移切一段出来。窗口路径上要求那段仍在缓冲里 —— 调用方用 floorAbs 保证。
    private string Slice(int absolute, int length)
        => _text is not null
            ? _text.Substring(absolute, length)
            : new string(_window, absolute - _origin, length);

    // 把一段原文追加进 StringBuilder，同样要求那段还在缓冲里。
    private void AppendTo(StringBuilder builder, int absolute, int length)
    {
        if (_text is not null) builder.Append(_text, absolute, length);
        else builder.Append(_window, absolute - _origin, length);
    }

    /// <summary>Reads the <c>{"$ref":"n"}</c> form, leaving the cursor untouched when it is not that.</summary>
    private bool TryReadReference(out int referenceId)
    {
        var checkpoint = _position;
        var savedFloor = _checkpointFloor;

        // 探测期间可能续读并压缩，回退位置必须先压下限保住。
        _checkpointFloor = Math.Min(_checkpointFloor, checkpoint);

        try
        {
            if (TryBeginMetadata("$ref"))
            {
                var id = ReadReferenceId();

                if (PeekAfterWhitespace() == '}')
                {
                    _position++;
                    referenceId = id;
                    // 这里没有进过层 —— BeginObject 是在探测之后才 EnterLevel 的，所以也不能在这里退层。
                    return true;
                }
            }

            _position = checkpoint;
            referenceId = 0;
            return false;
        }
        finally
        {
            _checkpointFloor = savedFloor;
        }
    }

    // 光标处若是 "name": 就消费掉它并把光标停在冒号之后，否则原地不动。
    // 元数据成员（$id / $type / $ref）每个对象都要探两次，所以这条路上一次分配都不该有。
    private bool TryBeginMetadata(string name)
    {
        var checkpoint = _position;
        var savedFloor = _checkpointFloor;

        _checkpointFloor = Math.Min(_checkpointFloor, checkpoint);

        try
        {
            SkipWhitespace();

            // 前面已经读过一项时会有一个分隔逗号挡在这里；读不到就整体回退。
            if (Count() > 0 && Peek() == ',')
            {
                _position++;
                SkipWhitespace();
            }

            // 名字里不会有转义，所以直接在原文上比一段 —— 原先是先造出一个 string 再比，每个对象白造两个。
            if (!StartsWithQuoted(name))
            {
                _position = checkpoint;
                return false;
            }

            _position += name.Length + 2;
            SkipWhitespace();
            Expect(':');
            SkipWhitespace();

            Count(Count() + 1);
            return true;
        }
        finally
        {
            _checkpointFloor = savedFloor;
        }
    }

    // 光标处是否正好是 "name"（含两侧引号）。
    private bool StartsWithQuoted(string name)
    {
        // 长度感知：窗口截断时不许拿前缀去比，否则会把半个名字判成匹配。
        if (!Require(_position, name.Length + 2)) return false;
        if (CharAt(_position) != '"') return false;

        for (var i = 0; i < name.Length; i++)
        {
            if (CharAt(_position + 1 + i) != name[i]) return false;
        }

        return CharAt(_position + 1 + name.Length) == '"';
    }

    // $id 与 $ref 的值都是十进制小整数（写侧从 1 递增，没有负号），而且写成带引号的字符串
    // —— 见 VeloxJsonWriter.WriteStartObject 用的是 WriteString。裸写也认，与旧的 ReadText 一致。
    // 两种情况都原地解析，省掉一个只用来喂 int.Parse 的子串，每个对象一次。
    private int ReadReferenceId()
    {
        SkipWhitespace();

        var quoted = Peek() == '"';
        if (quoted) _position++;

        var start = _position;
        var value = 0;

        while (Require(_position, 1) && CharAt(_position) is >= '0' and <= '9')
        {
            value = value * 10 + (CharAt(_position) - '0');
            _position++;
        }

        if (_position == start) throw Malformed("expected a reference id");

        if (quoted)
        {
            // 收尾的引号可能在下一段里 —— 先续读再判，否则会把合法的 id 判成坏的。
            if (!Require(_position, 1) || CharAt(_position) != '"') throw Malformed("expected a reference id");
            _position++;
        }

        return value;
    }

    private string ReadQuoted()
    {
        Expect('"');

        // 快路径：先只找结束引号，一个字符都不复制。成员名、类型名、id 全走这条 ——
        // 原先是无条件先建一个 StringBuilder，哪怕整串一个转义都没有，而需要转义的字符串极少。
        var start = _position;

        // string 路径直接索引，不走 Require/CharAt —— 每个字符串都要扫一遍，间接层在这里看得见。
        if (_text is not null)
        {
            while (_position < _text.Length)
            {
                var c = _text[_position];

                if (c == '"')
                {
                    var text = _text.Substring(start, _position - start);
                    _position++;
                    return text;
                }

                // 碰到转义就转慢路径；已经扫过的那一段交给它做前缀。
                if (c == '\\') break;

                _position++;
            }

            if (_position >= _text.Length) throw Malformed("unterminated string");

            var escaped = new StringBuilder(_position - start + 16);
            escaped.Append(_text, start, _position - start);
            return ReadEscapedTail(escaped);
        }

        // 窗口路径：起点当下限，整串必须连续，压缩时不许把它切掉。
        while (Require(start, 1))
        {
            var c = CharAt(_position);

            if (c == '"')
            {
                var text = Slice(start, _position - start);
                _position++;
                return text;
            }

            if (c == '\\') break;

            _position++;
        }

        if (!Require(start, 1)) throw Malformed("unterminated string");

        var builder = new StringBuilder(_position - start + 16);
        AppendTo(builder, start, _position - start);
        return ReadEscapedTail(builder);
    }

    // 从一个 '\\' 起把一个字符串读完。只有真的含转义的字符串才走这里。
    private string ReadEscapedTail(StringBuilder builder)
    {
        while (true)
        {
            if (!Require(_position, 1)) throw Malformed("unterminated string");

            var c = CharAt(_position++);
            if (c == '"') return builder.ToString();

            if (c != '\\') { builder.Append(c); continue; }

            if (!Require(_position, 1)) throw Malformed("unterminated escape");
            var escape = CharAt(_position++);
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
                    // 原地按十六进制解析四位，不再切一个子串出来。四位可能跨在两段之间，先续读足。
                    if (!Require(_position, 4)) throw Malformed("truncated escape");
                    var code = 0;
                    for (var i = 0; i < 4; i++) code = (code << 4) | HexValue(CharAt(_position + i));
                    builder.Append((char)code);
                    _position += 4;
                    break;
                default: throw Malformed($"unknown escape '\\{escape}'");
            }
        }
    }

    // 一个十六进制字符的值。非十六进制字符与原先 int.Parse(NumberStyles.HexNumber) 一样抛 FormatException。
    private static int HexValue(char c)
    {
        if (c is >= '0' and <= '9') return c - '0';
        if (c is >= 'a' and <= 'f') return c - 'a' + 10;
        if (c is >= 'A' and <= 'F') return c - 'A' + 10;

        throw new FormatException($"'{c}' is not a hexadecimal digit.");
    }

    private char Peek()
    {
        if (!Require(_position, 1)) throw Malformed("unexpected end of document");
        return CharAt(_position);
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
        // 每个 token 都要过这个循环，所以 string 路径直接索引、不走 Require —— 间接层在这里最贵。
        if (_text is not null)
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
            return;
        }

        while (TryPeek(out var c) && char.IsWhiteSpace(c)) _position++;
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
        // 上下文只从已经缓冲下来的部分取，绝不在这里续读 —— 那会掩盖原始错误，或者再抛一个异常出来。
        var from = Math.Max(_position - 40, _origin);
        var to = Math.Min(_position + 40, End);
        var context = Slice(from, to - from).Replace("\r", "\\r").Replace("\n", "\\n");

        return new FormatException(
            $"Malformed archive document: {what} (at offset {_position}). Around there: …{context}…");
    }
}
