using System.ComponentModel;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Cli.Test;

/// <summary>Creates a throwaway archive directory and the minimal trees the CLI tests operate on.</summary>
internal sealed class TestArchive : IDisposable
{
    public TestArchive()
    {
        Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "veloxdev-cli-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public string Path => System.IO.Path.Combine(Directory, "graph.json");

    /// <remarks>
    /// The type parameter is deliberately concrete: serializing through a base or interface reference makes the
    /// writer emit a root <c>$type</c>, while a host that calls <c>treeViewModel.Serialize()</c> does not.
    /// </remarks>
    public string WriteTree<T>(T tree) where T : IWorkflowTreeViewModel, INotifyPropertyChanged
    {
        var json = tree.Serialize();
        File.WriteAllText(Path, json);
        return Path;
    }

    public string WriteManifest(string json)
    {
        var path = Path + WorkflowArchive.ManifestSuffix;
        File.WriteAllText(path, json);
        return path;
    }

    public string WriteRaw(string json)
    {
        File.WriteAllText(Path, json);
        return Path;
    }

    public static TreeDefaultViewModel TreeWithOneNode(out NodeDefaultViewModel node)
    {
        var tree = new TreeDefaultViewModel();
        node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        return tree;
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Directory, recursive: true); }
        catch { /* a test must not fail on temp cleanup */ }
    }
}
