using Microsoft.CodeAnalysis;
using VeloxDev.Core.Test.MVVM;
using VeloxDev.Generators;

namespace VeloxDev.Core.Test.Serialization;

/// <summary>
/// The ways an <c>[Archive]</c> declaration can fail, and the one way <c>[Archivable]</c> widens the closed world.
/// </summary>
/// <remarks>
/// Driven through the compiler rather than against built fixtures: a rejected declaration never reaches the
/// generated file, so the diagnostic is the only place it shows up at all.
/// </remarks>
[TestClass]
public class ArchiveDiagnosticsTests
{
    private const string ReNameWithoutAName = """
        using VeloxDev.MVVM;
        using VeloxDev.Serialization;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private int count;

            [Archive(ArchiveOptions.ReName)]
            public string? Name { get; set; }
        }
        """;

    private const string KeepFieldOnAPairedField = """
        using VeloxDev.MVVM;
        using VeloxDev.Serialization;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty]
            [Archive(ArchiveOptions.KeepField)]
            private int count;
        }
        """;

    private const string MarkedButUnreachable = """
        using VeloxDev.MVVM;
        using VeloxDev.Serialization;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private int count;

            [Archive(ArchiveOptions.KeepProperty)]
            private string? Hidden => "computed";
        }
        """;

    [TestMethod]
    public void ARenameWithNothingToRenameTo_IsReported()
        => AssertReports("VELOX_JSON_MEMBER001", ReNameWithoutAName);

    [TestMethod]
    public void AMemberGeneratedCodeCannotReach_IsReported()
        => AssertReports("VELOX_JSON_MEMBER001", MarkedButUnreachable);

    [TestMethod]
    public void AFieldThatAlreadyHasAProperty_IsReportedRatherThanSilentlySwapped()
        => AssertReports("VELOX_JSON_MEMBER002", KeepFieldOnAPairedField);

    private const string ArrayInsideAContainer = """
        using System.Collections.Generic;
        using VeloxDev.MVVM;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private List<int[]> jagged = [];
        }
        """;

    /// <summary>A document that can never satisfy the reader: the member is required and never written.</summary>
    private const string RequiredButExcluded = """
        using System.Text.Json.Serialization;
        using VeloxDev.MVVM;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private int count;

            [JsonIgnore]
            [JsonRequired]
            public string? Tag { get; set; }
        }
        """;

    [TestMethod]
    public void AnArrayInsideAContainer_IsReportedRatherThanLeftToFailAtRunTime()
        => AssertReports("VELOX_JSON_MEMBER001", ArrayInsideAContainer);

    /// <summary>EnumName changes the spelling of a document, so asking for it where it cannot apply is an error.</summary>
    private const string EnumNameOnANonEnum = """
        using VeloxDev.MVVM;
        using VeloxDev.Serialization;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private int count;

            [Archive(ArchiveOptions.EnumName)]
            public string? Tag { get; set; }
        }
        """;

    [TestMethod]
    public void ARequiredMemberThatIsNeverWritten_IsReported()
        => AssertReports("VELOX_JSON_MEMBER001", RequiredButExcluded);

    [TestMethod]
    public void EnumNameOnAMemberThatIsNotAnEnum_IsReported()
        => AssertReports("VELOX_JSON_MEMBER001", EnumNameOnANonEnum);

    /// <summary>A named root the generator cannot emit an entry for — the declaration would be a runtime surprise.</summary>
    private const string NamedRootThatCannotBeWritten = """
        using VeloxDev.MVVM;
        using VeloxDev.Serialization;

        namespace Probe;

        [Archivable(typeof(AbstractThing))]
        public partial class Model
        {
            [VeloxProperty] private int count;
        }

        public abstract class AbstractThing
        {
            public string? Tag { get; set; }
        }
        """;

    /// <summary>Two callbacks for one moment: the BCL formatter would throw, so the generator has to say so.</summary>
    private const string TwoCallbacksForOneMoment = """
        using System.Runtime.Serialization;
        using VeloxDev.MVVM;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private int count;

            [OnDeserialized]
            public void First(StreamingContext context) { }

            [OnDeserialized]
            public void Second(StreamingContext context) { }
        }
        """;

    /// <summary>A callback the generated code cannot reach, so it would silently never run.</summary>
    private const string ACallbackThatCannotBeCalled = """
        using System.Runtime.Serialization;
        using VeloxDev.MVVM;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private int count;

            [OnDeserializing]
            private void Hidden(StreamingContext context) { }
        }
        """;

