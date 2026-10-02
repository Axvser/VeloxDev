#if !NET5_0_OR_GREATER
// Polyfill for [ModuleInitializer] on target frameworks that predate
// System.Runtime.CompilerServices.ModuleInitializerAttribute (available in the BCL since .NET 5).
// Required because VeloxDev.Core multi-targets netstandard2.0 / netframework4.6.1 / netcoreapp3.0,
// and the generated AIContextTree fragment registers itself from a module initializer.
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Marks a method the runtime calls when the module is loaded.
    /// </summary>
    /// <remarks>
    /// Public, not internal: generated fragments in consuming assemblies also mark their registration method
    /// with this attribute, and a consumer that targets one of the legacy frameworks resolves the name here
    /// rather than emitting a second polyfill of its own.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class ModuleInitializerAttribute : Attribute
    {
    }
}
#endif
