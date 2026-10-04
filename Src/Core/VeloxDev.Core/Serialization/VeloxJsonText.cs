using System;
using System.Globalization;
using System.IO;

#if NET8_0_OR_GREATER
using System.Buffers;
#endif

namespace VeloxDev.Serialization;

// 一个值在文档里怎么拼。归档序列化器与 JSON 树都从这里取拼法，所以同一个字符串两边逃逸结果一致、
// 同一个数字两边写出来一样；这份规则有两份拷贝，就等于有两个地方会开始产生不同的文档。
internal static class VeloxJsonText
{
    // 需要转义的只有三类：双引号、反斜杠、以及全部 C0 控制字符（含具名的 \b \f \n \r \t）。
    // 判定「这一段是否干净」时用它，而绝大多数字符串一个都不含。
    private static readonly char[] Escapable = BuildEscapable();

#if NET8_0_OR_GREATER
    private static readonly SearchValues<char> EscapableSearch = SearchValues.Create(Escapable);
#endif

    private static char[] BuildEscapable()
    {
        var chars = new char[34];
        var index = 0;
        for (var c = 0; c < ' '; c++) chars[index++] = (char)c;
        chars[index++] = '"';
        chars[index] = '\\';
        return chars;
    }

    // 从 start 起第一个需要转义的字符，没有则 -1。转义是罕见的，所以这里只负责「找到下一个」，
    // 真正省下时间的是调用方：干净区间整块写，而不是每字符一次虚调用。
    internal static int IndexOfEscapable(string value, int start)
    {
#if NET8_0_OR_GREATER
        var index = value.AsSpan(start).IndexOfAny(EscapableSearch);
        return index < 0 ? -1 : start + index;
#else
        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"' || c == '\\' || c < ' ') return i;
        }
        return -1;
#endif
    }

    // 一个字符的转义拼法；不需要转义时返回 null。**只有这一份** —— 归档写入器（写进自己的缓冲）
    // 与 JSON 树（写进 TextWriter）都从这里取，「文档怎么拼」因此不可能出现第二种说法。
    internal static string? EscapeSequence(char c)
    {
        switch (c)
        {
            case '"': return "\\\"";
            case '\\': return "\\\\";
            case '\b': return "\\b";
            case '\f': return "\\f";
            case '\n': return "\\n";
            case '\r': return "\\r";
            case '\t': return "\\t";
            default:
                return c < ' ' ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture) : null;
        }
    }

    // 写出字符串的内容，引号由调用方负责，这里不写。
    internal static void Escape(TextWriter writer, string value)
    {
        var index = IndexOfEscapable(value, 0);

        // 常见情形：整串没有一处要转义 —— 一次 Write 写完。原先这里对每个字符都要走一次虚调用，
        // 一个 32 字符的 id 就是 32 次，而这正是文档里数量最多的东西。
        if (index < 0)
        {
            writer.Write(value);
            return;
        }

        var start = 0;
        while (index >= 0)
        {
            // 两处转义之间的干净区间整块写。Substring 只在真的含转义的字符串上才会发生。
            if (index > start) writer.Write(value.Substring(start, index - start));

            WriteEscapedChar(writer, value[index]);

            start = index + 1;
            index = IndexOfEscapable(value, start);
        }

        if (start < value.Length) writer.Write(value.Substring(start));
    }

    // 调用方保证 c 一定需要转义，所以 EscapeSequence 不会返回 null。
    private static void WriteEscapedChar(TextWriter writer, char c)
        => writer.Write(EscapeSequence(c));

    // 最短往返拼写，整数值补 .0，非有限值写成字符串。
    internal static string Double(double value)
    {
        if (double.IsNaN(value)) return "\"NaN\"";
        if (double.IsPositiveInfinity(value)) return "\"Infinity\"";
        if (double.IsNegativeInfinity(value)) return "\"-Infinity\"";

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0) text += ".0";
        return text;
    }
}
