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
/// <c>RegisterWriter</c> twice. The ownership is stated by the caller rather than inferred, so these tests pin the
/// rule itself; that the generator states it correctly is pinned separately, on the generated text.
/// </remarks>
[TestClass]
public class RegistryOwnershipTests
{
    [TestMethod]
    public void AConsumerCannotReplaceTheDeclaringAssemblysEntry()
    {
        // Offset 的条目是 VeloxDev.Core 自己登记的。
        var coreWriter = VeloxJsonRegistry.WriterFor(typeof(Offset));
        var coreReader = VeloxJsonRegistry.ReaderFor(typeof(Offset));
        Assert.IsNotNull(coreWriter, "the declaring assembly's registration must have run");
        Assert.IsNotNull(coreReader);

        VeloxJsonRegistry.RegisterWriter(typeof(Offset), new LocalWriter(), declaresType: false);
        VeloxJsonRegistry.RegisterReader(typeof(Offset), new LocalReader(), declaresType: false);

        Assert.AreSame(coreWriter, VeloxJsonRegistry.WriterFor(typeof(Offset)), "the narrower view must not win");
        Assert.AreSame(coreReader, VeloxJsonRegistry.ReaderFor(typeof(Offset)));
    }

    [TestMethod]
    public void AConsumerMayStillRegisterWhatNobodyHasClaimed()
    {
        // 消费方不是「不许注册」，是「不许覆盖」—— 它带来的可能正是声明方命名不出的那个封闭实例。
        var writer = new LocalWriter();
        var reader = new LocalReader();

        VeloxJsonRegistry.RegisterWriter(typeof(UnclaimedType), writer, declaresType: false);
        VeloxJsonRegistry.RegisterReader(typeof(UnclaimedType), reader, declaresType: false);

        Assert.AreSame(writer, VeloxJsonRegistry.WriterFor(typeof(UnclaimedType)));
        Assert.AreSame(reader, VeloxJsonRegistry.ReaderFor(typeof(UnclaimedType)));

        // 第二个消费方也改不动它 —— 谁先接上算谁的，之后只认那个。
        VeloxJsonRegistry.RegisterWriter(typeof(UnclaimedType), new LocalWriter(), declaresType: false);
        VeloxJsonRegistry.RegisterReader(typeof(UnclaimedType), new LocalReader(), declaresType: false);

        Assert.AreSame(writer, VeloxJsonRegistry.WriterFor(typeof(UnclaimedType)));
        Assert.AreSame(reader, VeloxJsonRegistry.ReaderFor(typeof(UnclaimedType)));
    }

    [TestMethod]
    public void TheDeclaringAssemblyAlwaysWins()
    {
        // 声明方唯一，所以它写两次就是「换一份」，不存在两个声明方争同一个类型。
        var first = new LocalWriter();
        VeloxJsonRegistry.RegisterWriter(typeof(LocalType), first, declaresType: true);
        Assert.AreSame(first, VeloxJsonRegistry.WriterFor(typeof(LocalType)), "a first registration lands");

        var second = new LocalWriter();
        VeloxJsonRegistry.RegisterWriter(typeof(LocalType), second, declaresType: true);
        Assert.AreSame(second, VeloxJsonRegistry.WriterFor(typeof(LocalType)), "and the declaring assembly replaces it");

        // 声明方还压得住已经在那儿的消费方条目。
        VeloxJsonRegistry.RegisterReader(typeof(LocalType), new LocalReader(), declaresType: false);
        var owning = new LocalReader();
        VeloxJsonRegistry.RegisterReader(typeof(LocalType), owning, declaresType: true);
        Assert.AreSame(owning, VeloxJsonRegistry.ReaderFor(typeof(LocalType)), "the wider view takes the entry");
    }

    /// <summary>A type this assembly declares.</summary>
    private sealed class LocalType;

    /// <summary>A type nobody's generated code registers — the state a first consumer meets.</summary>
    private sealed class UnclaimedType;

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
