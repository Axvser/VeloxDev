namespace VeloxDev.Cli.Test;

[TestClass]
public class PluginLoaderTests
{
    [TestMethod]
    public void PrepareCopyWritesAnOwnedCopyAndLeavesTheSourceUnlocked()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"veloxdev-loader-source-{Guid.NewGuid():N}");
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"veloxdev-loader-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);

        var assemblySource = Path.Combine(AppContext.BaseDirectory, "VeloxDev.Cli.Test.Copy.dll");
        Assert.IsTrue(File.Exists(assemblySource), assemblySource);

        var source = Path.Combine(sourceRoot, Path.GetFileName(assemblySource));
        File.Copy(assemblySource, source);

        try
        {
            var loader = new PluginLoader(stagingRoot);
            var staged = loader.PrepareCopy(source);

            Assert.IsTrue(File.Exists(staged), staged);
            Assert.IsNotNull(loader.StagingDirectory);
            Assert.IsTrue(
                Path.GetFullPath(staged).StartsWith(Path.GetFullPath(stagingRoot), StringComparison.OrdinalIgnoreCase),
                staged);
            Assert.AreEqual(Path.GetFileName(source), Path.GetFileName(staged));

            // No load handle was opened on the source; a rebuild can replace it immediately.
            File.Delete(source);
            Assert.IsFalse(File.Exists(source));
        }
        finally
        {
            try { Directory.Delete(sourceRoot, recursive: true); }
            catch { /* the staging copy stays for this process */ }
        }
    }

    [TestMethod]
    public void PrepareCopyCanBeDisabled()
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "VeloxDev.Cli.Test.Copy.dll"));
        var loader = new PluginLoader(Path.Combine(Path.GetTempPath(), $"veloxdev-loader-staging-{Guid.NewGuid():N}"))
        {
            CopyAssemblies = false,
        };

        Assert.AreEqual(source, loader.PrepareCopy(source));
        Assert.IsNull(loader.StagingDirectory);
    }
}
