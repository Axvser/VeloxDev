using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeloxDev.Core.Test.MVVM;
using VeloxDev.Generators;

namespace VeloxDev.Core.Test.Serialization;

/// <summary>
/// The <c>declaresType</c> flag the generated registration carries, asserted on the generated text.
/// </summary>
/// <remarks>
/// <para>
/// <c>VeloxJsonRegistry</c> keeps an entry that is already there unless the caller says it declares the type, so
/// the flag is what decides which of two assemblies offering the same type wins. It is asked for rather than
/// inferred, which moves the risk from "the answer was never stated" to "the answer is stated wrongly" — and that
/// one is silent: a consumer would simply take the entry at some later module load.
/// </para>
/// <para>
/// Both halves are asserted here. The probe declares <c>Probe.Model</c> itself, and merely <b>names</b>
/// <c>SlotEnumerator&lt;SlotDefaultViewModel&gt;</c> — a closed generic declared in another assembly, whose
/// instantiation only a consumer that can see <c>SlotDefaultViewModel</c> ever names.
/// </para>
/// </remarks>
[TestClass]
public class RegistrationOwnershipEmissionTests
{
    private const string ItsOwnTypeAndAForeignClosedGeneric = """
        using VeloxDev.MVVM;
        using VeloxDev.WorkflowSystem;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private int count;

            public SlotEnumerator<SlotDefaultViewModel>? Slots { get; set; }
        }
        """;

    private static string Generated()
        => GeneratorProbe.Run(new VeloxJson(), ItsOwnTypeAndAForeignClosedGeneric, "Probe").Generated;

    private static string WriterLineFor(string typePrefix)
        => Line(generated: Generated(), registration: "RegisterWriter", typePrefix: typePrefix);

    private static string Line(string generated, string registration, string typePrefix)
    {
        var line = generated
            .Split('\n')
            .FirstOrDefault(l => l.Contains(registration + "(typeof(" + typePrefix, StringComparison.Ordinal));

        Assert.IsNotNull(line, $"no {registration} was emitted for {typePrefix}");
        return line;
    }

    [TestMethod]
    public void ATypeTheAssemblyDeclares_IsRegisteredAsItsOwn()
    {
        var writer = WriterLineFor("global::Probe.Model)");
        StringAssert.Contains(writer, "declaresType: true", "the type the probe declares is its own to register");

        StringAssert.Contains(
            Line(Generated(), "RegisterReader", "global::Probe.Model)"),
            "declaresType: true",
            "and its reader agrees");
    }

    [TestMethod]
    public void AClosedGenericTheAssemblyOnlyNames_IsRegisteredAsAForeign()
    {
        // `SlotEnumerator<>` 与 `SlotDefaultViewModel` 都声明在 Core，探针只是**命名**了这个封闭实例 ——
        // 声明它们的那一侧命名不出 `Probe.*`，所以这一条只能由消费方发，而消费方不是声明方。
        var writer = WriterLineFor("global::VeloxDev.WorkflowSystem.SlotEnumerator<");
        StringAssert.Contains(writer, "declaresType: false", "a closed generic named from elsewhere is not this assembly's own");

        StringAssert.Contains(
            Line(Generated(), "RegisterReader", "global::VeloxDev.WorkflowSystem.SlotEnumerator<"),
            "declaresType: false",
            "and its reader agrees");
    }
}
