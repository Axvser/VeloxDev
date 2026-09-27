using System;
using System.IO;
using System.Text;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>An <see cref="ILogWriter"/> over a delegate — the one-liner form for tests and hosts that already have a sink.</summary>
/// <param name="write">Called once per line, on the thread driving the run.</param>
public sealed class DelegateLogWriter(Action<string> write) : ILogWriter
{
    private readonly Action<string> _write = write ?? throw new ArgumentNullException(nameof(write));

    /// <inheritdoc />
    public void Write(string line) => _write(line);
}

/// <summary>
/// An <see cref="ILogWriter"/> that appends to a <see cref="System.IO.TextWriter"/> — the usual case being a
/// file opened for append.
/// </summary>
/// <remarks>
/// <para>
/// <b>Whoever opens the stream closes it.</b> A writer built on a <see cref="System.IO.TextWriter"/> someone else
/// handed in never closes that writer — the host keeps appending across runs with it. One built by
/// <see cref="For"/> opened the file itself, so disposing it flushes and closes. Disposing either kind flushes.
/// </para>
/// <para>
/// Encoding follows the convention <c>ComponentModelEx</c> already uses for text this library writes: UTF-8
/// <b>without</b> a byte-order mark.
/// </para>
/// <para>
/// Like every <see cref="ILogWriter"/>, it is called on the thread driving the run — which for a compiled run is
/// normally the host's UI thread. Wrap it (or the <see cref="TextWriter"/> it is given) in your own queue if the
/// IO must not happen there.
/// </para>
/// </remarks>
public sealed class TextWriterLogWriter : ILogWriter, IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;

    /// <summary>Wraps an existing writer, which this instance will not close.</summary>
    /// <param name="writer">Where lines are appended.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public TextWriterLogWriter(TextWriter writer) : this(writer, ownsWriter: false) { }

    private TextWriterLogWriter(TextWriter writer, bool ownsWriter)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _ownsWriter = ownsWriter;
    }

    /// <summary>
    /// Opens (or creates) <paramref name="path"/> for appending and returns a writer over it, UTF-8 without a
    /// byte-order mark.
    /// </summary>
    /// <param name="path">The log file. Its directory must exist.</param>
    /// <returns>
    /// A writer that owns the stream — dispose it to flush and close. Pass it to the session's
    /// <see cref="RuntimeContext.LogWriter"/> and dispose when the host is done with the file.
    /// </returns>
    public static TextWriterLogWriter For(string path)
    {
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new TextWriterLogWriter(
            new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)), ownsWriter: true);
    }

    /// <summary>Appends one line and flushes, so a host reading the file sees it without waiting for a buffer.</summary>
    /// <param name="line">The line, without a terminator.</param>
    public void Write(string line)
    {
        _writer.WriteLine(line);
        _writer.Flush();
    }

    /// <summary>
    /// Flushes, and closes the file when this instance opened it — a borrowed <see cref="TextWriter"/> is left
    /// open for its owner.
    /// </summary>
    public void Dispose()
    {
        _writer.Flush();
        if (_ownsWriter) _writer.Dispose();
    }
}
