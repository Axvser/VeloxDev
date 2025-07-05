using VeloxDev.Core.ThemeSystem;

namespace VeloxDev.Core.Interfaces.Theme
{
    public interface INotifyThemeChanged
    {
        public event ThemeChangedEventHandler? ThemeChanged;
    }
}
