using System;
using System.Threading;

namespace VeloxDev.Docs.ViewModels;

/// <summary>
/// Tracks whether the current logical flow is materializing view-models from a
/// persisted form (e.g. JSON deserialization). Property setters whose change
/// notifications would otherwise trigger expensive side effects (syntax
/// highlighting, image downloads, document reloads) consult this flag and
/// defer that work until hydration completes.
/// </summary>
internal static class HydrationScope
{
    private static readonly AsyncLocal<int> _depth = new();

    public static bool IsActive => _depth.Value > 0;

    public static IDisposable Enter()
    {
        _depth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_depth.Value > 0)
                _depth.Value--;
        }
    }
}
