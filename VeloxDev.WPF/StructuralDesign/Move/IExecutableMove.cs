using System.Windows;

namespace VeloxDev.WPF.StructuralDesign.Move
{
    public interface IExecutableMove
    {
        public void Start(FrameworkElement target);
        public void Stop(FrameworkElement target);
    }
}
