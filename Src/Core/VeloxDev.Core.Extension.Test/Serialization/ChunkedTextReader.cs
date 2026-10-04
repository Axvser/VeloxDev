using System;
using System.IO;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>An in-memory source that never hands over more than <c>chunk</c> characters at a time.</summary>
/// <remarks>
/// Refill boundaries are the whole risk of a streaming reader, and they are invisible to a source that hands the
/// document over at once: nothing that reads a whole string reaches the code that compacts, grows, or compares
/// across a window edge. One character at a time puts every token on a boundary.
/// </remarks>
internal sealed class ChunkedTextReader(string text, int chunk) : TextReader
{
    private int _position;

    /// <inheritdoc />
    public override int Read(char[] buffer, int index, int count)
    {
        var take = Math.Min(Math.Min(chunk, count), text.Length - _position);
        if (take <= 0) return 0;

        text.CopyTo(_position, buffer, index, take);
        _position += take;
        return take;
    }
}