    /// <summary>A closed generic, whose registered name has to say which instantiation it is.</summary>
    private const string AClosedGenericMember = """
        using VeloxDev.MVVM;
        using VeloxDev.WorkflowSystem;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private SlotEnumerator<SlotDefaultViewModel>? enumerator;
        }
        """;

    /// <summary>
    /// A generic nested in a generic. Each level's arguments belong beside that level's own name — flattened onto
    /// the innermost one, <c>Envelope&lt;A&gt;.Inner&lt;B&gt;</c> and <c>Envelope.Inner&lt;A, B&gt;</c> would spell
    /// the same thing.
    /// </summary>
    private const string ANestedGenericMember = """
        using VeloxDev.MVVM;
        using VeloxDev.WorkflowSystem;

        namespace Probe;

        public class Envelope<T>
        {
            public class Inner<U>
            {
                public string? Tag { get; set; }
            }
        }

        public partial class Model
        {
            [VeloxProperty] private Envelope<SlotDefaultViewModel>.Inner<SlotDefaultViewModel>? nested;
        }
        """;

    [TestMethod]
    public void ANestedGeneric_PutsEachLevelsArgumentsBesideThatLevel()
    {
        var (diagnostics, generated) = GeneratorProbe.Run(new VeloxJson(), ANestedGenericMember, "Probe");

        Assert.IsFalse(diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(diagnostics));

        StringAssert.Contains(
            generated,
            "\"Probe.Envelope<VeloxDev.WorkflowSystem.SlotDefaultViewModel, VeloxDev.Core>"
                + "+Inner<VeloxDev.WorkflowSystem.SlotDefaultViewModel, VeloxDev.Core>, Probe\"",
            GeneratorProbe.Describe(diagnostics));
    }

    [TestMethod]
    public void AClosedGeneric_IsRegisteredUnderANameCarryingItsTypeArgument()
    {
        var (diagnostics, generated) = GeneratorProbe.Run(new VeloxJson(), AClosedGenericMember, "Probe");

        Assert.IsFalse(diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(diagnostics));

        StringAssert.Contains(
            generated,
            "\"VeloxDev.WorkflowSystem.SlotEnumerator<VeloxDev.WorkflowSystem.SlotDefaultViewModel, VeloxDev.Core>, VeloxDev.Core\"",
            "without the argument, SlotEnumerator<A> and SlotEnumerator<B> would register the same name");
    }

    [TestMethod]
    public void ANamedRootThatCannotBeWritten_IsReported()
        => AssertReports("VELOX_JSON_ARCH001", NamedRootThatCannotBeWritten);

    [TestMethod]
    public void TwoCallbacksForOneMoment_AreReported()
        => AssertReports("VELOX_JSON_HOOK001", TwoCallbacksForOneMoment);

    [TestMethod]
    public void ACallbackTheGeneratedCodeCannotReach_IsReported()
        => AssertReports("VELOX_JSON_HOOK002", ACallbackThatCannotBeCalled);

    [TestMethod]
    public void ANamedRoot_TakesPartWithoutBeingReachableFromAComponent()
    {
        const string source = """
            using VeloxDev.MVVM;
            using VeloxDev.Serialization;

            namespace Probe;

            [Archivable(typeof(Extra))]
            public partial class Model
            {
                [VeloxProperty] private int count;
            }

            public class Extra
            {
                public string? Tag { get; set; }
            }
            """;

        var (diagnostics, generated) = GeneratorProbe.Run(new VeloxJson(), source, "Probe");

        Assert.IsFalse(diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(diagnostics));
        StringAssert.Contains(generated, "global::Probe.Extra", "the named type gets an entry of its own");
    }

    /// <summary>
    /// A member declared as a concrete base class. The value written is a derived one, and <c>$type</c> names the
    /// derived type — so without an entry for it the document cannot be written at all.
    /// </summary>
    private const string APolymorphicMemberDeclaredAsAConcreteBase = """
        using VeloxDev.MVVM;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private Animal? pet;
        }

        public class Animal
        {
            public string? Name { get; set; }
        }

        public class Dog : Animal
        {
            public bool Barks { get; set; }
        }

        public class Unrelated
        {
            public string? Tag { get; set; }
        }
        """;

    /// <summary>
    /// A root-shaped open generic. It can never have an entry of its own — entries go to closed instantiations —
    /// but what its type parameter is constrained to is what a value of that parameter can be.
    /// </summary>
    private const string AConstrainedTypeParameterOnARootShapedGeneric = """
        using VeloxDev.Serialization;

        namespace Probe;

        [Archivable]
        public partial class Host<T> where T : Animal
        {
            public T? Value { get; set; }
        }

        public class Animal
        {
            public string? Name { get; set; }
        }

        public class Dog : Animal
        {
            public bool Barks { get; set; }
        }
        """;

