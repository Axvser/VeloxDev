#if !NET5_0_OR_GREATER
// Polyfill for C# 9 records / init-only setters on target frameworks that predate
// System.Runtime.CompilerServices.IsExternalInit (available in the BCL since .NET 5).
// Required because VeloxDev.Core multi-targets netstandard2.0 / netframework4.6.1 / netcoreapp3.0
// and hosts record types such as SurfaceViewport.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
