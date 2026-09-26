#if !NET5_0_OR_GREATER
using System;

namespace System.Diagnostics.CodeAnalysis
{
    // netstandard2.0 的引用程序集里没有这个特性，而编译器是按「全名 + 构造函数签名」识别它的 ——
    // 与 IsExternalInit 同机制，所以本程序集内自带一份就够了，不需要额外依赖。
    // internal：只服务本程序集内的可空性流分析，不外泄给消费者。
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        public bool ReturnValue { get; }
    }
}
#endif
