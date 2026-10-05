using System;
using System.Threading.Tasks;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.Serialization;

/// <summary>
/// Who owns a registry entry when two assemblies both offer one.
/// </summary>
/// <remarks>
/// A closed generic can be named by a consumer that its declaring assembly cannot see
/// (<c>SlotEnumerator&lt;ThatConsumer'sSlot&gt;</c>), so the same <see cref="Type"/> can reach
/// <c>RegisterWriter</c> twice. Only the declaring assembly may replace what is already there — its view of the
/// type is the wider one — and these two tests pin both halves of that rule.
/// </remarks>
[TestClass]
public class RegistryOwnershipTests
{
    [TestMethod]
    public void AConsumerCannotReplaceTheDeclaringAssemblysEntry()
    {
        // Offset 的条目是 VeloxDev.Core 自己登记的，实现类在 Core 里。
        var coreWriter = VeloxJsonRegistry.WriterFor(typeof(Offset));
        var coreReader = VeloxJsonRegistry.ReaderFor(typeof(Offset));
        Assert.IsNotNull(coreWriter, "the declaring assembly's registration must have run");
        Assert.IsNotNull(coreReader);

        // 本程序集不是 Offset 的声明方（实现类在 Core.Test 里）—— 这两次注册必须整个丢掉。
        VeloxJsonRegistry.RegisterWriter(typeof(Offset), new LocalWriter());
        VeloxJsonRegistry.RegisterReader(typeof(Offset), new LocalReader());

        Assert.AreSame(coreWriter, VeloxJsonRegistry.WriterFor(typeof(Offset)), "the narrower view must not win");
        Assert.AreSame(coreReader, VeloxJsonRegistry.ReaderFor(typeof(Offset)));
    }

    [TestMethod]
    public void TheDeclaringAssemblyMayReplaceItsOwnEntry()
    {
        var first = new LocalWriter();
        VeloxJsonRegistry.RegisterWriter(typeof(LocalType), first);
        Assert.AreSame(first, VeloxJsonRegistry.WriterFor(typeof(LocalType)), "a first registration always lands");

        var second = new LocalWriter();
        VeloxJsonRegistry.RegisterWriter(typeof(LocalType), second);
        Assert.AreSame(second, VeloxJsonRegistry.WriterFor(typeof(LocalType)), "and the declaring assembly may replace it");
    }

    /// <summary>A type this assembly declares, so a writer emitted here owns its entry.</summary>
    private sealed class LocalType;

    private sealed class LocalWriter : IVeloxJsonWriter
    {
        public void Write(VeloxJsonWriter writer, object value, Type? declaredType) => writer.WriteNull();

        public Task WriteAsync(VeloxJsonWriter writer, object value, Type? declaredType) => writer.WriteNullAsync();
    }

    private sealed class LocalReader : IVeloxJsonReader
    {
        public object Create() => new LocalType();

        public void Read(VeloxJsonReader reader, object target) => reader.SkipValue();

        public Task ReadAsync(VeloxJsonReader reader, object target) => reader.SkipValueAsync();
    }
}
