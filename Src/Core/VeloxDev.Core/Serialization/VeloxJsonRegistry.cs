using System;
using System.Collections.Generic;

namespace VeloxDev.Serialization;

/// <summary>
/// Writes one type's members. Generated, one implementation per serializable type.
/// </summary>
/// <remarks>
/// The implementation is a straight-line sequence of member writes — every member name is a literal known when
/// the declaring assembly was compiled, so nothing here reflects over metadata.
/// </remarks>
public interface IVeloxJsonWriter
{
    /// <summary>
    /// Writes <paramref name="value"/>, which is an instance of this writer's type.
    /// </summary>
    /// <param name="writer">The document being built.</param>
    /// <param name="value">The instance to write.</param>
    /// <param name="declaredType">
    /// The member's declared type, or <see langword="null"/> when the value is the document root or is written
    /// where its own type is the only thing that could be meant. A type name is written when it differs from the
    /// value's runtime type.
    /// </param>
    void Write(VeloxJsonWriter writer, object value, Type? declaredType);
}

/// <summary>
/// Builds one type's instances from a document. Generated, one implementation per serializable type.
/// </summary>
/// <remarks>
/// A reader may be handed an existing instance to fill (<c>target</c>), which is how the format restores objects
/// the graph already refers to; when it is <see langword="null"/> the reader creates one.
/// </remarks>
public interface IVeloxJsonReader
{
    /// <summary>
    /// Creates a blank instance to read into.
    /// </summary>
    /// <returns>The new instance.</returns>
    object Create();

    /// <summary>
    /// Reads this type's members into <paramref name="target"/>, consuming through the object's closing brace.
    /// </summary>
    /// <param name="reader">The document being read, positioned just inside the object.</param>
    /// <param name="target">The instance to fill. The caller has already created or reused it.</param>
    void Read(VeloxJsonReader reader, object target);
}

/// <summary>
/// Runs just before a type's members are written.
/// </summary>
/// <remarks>
/// Callbacks are interfaces rather than attributes on purpose: an implementation is visible to the compiler and
/// to a reader of the type, where an attribute is a stringly-typed convention that fails silently when it is
/// misspelled.
/// </remarks>
public interface IVeloxJsonSerializing
{
    /// <summary>Called before this instance's members are written.</summary>
    void OnSerializing();
}

/// <summary>
/// Runs just after a type's members are written, whatever happened while writing them.
/// </summary>
public interface IVeloxJsonSerialized
{
    /// <summary>Called after this instance's members are written.</summary>
    void OnSerialized();
}

/// <summary>
/// Runs just before a type's members are read into an instance.
/// </summary>
public interface IVeloxJsonDeserializing
{
    /// <summary>Called before this instance's members are read.</summary>
    void OnDeserializing();
}

/// <summary>
/// Runs just after every member of a type has been read.
/// </summary>
/// <remarks>
/// After <b>every</b> member: a type whose members depend on one another — an enum key that needs the type name
/// beside it, an enumerator whose items only make sense once its selector type is known — must be able to rely
/// on all of them being present, which is why this cannot be a generated setter.
/// </remarks>
public interface IVeloxJsonDeserialized
{
    /// <summary>Called after every member of this instance has been read.</summary>
    void OnDeserialized();
}

/// <summary>
/// The compile-time facts the archive serializer needs: which writer and reader belong to each type, and the
/// name a type is written as.
/// </summary>
/// <remarks>
/// <para>
/// Registration comes from generated <c>[ModuleInitializer]</c> methods in every assembly that declares a
/// serializable type, so the table is complete before any caller can reach it and no host has to wire anything.
/// </para>
/// <para>
/// A type with no entry cannot be written. That is the closed world the format lives in, and it is what removes
/// the reflection: the set of types an archive can contain is the set the generator was compiled over.
/// </para>
/// <para>
/// <b>Lookups take no lock.</b> The five tables live in a snapshot that is replaced, never mutated, so a
/// reader sees a whole table or the previous whole table and never a half-built one. Registration stays
/// serialised behind <c>Gate</c>, which is where a new snapshot is built and published. This matters because
/// lookups sit on the per-node path — a writer is looked up for every value written and a reader for every
/// object read — so a shared lock there was a bottleneck on every document, contended or not.
/// </para>
/// </remarks>
public static class VeloxJsonRegistry
{
    // 注册只在模块初始化器里发生，之后整份快照只读；用 volatile 发布，读侧就彻底免锁了。
    private static readonly object Gate = new();
    private static volatile Snapshot _snapshot = Snapshot.Empty;

    // 一份完整的注册表。里面的表发布之后不再被任何人改动，所以并发读是安全的 —— 换表换的是整个快照。
    private sealed class Snapshot
    {
        internal static readonly Snapshot Empty = new([], [], [], [], new(StringComparer.Ordinal));

        internal readonly Dictionary<Type, IVeloxJsonWriter> Writers;
        internal readonly Dictionary<Type, IVeloxJsonReader> Readers;
        internal readonly Dictionary<Type, Func<object>> ContainerFactories;
        internal readonly Dictionary<Type, string> Names;
        internal readonly Dictionary<string, Type> TypesByName;

