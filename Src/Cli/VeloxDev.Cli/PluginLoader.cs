using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace VeloxDev.Cli;

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
    private bool _hooked;

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
                try
                {
                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(full);
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
                    return context.LoadFromAssemblyPath(path);
            }

            if (name.Name is { Length: > 0 } simpleName)
            {
                foreach (var directory in _directories)
                {
                    var candidate = System.IO.Path.Combine(directory, simpleName + ".dll");
                    if (File.Exists(candidate))
                        return context.LoadFromAssemblyPath(candidate);
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
                    return NativeLibrary.Load(path);
            }
        }

        return IntPtr.Zero;
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

            var warning = $"'{System.IO.Path.GetFileName(path)}' references {reference.Name} {wanted}, " +
                          $"but this process loaded {loaded}. The default load context unifies by simple name, " +
                          "so the plugin runs against the host version — check the API it was built with.";
            if (!_warnings.Contains(warning, StringComparer.Ordinal)) _warnings.Add(warning);
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
