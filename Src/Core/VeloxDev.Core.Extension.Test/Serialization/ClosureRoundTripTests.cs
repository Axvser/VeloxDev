using System.Collections.Generic;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// What the type closure takes in beyond a member's declared type: a derived class held by a base-typed member,
/// the implementations behind a map's interface key, and the family a type parameter is constrained to.
/// </summary>
/// <remarks>
/// Each group below is written so that nothing but the rule under test can account for the types being recorded —
/// they appear in no member's declared type anywhere in this assembly.
/// </remarks>
[TestClass]
public partial class ClosureRoundTripTests
{
    internal sealed partial class Zoo
    {
        [VeloxProperty] private Animal? pet;
    }

    internal class Animal
    {
        public string? Name { get; set; }
    }

    internal class Dog : Animal
    {
        public bool Barks { get; set; }

        // 深两层的成员：Dog 自己带一个 Collar，Collar 又带一个声明成基类的 Fastener。
        // 闭包是工作表算法 —— 每一个被收进来的类型都会再走一遍成员，所以应该一路跟到底。
        public Collar? Collar { get; set; }
    }

    internal class Collar
    {
        public string? Color { get; set; }

        public Fastener? Fastener { get; set; }
    }

    internal class Fastener
    {
        public string? Kind { get; set; }
    }

    internal class Buckle : Fastener
    {
        public bool Metal { get; set; }
    }

    /// <summary>
    /// Writing used to fail outright here: the document names the runtime type, and no declaration mentioned it.
    /// </summary>
    [TestMethod]
    public void AMemberDeclaredAsABaseClass_ReadsBackAsTheDerivedType()
    {
        var restored = VeloxJsonSerializer.Deserialize<Zoo>(
            VeloxJsonSerializer.Serialize(new Zoo
            {
                Pet = new Dog
                {
                    Name = "Rex",
                    Barks = true,
                    Collar = new Collar
                    {
                        Color = "red",
                        Fastener = new Buckle { Kind = "clip", Metal = true },
                    },
                },
            }))!;

        Assert.IsInstanceOfType(restored.Pet, typeof(Dog),
            "$type carries the runtime type, so the reader has to have an entry for it");
        Assert.AreEqual("Rex", restored.Pet!.Name);
        Assert.IsTrue(((Dog)restored.Pet).Barks);

        var collar = ((Dog)restored.Pet).Collar!;
        Assert.AreEqual("red", collar.Color, "a member of a type reached by that first step is walked too");

        Assert.IsInstanceOfType(collar.Fastener, typeof(Buckle),
            "and the walk follows the same rule at that depth");
        Assert.IsTrue(((Buckle)collar.Fastener!).Metal);
    }

    internal interface IWeighted
    {
        string? Label { get; set; }
    }

    internal class Weight : IWeighted
    {
        public string? Label { get; set; }
    }

    internal sealed partial class Keyed
    {
        // 键是接口，而 IWeighted 与 Weight 在本程序集里别的成员声明上一个都没出现。
        [VeloxProperty] private Dictionary<IWeighted, int> weights = [];
    }

    [TestMethod]
    public void AnInterfaceKeyedMapsImplementations_GetAnEntry()
    {
        // 只断言「有条目」而不做往返：接口键写成键对象的引用 id，而那个对象得先在文档别处以完整对象
        // 出现过读侧才解析得回来 —— 那个不变量是独立的已知契约，不是这条规则保证的。
        Assert.IsNotNull(VeloxJsonRegistry.WriterFor(typeof(Weight)),
            "the key is written as a reference id, so what that id resolves to needs an entry of its own");
    }

    internal class Instrument
    {
        public string? Model { get; set; }
    }

    internal class Piano : Instrument
    {
        public int Keys { get; set; }
    }

    /// <summary>An open generic, so it never gets an entry of its own — only what its parameter may hold does.</summary>
    [Archivable]
    internal partial class ConstrainedHost<T> where T : Instrument
    {
        public T? Value { get; set; }
    }

    [TestMethod]
    public void ATypeParametersConstraintFamily_GetsAnEntry()
    {
        Assert.IsNotNull(VeloxJsonRegistry.WriterFor(typeof(Instrument)),
            "the constraint itself is what a value of T can be declared as");

        Assert.IsNotNull(VeloxJsonRegistry.WriterFor(typeof(Piano)),
            "T is constrained to Instrument, so a value of T can be a Piano");
    }

    [TestMethod]
    public void ATypeThatNoDeclarationNames_IsStillLeftOut()
    {
        Assert.IsNull(VeloxJsonRegistry.WriterFor(typeof(Unrelated)),
            "the closure widens along inheritance, it does not sweep the assembly");
    }

    internal class Unrelated
    {
        public string? Tag { get; set; }
    }
}