        private Snapshot(
            Dictionary<Type, IVeloxJsonWriter> writers,
            Dictionary<Type, IVeloxJsonReader> readers,
            Dictionary<Type, Func<object>> containerFactories,
            Dictionary<Type, string> names,
            Dictionary<string, Type> typesByName)
        {
            Writers = writers;
            Readers = readers;
            ContainerFactories = containerFactories;
            Names = names;
            TypesByName = typesByName;
        }

        // 复制一份再改。只有被换掉的那张表需要复制，其余三张原样带走 —— 它们本来就不会再变。
        internal Snapshot WithWriter(Type type, IVeloxJsonWriter writer)
            => new(new Dictionary<Type, IVeloxJsonWriter>(Writers) { [type] = writer }, Readers, ContainerFactories, Names, TypesByName);

        internal Snapshot WithReader(Type type, IVeloxJsonReader reader)
            => new(Writers, new Dictionary<Type, IVeloxJsonReader>(Readers) { [type] = reader }, ContainerFactories, Names, TypesByName);

        internal Snapshot WithContainerFactory(Type type, Func<object> factory)
            => new(Writers, Readers, new Dictionary<Type, Func<object>>(ContainerFactories) { [type] = factory }, Names, TypesByName);

        // 类型名那张表是双向的：写用 Names，读用 TypesByName，所以两份都必须一起换。
        internal Snapshot WithName(Type type, string name)
        {
            var names = new Dictionary<Type, string>(Names) { [type] = name };
            var typesByName = new Dictionary<string, Type>(TypesByName, StringComparer.Ordinal) { [name] = type };
            return new Snapshot(Writers, Readers, ContainerFactories, names, typesByName);
        }
    }

    /// <summary>
    /// Registers how to construct a container the document holds as a nested value.
    /// </summary>
    /// <param name="type">The container's declared type — <c>Dictionary&lt;K, V&gt;</c>, <c>List&lt;T&gt;</c>.</param>
    /// <param name="factory">Creates an empty instance the value can be read into.</param>
    /// <remarks>
    /// A container at the top of a member is filled in place by that member's generated reader. One nested inside
    /// another — the tree's <c>LinksMap</c> is a map of maps — has nowhere to be filled, and constructing a
    /// generic container from a <see cref="Type"/> alone takes <c>Activator</c>, which the format exists to avoid.
    /// So the generator, which met the closed combination, registers how to make one.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static void RegisterContainerFactory(Type type, Func<object> factory)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        lock (Gate) _snapshot = _snapshot.WithContainerFactory(type, factory);
    }

    /// <summary>The factory for a nested container type, or <see langword="null"/> when it has none.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The factory, or <see langword="null"/>.</returns>
    public static Func<object>? ContainerFactoryFor(Type type)
    {
        var snapshot = _snapshot;
        return snapshot.ContainerFactories.TryGetValue(type, out var factory) ? factory : null;
    }

    /// <summary>
    /// Registers one type's writer.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="writer">Its writer.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static void RegisterWriter(Type type, IVeloxJsonWriter writer)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        lock (Gate) _snapshot = _snapshot.WithWriter(type, writer);
    }

    /// <summary>
    /// Registers one type's reader.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="reader">Its reader.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static void RegisterReader(Type type, IVeloxJsonReader reader)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        lock (Gate) _snapshot = _snapshot.WithReader(type, reader);
    }

    /// <summary>
    /// Registers the name a type is written as.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="name">
    /// The name, in the form existing archives carry — <c>Namespace.Type, AssemblyName</c>, the assembly-qualified
    /// name without its version and culture. It is the wire format, not a label.
    /// </param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static void RegisterName(Type type, string name)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (name is null) throw new ArgumentNullException(nameof(name));

        lock (Gate) _snapshot = _snapshot.WithName(type, name);
    }

    /// <summary>The name a type is written as, or <see langword="null"/> when it has none.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The registered name, or <see langword="null"/>.</returns>
    public static string? NameOf(Type type)
    {
        var snapshot = _snapshot;
        return snapshot.Names.TryGetValue(type, out var name) ? name : null;
    }

    /// <summary>The type a written name stands for, or <see langword="null"/> when nothing registered it.</summary>
    /// <param name="name">A name from the document.</param>
    /// <returns>The type, or <see langword="null"/>.</returns>
    public static Type? TypeOf(string name)
    {
        var snapshot = _snapshot;
        return snapshot.TypesByName.TryGetValue(name, out var type) ? type : null;
    }

    /// <summary>The writer for a type, or <see langword="null"/> when it has none.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The writer, or <see langword="null"/>.</returns>
    public static IVeloxJsonWriter? WriterFor(Type type)
    {
        var snapshot = _snapshot;
        return snapshot.Writers.TryGetValue(type, out var writer) ? writer : null;
    }

    /// <summary>The reader for a type, or <see langword="null"/> when it has none.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The reader, or <see langword="null"/>.</returns>
    public static IVeloxJsonReader? ReaderFor(Type type)
    {
        var snapshot = _snapshot;
        return snapshot.Readers.TryGetValue(type, out var reader) ? reader : null;
    }
}
