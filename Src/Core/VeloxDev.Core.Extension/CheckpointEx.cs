using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Serialization;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Serialization;

/// <summary>
/// Persists an <see cref="ExecutionCheckpoint"/>: the JSON form, and the <see cref="IExecutionCheckpointStore"/>
/// that keeps it in a file.
/// </summary>
/// <remarks>
/// <para>
/// <b>It goes through the same settings the rest of the library uses</b> — the ones behind
/// <see cref="ViewModelSerializer"/> — rather than a private set of its own, so a checkpoint and the graph it
/// belongs to are written the same way, and a payload keeps its shape: a dictionary comes back as a dictionary
/// rather than a <c>JObject</c>.
/// </para>
/// <para>
/// <b>Numbers do not keep their type, and that is measured, not assumed.</b> A payload is
/// <see cref="object"/>, and JSON has one integer type: an <see cref="int"/> that went in comes back as a
/// <see cref="long"/>, a <see cref="float"/> as a <see cref="double"/>. <c>TypeNameHandling.All</c> does not help
/// — primitives are written as bare JSON values whatever the setting. The engine's own fields
/// (<see cref="ExecutionCheckpoint.Attempt"/>, the keys, the shape) are exact; a node that pattern-matches a
/// payload on <see cref="int"/> will not match after a resume. The store that keeps the object graph as it is —
/// <see cref="InMemoryCheckpointStore"/> — has no such gap.
/// </para>
/// <para>
/// <b>A checkpoint is a plain document, not a view model</b>, which is why it does not go through
/// <see cref="ViewModelSerializer.Serialize{T}(T)"/>: that surface is constrained to
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/>. It takes part in the archive format by declaring
/// itself serializable — see <see cref="VeloxSerializableAttribute"/>.
/// </para>
/// <para>
/// Like <see cref="CompiledGraphEx"/>, this stays in this assembly while the engine and the general view-model
/// surface live in <c>VeloxDev.Core</c>: these two are the workflow domain's own wrappers over it.
/// </para>
/// </remarks>
/// <seealso cref="FileCheckpointStore"/>
public static class CheckpointEx
{
    /// <summary>Writes a checkpoint as JSON.</summary>
    /// <param name="checkpoint">The checkpoint to write.</param>
    /// <returns>The JSON text, indented — it is a file a person may end up opening.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="checkpoint"/> is <c>null</c>.</exception>
    public static string SerializeCheckpoint(this ExecutionCheckpoint checkpoint)
    {
        if (checkpoint is null) throw new ArgumentNullException(nameof(checkpoint));
        return VeloxJsonSerializer.Serialize(checkpoint, indented: true);
    }

    /// <summary>Reads a checkpoint back.</summary>
    /// <param name="json">What <see cref="SerializeCheckpoint"/> wrote.</param>
    /// <returns>The checkpoint, or <c>null</c> when the text is empty or is not a checkpoint.</returns>
    public static ExecutionCheckpoint? DeserializeCheckpoint(this string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return VeloxJsonSerializer.Deserialize<ExecutionCheckpoint>(json);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// A checkpoint store backed by one file, so a run's place outlives the process that took it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One store, one file, one run.</b> Each save replaces the file's contents; the directory is created on the
/// first save if it does not exist.
/// </para>
/// <para>
/// The engine can have two fan-out branches saving at once (they interleave rather than run on separate threads,
/// but an await is enough to overlap them), so writes are serialised behind a gate — two writers on one file would
/// otherwise be free to interleave their bytes.
/// </para>
/// </remarks>
public sealed class FileCheckpointStore : IExecutionCheckpointStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the store.</summary>
    /// <param name="path">The file to keep the checkpoint in. Created, with its directory, on the first save.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c> or empty.</exception>
    public FileCheckpointStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        Path = path;
    }

    /// <summary>The file this store reads and writes.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public async Task SaveAsync(ExecutionCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        // 序列化放在门外：门下只留 I/O，别的分支不必等它的 CPU。
        var json = checkpoint.SerializeCheckpoint();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

#if NET8_0_OR_GREATER
            await File.WriteAllTextAsync(Path, json, cancellationToken).ConfigureAwait(false);
#else
            // netstandard2.0 / net461 没有 WriteAllTextAsync（那是 .NET Core 2.0 起的 API）。
            // 这两档保留同步写：兼容优先，而且它们不是流式那条路的目标。
            File.WriteAllText(Path, json);
#endif
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ExecutionCheckpoint?> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(Path)) return null;

#if NET8_0_OR_GREATER
            return (await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false)).DeserializeCheckpoint();
#else
            return File.ReadAllText(Path).DeserializeCheckpoint();
#endif
        }
        finally
        {
            _gate.Release();
        }
    }
}
