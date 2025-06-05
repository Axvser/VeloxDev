
namespace VeloxDev.WPF.StructuralDesign.Theme
{
    public interface IThemeApplied
    {
        public bool IsThemeChanging { get; set; }
        public Type? CurrentTheme { get; set; }
        public void RunThemeChanging(Type? oldTheme, Type newTheme);
        public void RunThemeChanged(Type? oldTheme, Type newTheme);
    }
}
