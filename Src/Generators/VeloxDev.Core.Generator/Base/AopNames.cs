using Microsoft.CodeAnalysis;

namespace VeloxDev.Generators.Base
{
    // AOP 名字的唯一算法。
    //
    // 此前接口名在三处各算一遍：AopInterface.cs 用内联 Replace('.', '_')，AopWriter 用
    // WriterBase.NamespaceFileSegment()。两者只在真实命名空间下一致 —— 全局命名空间时前者会产出
    // "<global namespace>"，尖括号与空格都是非法的标识符字符，生成器会整个抛出去而宿主只报 CS8785。
    // 代理实现类必须与接口同名同命名空间，所以这一处只能有一个算法。
    internal static class AopNames
    {
        internal const string InterfaceNamespace = "VeloxDev.AopInterfaces";

        internal static string InterfaceFor(INamedTypeSymbol symbol)
            => $"{symbol.Name}_{Segment(symbol)}_Aop";

        internal static string ProxyFor(INamedTypeSymbol symbol)
            => InterfaceFor(symbol) + "Proxy";

        internal static string Segment(INamedTypeSymbol symbol)
            => symbol.ContainingNamespace is null || symbol.ContainingNamespace.IsGlobalNamespace
                ? "Global"
                : symbol.ContainingNamespace.ToDisplayString().Replace('.', '_');
    }
}
