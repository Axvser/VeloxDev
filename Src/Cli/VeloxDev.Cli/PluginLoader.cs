using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace VeloxDev.Cli;

/// <summary>One loaded assembly whose VeloxDev reference differs from the host's loaded version.</summary>
internal sealed record PluginVersionMismatch(
    string Assembly,
    string Reference,
    Version? Wanted,
    Version? Loaded,
    bool MajorMismatch);

/// <summary>The plugin version picture: exact, unified by simple name, or incompatible at the major level.</summary>
internal sealed record PluginCompatibility(
    string Status,
    IReadOnlyList<PluginVersionMismatch> Mismatches);

/// <summary>
/// Loads workflow component assemblies into the default load context and resolves the assemblies and native
/// libraries they reference.
/// <para>
/// Loading is code execution, not a file read: module initializers run and the library's serializer registry is
/// populated by generated registration code inside those assemblies. That is why this class forces the module
/// initializer instead of relying on the runtime to run it lazily, and why every path it accepts is explicit.
/// </para>
/// </summary>
internal sealed class PluginLoader
{
    private readonly object _gate = new();
    private readonly List<AssemblyDependencyResolver> _resolvers = [];
    private readonly List<string> _directories = [];
    private readonly List<string> _loaded = [];
    private readonly List<string> _warnings = [];
    private readonly List<PluginVersionMismatch> _mismatches = [];
    private readonly string? _stagingRootOverride;
    private string? _stagingDirectory;
    private int _nativeCopyCounter;
    private bool _hooked;

    /// <summary>Creates a loader that stages copies beside the CLI executable.</summary>
    public PluginLoader() : this(null)
    {
    }

    /// <summary>Creates a loader with an explicit staging root (tests and embedders).</summary>
    internal PluginLoader(string? stagingRootOverride)
    {
        _stagingRootOverride = stagingRootOverride;
    }

    /// <summary>Whether resolved assemblies are copied before they are loaded.</summary>
    public bool CopyAssemblies { get; set; } = true;

    /// <summary>
    /// The per-process directory holding owned copies of loaded assemblies, or <c>null</c> when no assembly has
    /// been copied yet. Loading from here is what keeps the source build output unlocked.
    /// </summary>
    public string? StagingDirectory
    {
        get { lock (_gate) return _stagingDirectory; }
    }

    /// <summary>The per-process task directory, created on first use.</summary>
    internal string EnsureTaskDirectory() => EnsureStagingDirectory();

    /// <summary>The per-process task directory, or <c>null</c> before anything needed it.</summary>
    public string? TaskDirectory => StagingDirectory;

    /// <summary>Refuses a major-version mismatch instead of reporting it as a warning.</summary>
    public bool StrictVersions { get; set; }

    /// <summary>The explicit assembly files loaded so far, in load order.</summary>
    public IReadOnlyList<string> LoadedPaths
    {
        get { lock (_gate) return [.. _loaded]; }
    }

    /// <summary>
    /// Version mismatches found in loaded plugins. The default load context unifies by simple name, so a plugin
    /// built against another VeloxDev.Core version runs against this process's copy regardless — worth saying out
    /// loud rather than debugging later.
    /// </summary>
    public IReadOnlyList<string> Warnings
    {
        get { lock (_gate) return [.. _warnings]; }
    }

    /// <summary>
    /// The structured version picture: <c>exact</c> when every reference matches, <c>unified</c> when only
    /// minor/patch differ, <c>incompatible</c> when a major version differs.
    /// </summary>
    public PluginCompatibility Compatibility
    {
        get
        {
            lock (_gate)
            {
                var status = _mismatches.Count == 0
                    ? "exact"
                    : _mismatches.Any(mismatch => mismatch.MajorMismatch)
                        ? "incompatible"
                        : "unified";
                return new PluginCompatibility(status, [.. _mismatches]);
            }
        }
    }

