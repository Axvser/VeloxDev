using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace VeloxDev.Serialization;

// 异步面：与同步面逐字同义，唯一的差别是**续读要 await**。
//
// 为什么要有两份：同步面服务 string 入口（文档已在内存里，永不发生 I/O，因此永远不必 await），
// 异步面服务流入口（每一次把数据读进来都是 ReadAsync）。合并成一份的代价是让每个 token 都穿过
// 状态机 —— 那是内存内路径上纯亏的开销。
//
// 两份必须行为一致：改其中一份时另一份要跟着改，VeloxJsonStreamingTests 会在两条路上跑同一批语料。
public sealed partial class VeloxJsonReader
{
    // ── 续读 ──────────────────────────────────────────────────────────────────────────────────────────

    // 异步版的 Require。string 入口上它和同步版一样立刻返回 —— 那条路永远不需要等。
    private async Task<bool> RequireAsync(int floorAbs, int count)
    {
        if (_text is not null) return _position + count <= _text.Length;

        while (!_sourceComplete && _position + count > _origin + _length)
            await FillAsync(floorAbs, count).ConfigureAwait(false);

        return _position + count <= _origin + _length;
    }

    private async Task FillAsync(int floorAbs, int count)
    {
        PrepareSpace(floorAbs, count);

        while (!_sourceComplete && _origin + _length < _position + count)
        {
            var read = await _reader!.ReadAsync(_window, _length, _window.Length - _length).ConfigureAwait(false);
            if (read == 0) { _sourceComplete = true; break; }

            _length += read;
        }
    }

    // ── 原语 ──────────────────────────────────────────────────────────────────────────────────────────

    // 当前位置的字符；文档结束时返回 -1。异步方法不能用 out，所以用 int 传。
    private async Task<int> PeekCodeAsync()
    {
        if (!await RequireAsync(_position, 1).ConfigureAwait(false)) return -1;

        return CharAt(_position);
    }

    private async Task<char> PeekAsync()
    {
        if (!await RequireAsync(_position, 1).ConfigureAwait(false)) throw Malformed("unexpected end of document");
        return CharAt(_position);
    }

    private async Task SkipWhitespaceAsync()
    {
        while (await PeekCodeAsync().ConfigureAwait(false) is var c && c >= 0 && char.IsWhiteSpace((char)c)) _position++;
    }

