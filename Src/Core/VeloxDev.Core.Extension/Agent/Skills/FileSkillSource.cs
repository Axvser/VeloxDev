using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VeloxDev.AI;

namespace VeloxDev.AI.Skills;

/// <summary>
/// Discovers skills on disk as <c>&lt;root&gt;/&lt;name&gt;/SKILL.md</c>, following the Agent Skills
/// convention. A <c>references/</c> tree beside the <c>SKILL.md</c> ships extra documents that are
/// counted at discovery but only read when the model asks for one.
/// <para>
/// Layouts understood: a set of skill folders under a shared root (the usual case, and the one the
/// directory-name check applies to), or a single <c>SKILL.md</c> sitting directly in the root.
/// </para>
/// <para>
/// File skills are language-neutral — the convention has no language dimension — so the
/// <see cref="AgentLanguages"/> argument of the read methods is accepted for interface uniformity and
/// ignored.
/// </para>
/// </summary>
public sealed class FileSkillSource : ISkillSource
{
    private const string SkillFileName = "SKILL.md";

    /// <summary>Extensions counted as bundled resources — the Agent Framework's default set.</summary>
    private static readonly HashSet<string> ResourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".json", ".yaml", ".yml", ".csv", ".xml", ".txt",
    };

    private readonly string _root;
    private readonly Dictionary<string, string> _skillDirs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a source over <paramref name="rootPath"/>. Discovery is deferred to <see cref="Discover"/>.</summary>
    /// <param name="rootPath">Directory that contains the skill folders. Need not exist yet.</param>
    public FileSkillSource(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("A skill root path is required.", nameof(rootPath));
        _root = Path.GetFullPath(rootPath);
    }

    /// <inheritdoc />
    public SkillSourceKind Kind => SkillSourceKind.File;

    /// <summary>The resolved root directory this source reads from.</summary>
    public string Root => _root;

    /// <inheritdoc />
    public IReadOnlyList<SkillDescriptor> Discover()
    {
        _skillDirs.Clear();
        var found = new List<SkillDescriptor>();

        if (!Directory.Exists(_root)) return found;

        foreach (var file in EnumerateSkillFiles())
        {
            var directory = Path.GetDirectoryName(file)!;
            var folderName = new DirectoryInfo(directory).Name;

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                found.Add(new SkillDescriptor
                {
                    Name = folderName,
                    Source = SkillSourceKind.File,
                    Path = file,
                    Error = $"Cannot read SKILL.md: {ex.Message}",
                });
                continue;
            }

            if (!SkillFrontmatter.TryParse(text, out var name, out var description, out _, out var error))
            {
                found.Add(new SkillDescriptor
                {
                    // Fall back to the folder name so the broken skill is still identifiable and reported.
                    Name = name ?? folderName,
                    Source = SkillSourceKind.File,
                    Path = file,
                    Error = error,
                });
                continue;
            }

            // The convention is folder-per-skill. A SKILL.md sitting directly in the root is the
            // single-skill layout, where the root's own name carries no meaning to compare against.
            if (!PathsEqual(directory, _root) && !string.Equals(folderName, name, StringComparison.Ordinal))
            {
                found.Add(new SkillDescriptor
                {
                    Name = name!,
                    Description = description!,
                    Source = SkillSourceKind.File,
                    Path = file,
                    Error = $"Skill name '{name}' does not match its parent directory name '{folderName}'. " +
                            "Rename the directory to match the frontmatter, or fix the frontmatter.",
                });
                continue;
            }

            _skillDirs[name!] = directory;
            found.Add(new SkillDescriptor
            {
                Name = name!,
                Description = description!,
                Source = SkillSourceKind.File,
                Path = file,
                ResourceCount = CountResources(directory),
            });
        }

        return found;
    }

    /// <inheritdoc />
    public string? ReadBody(string skillName, AgentLanguages language)
    {
        if (!TryResolveSkillDir(skillName, out var directory)) return null;

        var file = Path.Combine(directory, SkillFileName);
        if (!File.Exists(file)) return null;

        var text = File.ReadAllText(file);
        // Always take the parsed body rather than the raw text. The parse strips the frontmatter block
        // whenever it is present, even if the header itself is invalid — and the advertisement already
        // carries name and description, so re-sending the header would both duplicate it and leak raw
        // YAML into the prompt.
        SkillFrontmatter.TryParse(text, out _, out _, out var body, out _);
        return body;
    }

    /// <inheritdoc />
    public string? ReadResource(string skillName, string relativePath, AgentLanguages language)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        if (!TryResolveSkillDir(skillName, out var directory)) return null;

        // Containment check: a resource path comes from the model, so it must not be able to walk out of
        // the skill directory with '..' or a rooted path.
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(directory, relativePath));
        }
        catch (Exception)
        {
            return null;
        }

        var root = Path.GetFullPath(directory);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;

        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    private bool TryResolveSkillDir(string skillName, out string directory)
    {
        // A read may arrive without a preceding Discover on this instance (the scope rediscovers, but a
        // tool can be invoked against a freshly constructed source), so fall back to a scan.
        if (_skillDirs.Count == 0) Discover();
        return _skillDirs.TryGetValue(skillName, out directory!);
    }

    /// <summary>
    /// The two layouts the convention uses, shallowest first: a single <c>SKILL.md</c> directly in the
    /// root, and the usual one-folder-per-skill. Nothing deeper is scanned, matching the Agent
    /// Framework's default search depth.
    /// </summary>
    private IEnumerable<string> EnumerateSkillFiles()
    {
        var direct = Path.Combine(_root, SkillFileName);
        if (File.Exists(direct)) yield return direct;

        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            var nested = Path.Combine(dir, SkillFileName);
            if (File.Exists(nested)) yield return nested;
        }
    }

    private static int CountResources(string skillDirectory)
    {
        try
        {
            return Directory.EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories)
                .Count(f => !string.Equals(Path.GetFileName(f), SkillFileName, StringComparison.OrdinalIgnoreCase)
                            && ResourceExtensions.Contains(Path.GetExtension(f)));
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
