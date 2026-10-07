#if !NET5_0_OR_GREATER
// Polyfill for C# 9 records / init-only setters on target frameworks that predate
// System.Runtime.CompilerServices.IsExternalInit (available in the BCL since .NET 5).
// The compiler recognises the type by full name and constructor signature, so each assembly that declares a
// record needs its own — this project multi-targets netstandard2.0, and Core's copy is internal to Core.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