    /// <summary>An unconstrained parameter says nothing about what it may hold, so nothing can be taken in.</summary>
    private const string ATypeParameterWithNoResolvableConstraint = """
        using VeloxDev.Serialization;

        namespace Probe;

        [Archivable]
        public partial class Host<T> where T : object
        {
            public T? Value { get; set; }
        }
        """;

    /// <summary>
    /// An interface-keyed map. Its keys are written as the key objects' reference ids, so the implementations are
    /// what those ids resolve to — and the key type is named nowhere else in the compilation.
    /// </summary>
    private const string AnInterfaceKeyedMap = """
        using System.Collections.Generic;
        using VeloxDev.MVVM;

        namespace Probe;

        public partial class Model
        {
            [VeloxProperty] private Dictionary<ISlot, int> weights = [];
        }

        public interface ISlot
        {
            string? Name { get; set; }
        }

        public class Slot : ISlot
        {
            public string? Name { get; set; }
        }
        """;

    [TestMethod]
    public void ADerivedTypeOfAConcreteBaseMember_IsTakenIn()
    {
        var (diagnostics, generated) = GeneratorProbe.Run(new VeloxJson(), APolymorphicMemberDeclaredAsAConcreteBase, "Probe");

        Assert.IsFalse(diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(diagnostics));

        StringAssert.Contains(generated, "\"Probe.Dog, Probe\"",
            "a member declared as Animal can hold a Dog, and $type names the Dog");
        Assert.IsFalse(generated.Contains("\"Probe.Unrelated, Probe\""),
            "the closure widens along inheritance, it does not sweep the assembly");
    }

    [TestMethod]
    public void AConstraintOnARootShapedOpenGeneric_TakesItsFamilyIn()
    {
        var (diagnostics, generated) = GeneratorProbe.Run(new VeloxJson(), AConstrainedTypeParameterOnARootShapedGeneric, "Probe");

        Assert.IsFalse(diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(diagnostics));

        StringAssert.Contains(generated, "\"Probe.Dog, Probe\"",
            "T is constrained to Animal, so a value of T can be a Dog");
    }

    [TestMethod]
    public void ATypeParameterWithNoResolvableConstraint_IsReported()
        => AssertReports("VELOX_JSON_GENERIC001", ATypeParameterWithNoResolvableConstraint);

    [TestMethod]
    public void AKeyTypeThatIsAnInterface_TakesItsImplementationsIn()
    {
        var (diagnostics, generated) = GeneratorProbe.Run(new VeloxJson(), AnInterfaceKeyedMap, "Probe");

        Assert.IsFalse(diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(diagnostics));

        StringAssert.Contains(generated, "\"Probe.Slot, Probe\"",
            "the key is written as a reference id, so what that id resolves to needs an entry");
    }

    [TestMethod]
    public void OnlyTheTypesNoDeclarationNames_AreReportedAsInformation()
    {
        var (diagnostics, _) = GeneratorProbe.Run(new VeloxJson(), APolymorphicMemberDeclaredAsAConcreteBase, "Probe");
        var listed = diagnostics.Where(static d => d.Id == "VELOX_JSON_INCLUDE001").ToList();

        Assert.AreEqual(1, listed.Count,
            "only Dog is reached without being named; Model is a root and Animal is the member's declared type: "
                + GeneratorProbe.Describe(diagnostics));
        Assert.AreEqual(DiagnosticSeverity.Info, listed[0].Severity,
            "it reports what was taken in rather than complaining about it");
        StringAssert.Contains(listed[0].GetMessage(), "Probe.Dog");
        StringAssert.Contains(listed[0].GetMessage(), "derives from or implements");
    }

    [TestMethod]
    public void TheGeneratedFileHeader_ListsEveryTypeItCanWrite()
    {
        var (diagnostics, generated) = GeneratorProbe.Run(new VeloxJson(), APolymorphicMemberDeclaredAsAConcreteBase, "Probe");

        Assert.IsFalse(diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(diagnostics));

        StringAssert.Contains(generated, "//   Probe.Model",
            "the file is the full list, so the types the build stays quiet about are listed here");
        StringAssert.Contains(generated, "// * Probe.Dog",
            "(*) marks the ones reached without being named by a declaration");
    }

    private static void AssertReports(string id, string source)
    {
        var (diagnostics, _) = GeneratorProbe.Run(new VeloxJson(), source, "Probe");

        Assert.IsTrue(diagnostics.Any(d => d.Id == id), GeneratorProbe.Describe(diagnostics));
    }
}
