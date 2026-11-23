// 适配器仍要编 netframework4.6.1 / netcoreapp3.0：前者缺 record/init 所需的 IsExternalInit 与
// UnconditionalSuppressMessageAttribute。
#if NETFRAMEWORK || NETCOREAPP3_0
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(
        AttributeTargets.All,
        Inherited = false,
        AllowMultiple = false)]
    internal sealed class UnconditionalSuppressMessageAttribute : Attribute
    {
        public UnconditionalSuppressMessageAttribute(string category, string checkId)
        {
            Category = category;
            CheckId = checkId;
        }

        public string Category { get; }

        public string CheckId { get; }

        public string? Scope { get; set; }

        public string? Target { get; set; }

        public string? MessageId { get; set; }

        public string? Justification { get; set; }
    }
}
#endif
