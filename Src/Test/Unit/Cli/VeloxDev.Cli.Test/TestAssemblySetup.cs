namespace VeloxDev.Cli.Test;

/// <summary>
/// The demo component library installs its agent helper when a demo tree is constructed, and that helper refuses
/// to build without an API key. The CLI tests only need the tree, never a model call, so a placeholder keeps the
/// suite green on a clean machine (and on Linux CI) instead of aborting the test host.
/// </summary>
[TestClass]
public static class TestAssemblySetup
{
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("API_KEY_DEEPSEEK")))
            Environment.SetEnvironmentVariable("API_KEY_DEEPSEEK", "veloxdev-cli-tests");
    }
}