    /// <summary>Loads one assembly and runs its module initializer.</summary>
    /// <param name="path">An assembly file. Relative paths resolve against the current directory.</param>
    /// <returns>The loaded assembly.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="BadImageFormatException">The file is not a .NET assembly.</exception>
    /// <exception cref="InvalidOperationException">The module initializer failed.</exception>
    public Assembly Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An assembly path is required.", nameof(path));

        var full = System.IO.Path.GetFullPath(path);

        lock (_gate)
        {
            if (!File.Exists(full))
                throw new FileNotFoundException($"The dependency assembly was not found: {full}", full);

            Hook();

            var directory = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
                AddDirectoryUnsafe(directory);

            TryAddResolverUnsafe(full);

            // The default context unifies by simple name. A manifest and --assembly can legitimately name the
            // same library by two paths; loading the second copy is impossible, so reuse the resident one.
            var simpleName = AssemblyName.GetAssemblyName(full).Name;
            var resident = FindLoadedByNameUnsafe(simpleName);

            Assembly assembly;
            if (resident is not null)
            {
                assembly = resident;
            }
            else
            {
                var loadPath = PrepareCopy(full);
                try
                {
                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(loadPath);
                }
                catch (FileLoadException) when (FindLoadedByNameUnsafe(simpleName) is { } raced)
                {
                    // Another thread won the load between the probe above and this call.
                    assembly = raced;
                }
            }

            try
            {
                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"The module initializer of '{full}' failed. The archive's dependency could not register its types.", ex);
            }

            if (!_loaded.Contains(full, StringComparer.OrdinalIgnoreCase))
                _loaded.Add(full);

