using System.Text.Json;

namespace VeloxDev.Cli;

/// <summary>What to load, independent of where the request came from (startup options or the LoadArchive tool).</summary>
internal sealed record ArchiveRequest(
    string Path,
    string? Manifest = null,
    IReadOnlyList<string>? Assemblies = null,
    string? RootType = null);

/// <summary>
/// A workflow document plus the dependency list that has to be loaded before it can be read.
/// <para>
/// The manifest is a sidecar rather than a wrapper around the document: the graph JSON stays exactly what the
/// library's serializer produced, so an archive can be produced or consumed by anything that already speaks the
/// format. The sidecar only answers "which assemblies must be present first".
/// </para>
/// </summary>
internal sealed class WorkflowArchive
{
    public const string ManifestSuffix = ".manifest.json";

    private WorkflowArchive(
        string path,
        string json,
        string? manifestPath,
        IReadOnlyList<string> assemblies,
        bool manifestAllowsExecution,
        string? manifestRootType)
    {
        Path = path;
        Json = json;
        ManifestPath = manifestPath;
        Assemblies = assemblies;
        ManifestAllowsExecution = manifestAllowsExecution;
        ManifestRootType = manifestRootType;
    }

    /// <summary>The full path of the workflow document.</summary>
    public string Path { get; }

    /// <summary>The document text, exactly as it was read.</summary>
    public string Json { get; }

    /// <summary>The sidecar that supplied <see cref="Assemblies"/>, or <c>null</c> when there was none.</summary>
    public string? ManifestPath { get; }

    /// <summary>Dependency assembly paths, absolute and already checked to stay inside the manifest's directory.</summary>
    public IReadOnlyList<string> Assemblies { get; }

    /// <summary>Whether the manifest itself asks for node execution to be exposed.</summary>
    public bool ManifestAllowsExecution { get; }

    /// <summary>
    /// The concrete tree type the document was serialized from, when the archive carries one.
    /// <para>
    /// The serializer writes <c>$type</c> only when the runtime type differs from the declared one. Serializing a
    /// derived <c>TreeViewModel</c> through its own type therefore writes no root type marker at all, and a reader
    /// that assumes <c>TreeDefaultViewModel</c> silently loses the derived tree's behavior. This field is the
    /// missing half of the document.
    /// </para>
    /// </summary>
    public string? ManifestRootType { get; }

    public static WorkflowArchive Load(string archivePath, string? manifestPath = null)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("An archive path is required.", nameof(archivePath));

        var fullArchive = System.IO.Path.GetFullPath(archivePath);
        if (!File.Exists(fullArchive))
            throw new FileNotFoundException($"The workflow archive was not found: {fullArchive}", fullArchive);

        var json = File.ReadAllText(fullArchive);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException($"The workflow archive is empty: {fullArchive}");

        var manifest = ResolveManifest(fullArchive, manifestPath);
        if (manifest is null)
            return new WorkflowArchive(fullArchive, json, null, [], false, null);

        var (assemblies, allow, rootType) = ReadManifest(manifest);
        return new WorkflowArchive(fullArchive, json, manifest, assemblies, allow, rootType);
    }

    private static string? ResolveManifest(string archivePath, string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var full = System.IO.Path.GetFullPath(explicitPath);
            if (!File.Exists(full))
                throw new FileNotFoundException($"The archive manifest was not found: {full}", full);
            return full;
        }

        var sidecar = archivePath + ManifestSuffix;
        return File.Exists(sidecar) ? sidecar : null;
    }

    private static (IReadOnlyList<string> Assemblies, bool AllowExecution, string? RootType) ReadManifest(string manifestPath)
    {
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(manifestPath), options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The archive manifest is not valid JSON: {manifestPath}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"The archive manifest must be a JSON object: {manifestPath}");

            var directory = System.IO.Path.GetDirectoryName(manifestPath)
                ?? throw new InvalidDataException($"The archive manifest has no directory: {manifestPath}");

            var assemblies = new List<string>();
            if (root.TryGetProperty("assemblies", out var list))
            {
                if (list.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"'assemblies' must be an array: {manifestPath}");

                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException($"Every 'assemblies' entry must be a string: {manifestPath}");

                    var relative = item.GetString();
                    if (string.IsNullOrWhiteSpace(relative))
                        throw new InvalidDataException($"An 'assemblies' entry is empty: {manifestPath}");

                    assemblies.Add(ResolveInside(directory, relative, manifestPath));
                }
            }

            var allow = root.TryGetProperty("allowExecution", out var flag)
                && flag.ValueKind is JsonValueKind.True;

            string? rootType = null;
            if (root.TryGetProperty("rootType", out var rootTypeValue))
            {
                if (rootTypeValue.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException($"'rootType' must be a string: {manifestPath}");

                rootType = rootTypeValue.GetString();
                if (string.IsNullOrWhiteSpace(rootType))
                    throw new InvalidDataException($"'rootType' is empty: {manifestPath}");
            }

            return (assemblies, allow, rootType);
        }
    }

    /// <summary>
    /// Resolves a manifest-relative path and refuses anything that leaves the manifest's directory. An archive is
    /// shared input; a manifest that can name <c>../../Windows/...</c> is a way to make the archive decide what
    /// this process loads.
    /// </summary>
    private static string ResolveInside(string directory, string relative, string manifestPath)
    {
        var fullDirectory = System.IO.Path.GetFullPath(directory);
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullDirectory, relative));

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var prefix = fullDirectory.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + System.IO.Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, comparison))
            throw new InvalidDataException(
                $"Assembly '{relative}' escapes the archive directory ({manifestPath}). Manifest dependencies must stay inside it.");

        return full;
    }
}
