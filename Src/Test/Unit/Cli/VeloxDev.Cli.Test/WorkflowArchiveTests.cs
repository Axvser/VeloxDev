namespace VeloxDev.Cli.Test;

[TestClass]
public class WorkflowArchiveTests
{
    [TestMethod]
    public void ReadsTheSidecarManifestAndResolvesItsAssemblies()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteRaw("{}");
        fixture.WriteManifest("""
            {
              // comments and trailing commas are accepted
              "assemblies": [ "lib/Nodes.dll", "lib/More.dll", ],
              "allowExecution": true,
              "rootType": "Demo.ViewModels.TreeViewModel",
            }
            """);

        var archive = WorkflowArchive.Load(path);

        Assert.AreEqual(path + WorkflowArchive.ManifestSuffix, archive.ManifestPath);
        Assert.IsTrue(archive.ManifestAllowsExecution);
        Assert.AreEqual("Demo.ViewModels.TreeViewModel", archive.ManifestRootType);
        Assert.AreEqual(2, archive.Assemblies.Count);
        StringAssert.EndsWith(archive.Assemblies[0], Path.Combine("lib", "Nodes.dll"));
        StringAssert.EndsWith(archive.Assemblies[1], Path.Combine("lib", "More.dll"));
    }

    [TestMethod]
    public void AManifestMayNotEscapeTheArchiveDirectory()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteRaw("{}");
        fixture.WriteManifest("""{ "assemblies": [ "../outside.dll" ] }""");

        var error = Assert.ThrowsExactly<InvalidDataException>(() => WorkflowArchive.Load(path));

        StringAssert.Contains(error.Message, "escapes");
    }

    [TestMethod]
    public void AnAbsolutePathOutsideTheDirectoryIsRefusedToo()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteRaw("{}");
        var outside = Path.Combine(Path.GetTempPath(), "definitely-outside.dll");
        fixture.WriteManifest($$"""{ "assemblies": [ "{{outside.Replace("\\", "\\\\")}}" ] }""");

        Assert.ThrowsExactly<InvalidDataException>(() => WorkflowArchive.Load(path));
    }

    [TestMethod]
    public void MissingArchiveIsReportedAsSuch()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

        Assert.ThrowsExactly<FileNotFoundException>(() => WorkflowArchive.Load(missing));
    }

    [TestMethod]
    public void AnEmptyArchiveIsRefused()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteRaw("   ");

        Assert.ThrowsExactly<InvalidDataException>(() => WorkflowArchive.Load(path));
    }

    [TestMethod]
    public void NoSidecarMeansNoDependencies()
    {
        using var fixture = new TestArchive();
        var path = fixture.WriteRaw("{}");

        var archive = WorkflowArchive.Load(path);

        Assert.IsNull(archive.ManifestPath);
        Assert.AreEqual(0, archive.Assemblies.Count);
        Assert.IsFalse(archive.ManifestAllowsExecution);
    }
}