            CheckVersionsUnsafe(assembly, full);
            return assembly;
        }
    }

    private void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        AssemblyLoadContext.Default.Resolving += OnResolving;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += OnResolvingUnmanaged;
    }

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        lock (_gate)
        {
            foreach (var resolver in _resolvers)
            {
                var path = resolver.ResolveAssemblyToPath(name);
                if (path is not null && File.Exists(path))
                    return LoadResolved(context, path);
            }

            if (name.Name is { Length: > 0 } simpleName)
            {
                foreach (var directory in _directories)
                {
                    var candidate = System.IO.Path.Combine(directory, simpleName + ".dll");
                    if (File.Exists(candidate))
                        return LoadResolved(context, candidate);
                }
            }
        }

        return null;
    }

    private IntPtr OnResolvingUnmanaged(Assembly assembly, string name)
    {
        lock (_gate)
        {
            foreach (var resolver in _resolvers)
            {
                var path = resolver.ResolveUnmanagedDllToPath(name);
                if (path is not null && File.Exists(path))
                    return NativeLibrary.Load(PrepareNativeCopy(path));
            }
        }

        return IntPtr.Zero;
    }

    private Assembly LoadResolved(AssemblyLoadContext context, string sourcePath)
    {
        var simpleName = AssemblyName.GetAssemblyName(sourcePath).Name;
        if (FindLoadedByNameUnsafe(simpleName) is { } resident) return resident;
        return context.LoadFromAssemblyPath(PrepareCopy(sourcePath));
    }

    // 副本的目的只有一个：源 bin 里的 DLL 不被加载句柄锁住，项目可以继续构建。
    internal string PrepareCopy(string sourcePath)
    {
        if (!CopyAssemblies) return sourcePath;

        var staging = EnsureStagingDirectory();
        var target = System.IO.Path.Combine(staging, System.IO.Path.GetFileName(sourcePath));
        if (!File.Exists(target))
            File.Copy(sourcePath, target, overwrite: false);
        return target;
    }

    private string PrepareNativeCopy(string sourcePath)
    {
        if (!CopyAssemblies) return sourcePath;

        var staging = System.IO.Path.Combine(EnsureStagingDirectory(), "native");
        Directory.CreateDirectory(staging);
        var target = System.IO.Path.Combine(
            staging,
            $"{Interlocked.Increment(ref _nativeCopyCounter)}_{System.IO.Path.GetFileName(sourcePath)}");
        File.Copy(sourcePath, target, overwrite: false);
        return target;
    }

    // 每个进程一个 tasks/{任务ID}_{时间戳}/；tasks/.lock 只包住建目录这一步。
    // 目录名已带 PID 与随机段，锁拿不到时继续用一个唯一目录，不让加载失败。
    private string EnsureStagingDirectory()
    {
        if (_stagingDirectory is not null) return _stagingDirectory;

        var baseRoot = ResolveStagingRoot();
        var name = $"p{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}_" +
                   $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var lockPath = System.IO.Path.Combine(baseRoot, ".lock");

        try
        {
            using var gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var directory = System.IO.Path.Combine(baseRoot, name);
            Directory.CreateDirectory(directory);
            _stagingDirectory = directory;
            return directory;
        }
        catch (IOException)
        {
            var directory = System.IO.Path.Combine(baseRoot, name + "-independent");
            Directory.CreateDirectory(directory);
            _stagingDirectory = directory;
            return directory;
        }
    }

    private string ResolveStagingRoot()
    {
        if (!string.IsNullOrWhiteSpace(_stagingRootOverride))
        {
            Directory.CreateDirectory(_stagingRootOverride);
            return _stagingRootOverride;
        }

        var beside = System.IO.Path.Combine(AppContext.BaseDirectory, "tasks");
        if (TryPrepareRoot(beside)) return beside;

        var local = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VeloxDev", "tasks");
        if (TryPrepareRoot(local)) return local;

        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "veloxdev-tasks");
        Directory.CreateDirectory(temp);
        return temp;
    }

    private static bool TryPrepareRoot(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = System.IO.Path.Combine(path, $".probe-{Guid.NewGuid():N}");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void CheckVersionsUnsafe(Assembly assembly, string path)
    {
        var core = typeof(VeloxDev.WorkflowSystem.IWorkflowTreeViewModel).Assembly.GetName();
        var extension = typeof(VeloxDev.AI.Workflow.WorkflowAgentScope).Assembly.GetName();

        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            var host = reference.Name switch
            {
                "VeloxDev.Core" => core,
                "VeloxDev.Core.Extension" => extension,
                _ => null,
            };

            if (host?.Version is not { } loaded || reference.Version is not { } wanted) continue;
            if (wanted == loaded) continue;

            var file = System.IO.Path.GetFileName(path);
            var majorMismatch = wanted.Major != loaded.Major;
            if (!_mismatches.Any(mismatch =>
                    string.Equals(mismatch.Assembly, file, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(mismatch.Reference, reference.Name, StringComparison.Ordinal) &&
                    mismatch.Wanted == wanted && mismatch.Loaded == loaded))
            {
                _mismatches.Add(new PluginVersionMismatch(file, reference.Name!, wanted, loaded, majorMismatch));
            }

            var warning = $"'{file}' references {reference.Name} {wanted}, " +
                          $"but this process loaded {loaded}. The default load context unifies by simple name, " +
                          "so the plugin runs against the host version — check the API it was built with.";
            if (!_warnings.Contains(warning, StringComparer.Ordinal)) _warnings.Add(warning);

            if (StrictVersions && majorMismatch)
            {
                throw new InvalidOperationException(
                    $"'{file}' references {reference.Name} {wanted}, but this process loaded {loaded}. " +
                    "A major-version mismatch is refused under --strict-plugin-versions; " +
                    "rebuild the plugin against the host version or drop the flag.");
            }
        }
    }

    private void AddDirectoryUnsafe(string directory)
    {
        if (!_directories.Contains(directory, StringComparer.OrdinalIgnoreCase))
            _directories.Add(directory);
    }

    private void TryAddResolverUnsafe(string assemblyPath)
    {
        try
        {
            _resolvers.Add(new AssemblyDependencyResolver(assemblyPath));
        }
        catch (InvalidOperationException)
        {
            // No .deps.json beside the assembly. The directory probe above is the fallback, and an assembly
            // with no manifest of its own still works when its dependencies sit next to it.
        }
    }

    private static Assembly? FindLoadedByNameUnsafe(string? simpleName)
        => simpleName is null or ""
            ? null
            : AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
                !a.IsDynamic
                && string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
}
