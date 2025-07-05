using VeloxDev.Core.ThemeSystem;

namespace VeloxDev.Core.Interfaces.Theme
{
    public interface INotifyThemeChanging
    {
        public event ThemeChangingEventHandler? ThemeChanging;
    }
}
