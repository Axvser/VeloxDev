// 适配器仍要编 netframework4.6.1 / netcoreapp3.0：前者缺 record/init 所需的 IsExternalInit 与
// UnconditionalSuppressMessageAttribute；两者都缺 Math.Clamp / double.IsFinite / Environment.TickCount64。
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

namespace VeloxDev.WinForms.Xaml
{
    internal static class XamlMath
    {
        public static int Clamp(int value, int minimum, int maximum)
            => value < minimum ? minimum : value > maximum ? maximum : value;

        public static double Clamp(double value, double minimum, double maximum)
            => value < minimum ? minimum : value > maximum ? maximum : value;

        public static float Clamp(float value, float minimum, float maximum)
            => value < minimum ? minimum : value > maximum ? maximum : value;

        public static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        public static long TickCount64
            => (long)(System.Diagnostics.Stopwatch.GetTimestamp() * 1000L / System.Diagnostics.Stopwatch.Frequency);
    }
}
