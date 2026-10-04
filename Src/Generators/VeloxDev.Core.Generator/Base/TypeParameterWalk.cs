using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace VeloxDev.Generators.Base
{
    // 递归收集一个类型里出现的全部类型参数 —— 数组 / 指针 / 泛型实参都要下钻。
    // 两个生成器共用：命令侧要把方法的类型参数与约束原样搬到转换后的签名上，归档侧要沿类型参数的约束
    // 判断它可能装着哪一族类型。抄一份的话，两边的下钻规则会各自漂走。
    internal static class TypeParameterWalk
    {
        internal static IEnumerable<ITypeParameterSymbol> Collect(ITypeSymbol type)
        {
            switch (type)
            {
                case ITypeParameterSymbol typeParameter:
                    yield return typeParameter;
                    break;
                case IArrayTypeSymbol array:
                    foreach (var element in Collect(array.ElementType)) yield return element;
                    break;
                case IPointerTypeSymbol pointer:
                    foreach (var pointed in Collect(pointer.PointedAtType)) yield return pointed;
                    break;
                case INamedTypeSymbol named:
                    foreach (var argument in named.TypeArguments)
                        foreach (var nested in Collect(argument)) yield return nested;
                    break;
            }
        }
    }
}
