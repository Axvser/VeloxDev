using System.Windows;

namespace VeloxDev.WPF.Tools.Dependency
{
    public static class DependencyPropertyHelperEx
    {
        public static bool IsPropertySetInXaml(DependencyObject obj, DependencyProperty dp)
        {
            var valueSource = DependencyPropertyHelper.GetValueSource(obj, dp);
            return valueSource.BaseValueSource switch
            {
                BaseValueSource.Local => true,
                BaseValueSource.Style => true,
                BaseValueSource.ImplicitStyleReference => true,
                _ => false
            };
        }
    }
}
