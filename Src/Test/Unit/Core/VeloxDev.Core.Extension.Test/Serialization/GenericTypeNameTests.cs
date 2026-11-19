using System;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The name a generic type is registered under has to identify <b>one</b> closed type — two instantiations of the
/// same definition are two types with two readers, and the registry maps a name back to a single one.
/// </summary>
[TestClass]
public class GenericTypeNameTests
{
    [TestMethod]
    public void AClosedGeneric_IsNamedWithItsTypeArguments()
    {
        var name = VeloxJsonRegistry.NameOf(typeof(SlotEnumerator<SlotDefaultViewModel>));

        Assert.IsNotNull(name, "the test assembly registers this closed generic");
        StringAssert.Contains(name, "SlotEnumerator<", "the name has to say which instantiation it is");
        StringAssert.Contains(name, "SlotDefaultViewModel", "…and the argument is what distinguishes them");
    }

    [TestMethod]
    public void TheName_ResolvesBackToThatExactType()
    {
        // 名字是注册表的键：`$type` 靠它落回一个类型。同名两个封闭实例时，落错的那一个会让生成 reader
        // 里那句转型失败 —— 这正是名字要带上实参的原因。
        var closed = typeof(SlotEnumerator<SlotDefaultViewModel>);

        Assert.AreSame(closed, VeloxJsonRegistry.TypeOf(VeloxJsonRegistry.NameOf(closed)!));
    }
}