    private async Task ExpectAsync(char expected)
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);
        if (await PeekAsync().ConfigureAwait(false) != expected) throw Malformed($"expected '{expected}'");
        _position++;
    }

    private async Task<char> PeekAfterWhitespaceAsync()
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);
        return await PeekAsync().ConfigureAwait(false);
    }

    // ── 字符串与 token ────────────────────────────────────────────────────────────────────────────────

    private async Task<string> ReadQuotedAsync()
    {
        await ExpectAsync('"').ConfigureAwait(false);

        var start = _position;

        // 起点当下限：整串必须连续，压缩时不许把它切掉。
        while (await RequireAsync(start, 1).ConfigureAwait(false))
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

        if (!await RequireAsync(start, 1).ConfigureAwait(false)) throw Malformed("unterminated string");

        var builder = new StringBuilder(_position - start + 16);
        AppendTo(builder, start, _position - start);
        return await ReadEscapedTailAsync(builder).ConfigureAwait(false);
    }

    private async Task<string> ReadEscapedTailAsync(StringBuilder builder)
    {
        while (true)
        {
            if (!await RequireAsync(_position, 1).ConfigureAwait(false)) throw Malformed("unterminated string");

            var c = CharAt(_position++);
            if (c == '"') return builder.ToString();

            if (c != '\\') { builder.Append(c); continue; }

            if (!await RequireAsync(_position, 1).ConfigureAwait(false)) throw Malformed("unterminated escape");
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
                    if (!await RequireAsync(_position, 4).ConfigureAwait(false)) throw Malformed("truncated escape");
                    var code = 0;
                    for (var i = 0; i < 4; i++) code = (code << 4) | HexValue(CharAt(_position + i));
                    builder.Append((char)code);
                    _position += 4;
                    break;
                default: throw Malformed($"unknown escape '\\{escape}'");
            }
        }
    }

    private async Task<string> ReadBareTokenAsync()
    {
        var start = _position;

        while (await RequireAsync(start, 1).ConfigureAwait(false))
        {
            var c = CharAt(_position);
            if (char.IsWhiteSpace(c) || c is ',' or '}' or ']') break;
            _position++;
        }

        if (_position == start) throw Malformed("expected a value");
        return Slice(start, _position - start);
    }

    private async Task<bool> ReadLiteralAsync(string literal)
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);

        if (!await RequireAsync(_position, literal.Length).ConfigureAwait(false)) return false;

        for (var i = 0; i < literal.Length; i++)
        {
            if (CharAt(_position + i) != literal[i]) return false;
        }

        _position += literal.Length;
        return true;
    }

    private async Task<bool> StartsWithQuotedAsync(string name)
    {
        if (!await RequireAsync(_position, name.Length + 2).ConfigureAwait(false)) return false;
        if (CharAt(_position) != '"') return false;

        for (var i = 0; i < name.Length; i++)
        {
            if (CharAt(_position + 1 + i) != name[i]) return false;
        }

        return CharAt(_position + 1 + name.Length) == '"';
    }

    private async Task<int> ReadReferenceIdAsync()
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);

        var quoted = await PeekAsync().ConfigureAwait(false) == '"';
        if (quoted) _position++;

        var start = _position;
        var value = 0;

        while (await RequireAsync(_position, 1).ConfigureAwait(false) && CharAt(_position) is >= '0' and <= '9')
        {
            value = value * 10 + (CharAt(_position) - '0');
            _position++;
        }

        if (_position == start) throw Malformed("expected a reference id");

        if (quoted)
        {
            if (!await RequireAsync(_position, 1).ConfigureAwait(false) || CharAt(_position) != '"')
                throw Malformed("expected a reference id");
            _position++;
        }

        return value;
    }

    // ── 对象与数组 ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Asynchronously starts an object, taking its <c>$id</c> and <c>$type</c> when present.</summary>
    /// <returns>
    /// <c>Opened</c> is <see langword="false"/> when the document held a reference, in which case the object is
    /// reachable through <see cref="ResolveReference"/> and the cursor has moved past it — the same contract as
    /// the synchronous <c>BeginObject(out int, out string?)</c>, which cannot be used here because an
    /// asynchronous method may not have <c>out</c> parameters.
    /// </returns>
    public async Task<(bool Opened, int ReferenceId, string? TypeName)> BeginObjectAsync()
    {
        ReleaseMember();

        await ExpectAsync('{').ConfigureAwait(false);
        await SkipWhitespaceAsync().ConfigureAwait(false);

        // `$ref` 是一个只有一项的对象 —— 读到它就没有成员可读了。
        if (await TryReadReferenceAsync().ConfigureAwait(false) is { } reference)
            return (false, reference, null);

        EnterLevel();

        var referenceId = 0;
        if (await TryBeginMetadataAsync("$id").ConfigureAwait(false))
            referenceId = await ReadReferenceIdAsync().ConfigureAwait(false);

        string? typeName = null;
        if (await TryBeginMetadataAsync("$type").ConfigureAwait(false))
            typeName = await PeekAsync().ConfigureAwait(false) == '"'
                ? await ReadQuotedAsync().ConfigureAwait(false)
                : await ReadBareTokenAsync().ConfigureAwait(false);

        return (true, referenceId, typeName);
    }

    /// <summary>Moves to the next member of the current object, without materialising its name.</summary>
    /// <returns><see langword="false"/> when the object has no more members.</returns>
    public async Task<bool> NextMemberAsync()
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);
        if (await PeekAsync().ConfigureAwait(false) == '}') return false;

        if (Count() > 0) await ExpectAsync(',').ConfigureAwait(false);
        Count(Count() + 1);

        await SkipWhitespaceAsync().ConfigureAwait(false);
        await ExpectAsync('"').ConfigureAwait(false);

        _memberStart = _position;
        _memberHasEscape = false;
        _memberHeld = true;

        while (true)
        {
            var c = await PeekCodeAsync().ConfigureAwait(false);
            if (c < 0) throw Malformed("unterminated member name");

            if (c == '"') break;
            if (c == '\\') _memberHasEscape = true;
            _position++;
        }

        _memberLength = _position - _memberStart;
        _position++;
        await SkipWhitespaceAsync().ConfigureAwait(false);
        await ExpectAsync(':').ConfigureAwait(false);
        return true;
    }

    /// <summary>Materialises the member name <see cref="NextMemberAsync"/> just moved to.</summary>
    /// <returns>The member's name.</returns>
    /// <remarks>
    /// The synchronous pair is <c>NextMember(out string)</c>; the generated readers dispatch on
    /// <see cref="MemberNameEquals"/> instead and never call this. Only the shape-driven paths — an unknown
    /// type read as a map, an interface-keyed map — need the name as a string.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No member has been read yet.</exception>
    public Task<string> ReadMemberNameAsync()
    {
        if (!_memberHeld) throw new InvalidOperationException("ReadMemberNameAsync needs a preceding NextMemberAsync.");

        _memberHeld = false;

        var name = _memberHasEscape ? DecodeMemberName() : Slice(_memberStart, _memberLength);
        return Task.FromResult(name);
    }

    /// <summary>Consumes an object's closing brace, asynchronously.</summary>
    public async Task FinishObjectAsync()
    {
        LeaveLevel();
        await ExpectAsync('}').ConfigureAwait(false);
    }

    /// <summary>Starts an array, asynchronously.</summary>
    public async Task BeginArrayAsync()
    {
        ReleaseMember();

        await ExpectAsync('[').ConfigureAwait(false);
        EnterLevel();
    }

    /// <summary>Moves to the next element, asynchronously.</summary>
    /// <returns><see langword="false"/> when the array has no more elements.</returns>
    public async Task<bool> NextElementAsync()
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);
        if (await PeekAsync().ConfigureAwait(false) == ']') return false;

        if (Count() > 0) await ExpectAsync(',').ConfigureAwait(false);
        Count(Count() + 1);
        return true;
    }

    /// <summary>Consumes an array's closing bracket, asynchronously.</summary>
    public async Task FinishArrayAsync()
    {
        LeaveLevel();
        await ExpectAsync(']').ConfigureAwait(false);
    }

    private async Task<int?> TryReadReferenceAsync()
    {
        var checkpoint = _position;
        var savedFloor = _checkpointFloor;

        // 探测期间可能续读并压缩，回退位置必须先压下限保住。
        _checkpointFloor = Math.Min(_checkpointFloor, checkpoint);

        try
        {
            if (await TryBeginMetadataAsync("$ref").ConfigureAwait(false))
            {
                var id = await ReadReferenceIdAsync().ConfigureAwait(false);

                if (await PeekAfterWhitespaceAsync().ConfigureAwait(false) == '}')
                {
                    _position++;
                    return id;
                }
            }

            _position = checkpoint;
            return null;
        }
        finally
        {
            _checkpointFloor = savedFloor;
        }
    }

    private async Task<bool> TryBeginMetadataAsync(string name)
    {
        var checkpoint = _position;
        var savedFloor = _checkpointFloor;

        _checkpointFloor = Math.Min(_checkpointFloor, checkpoint);

        try
        {
            await SkipWhitespaceAsync().ConfigureAwait(false);

            if (Count() > 0 && await PeekAsync().ConfigureAwait(false) == ',')
            {
                _position++;
                await SkipWhitespaceAsync().ConfigureAwait(false);
            }

            if (!await StartsWithQuotedAsync(name).ConfigureAwait(false))
            {
                _position = checkpoint;
                return false;
            }

            _position += name.Length + 2;
            await SkipWhitespaceAsync().ConfigureAwait(false);
            await ExpectAsync(':').ConfigureAwait(false);
            await SkipWhitespaceAsync().ConfigureAwait(false);

            Count(Count() + 1);
            return true;
        }
        finally
        {
            _checkpointFloor = savedFloor;
        }
    }

    // ── 标量 ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Whether the next token is the JSON <c>null</c> literal, asynchronously.</summary>
    public async Task<bool> NextIsNullAsync()
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);

        if (!await RequireAsync(_position, 4).ConfigureAwait(false)) return false;

        return CharAt(_position) == 'n'
            && CharAt(_position + 1) == 'u'
            && CharAt(_position + 2) == 'l'
            && CharAt(_position + 3) == 'l';
    }

    /// <summary>Whether the next token is a quoted string, asynchronously.</summary>
    public async Task<bool> NextIsStringAsync()
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);
        return await PeekAsync().ConfigureAwait(false) == '"';
    }

    /// <summary>Whether the next token opens an array, asynchronously.</summary>
    public async Task<bool> NextIsArrayAsync()
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);
        return await PeekAsync().ConfigureAwait(false) == '[';
    }

    /// <summary>Whether the next token opens an object, asynchronously.</summary>
    public async Task<bool> NextIsObjectAsync()
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);
        return await PeekAsync().ConfigureAwait(false) == '{';
    }

    /// <summary>Reads a string asynchronously; a JSON <c>null</c> reads as <see langword="null"/>.</summary>
    /// <summary>
    /// Reads an enum written as the name of its value, or the JSON <c>null</c> literal.
    /// </summary>
    /// <param name="enumType">The enum's type.</param>
    /// <returns>The enum member, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="enumType"/> is <see langword="null"/>.</exception>
    /// <remarks>The asynchronous counterpart of <see cref="ReadEnum(Type)"/>; same reading, awaited refills.</remarks>
    public async Task<object?> ReadEnumAsync(Type enumType)
    {
        if (enumType is null) throw new ArgumentNullException(nameof(enumType));

        var text = await ReadStringAsync().ConfigureAwait(false);
        return text is null ? null : Enum.Parse(enumType, text, ignoreCase: true);
    }

    public async Task<string?> ReadStringAsync()
    {
        ReleaseMember();

        if (await NextIsNullAsync().ConfigureAwait(false)) { _position += 4; return null; }
        return await ReadQuotedAsync().ConfigureAwait(false);
    }

    /// <summary>Reads a value's text form asynchronously, whether written as a string or bare.</summary>
    public async Task<string> ReadTextAsync()
    {
        ReleaseMember();

        await SkipWhitespaceAsync().ConfigureAwait(false);
        return await PeekAsync().ConfigureAwait(false) == '"'
            ? await ReadQuotedAsync().ConfigureAwait(false)
            : await ReadBareTokenAsync().ConfigureAwait(false);
    }

    /// <summary>Reads a boolean asynchronously.</summary>
    /// <exception cref="FormatException">The token is neither <c>true</c> nor <c>false</c>.</exception>
    public async Task<bool> ReadBooleanAsync()
    {
        ReleaseMember();

        if (await ReadLiteralAsync("true").ConfigureAwait(false)) return true;
        if (await ReadLiteralAsync("false").ConfigureAwait(false)) return false;

        throw Malformed("expected a boolean");
    }

    /// <summary>Reads a 32-bit integer asynchronously.</summary>
    public async Task<int> ReadInt32Async()
        => int.Parse(await ReadTextAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);

    /// <summary>Reads a 64-bit integer asynchronously.</summary>
    public async Task<long> ReadInt64Async()
        => long.Parse(await ReadTextAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);

    /// <summary>Reads a double asynchronously, including the string spellings of the non-finite values.</summary>
    public async Task<double> ReadDoubleAsync()
        => (await ReadTextAsync().ConfigureAwait(false)) switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            var text => double.Parse(text, CultureInfo.InvariantCulture),
        };

    /// <summary>Reads a single-precision value asynchronously.</summary>
    public async Task<float> ReadSingleAsync() => (float)await ReadDoubleAsync().ConfigureAwait(false);

    /// <summary>Reads a decimal asynchronously.</summary>
    public async Task<decimal> ReadDecimalAsync()
        => decimal.Parse(await ReadTextAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);

    /// <summary>Reads a globally unique identifier asynchronously.</summary>
    public async Task<Guid> ReadGuidAsync() => Guid.Parse(await ReadTextAsync().ConfigureAwait(false));

    /// <summary>Skips one value of any shape, asynchronously.</summary>
    public async Task SkipValueAsync()
    {
        ReleaseMember();

        await SkipWhitespaceAsync().ConfigureAwait(false);

        switch (await PeekAsync().ConfigureAwait(false))
        {
            case '{':
                await BeginObjectAsync().ConfigureAwait(false);
                while (await NextMemberAsync().ConfigureAwait(false)) await SkipValueAsync().ConfigureAwait(false);
                await FinishObjectAsync().ConfigureAwait(false);
                return;

            case '[':
                await BeginArrayAsync().ConfigureAwait(false);
                while (await NextElementAsync().ConfigureAwait(false)) await SkipValueAsync().ConfigureAwait(false);
                await FinishArrayAsync().ConfigureAwait(false);
                return;

            case '"':
                await ReadQuotedAsync().ConfigureAwait(false);
                return;

            default:
                await ReadBareTokenAsync().ConfigureAwait(false);
                return;
        }
    }

    /// <summary>Whether the whole document has been consumed, reading ahead asynchronously if needed.</summary>
    /// <returns><see langword="true"/> when nothing but whitespace remains.</returns>
    public async Task<bool> AtEndAsync()
    {
        await SkipWhitespaceAsync().ConfigureAwait(false);
        return !await RequireAsync(_position, 1).ConfigureAwait(false);
    }
}
