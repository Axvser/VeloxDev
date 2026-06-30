using VeloxDev.WorkflowSystem.Compilation;

namespace Demo.ViewModels;

/// <summary>
/// Non-generated static helper providing enum value arrays for Compiler
/// configuration ComboBox bindings across all platforms.
/// Avoids {x:Static} issues with source-generated partial classes
/// on WPF/WinUI/MAUI XAML compilers.
/// </summary>
public static class CompilerConfigOptions
{
    public static CompileMode[] CompileModeValues => [CompileMode.BFS, CompileMode.DFS];
    public static CompileDirection[] CompileDirectionValues => [CompileDirection.Forward, CompileDirection.Reverse];
    public static CompileScope[] CompileScopeValues => [CompileScope.FromNode, CompileScope.Omni];
    public static CycleHandling[] CycleHandlingValues => [CycleHandling.Throw, CycleHandling.Trim, CycleHandling.Allow];
}
