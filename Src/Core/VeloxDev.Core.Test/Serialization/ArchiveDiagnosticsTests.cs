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

    private static void AssertReports(string id, string source)
    {
        var (diagnostics, _) = GeneratorProbe.Run(new VeloxJson(), source, "Probe");

        Assert.IsTrue(diagnostics.Any(d => d.Id == id), GeneratorProbe.Describe(diagnostics));
    }
}
