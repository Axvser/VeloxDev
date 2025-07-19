using System.ComponentModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel
{
    public interface IContext : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public bool IsEnabled { get; set; }
        public Anchor Anchor { get; set; }
        public IContextTree? Tree { get; set; }

        public IVeloxCommand MoveCommand { get; }
        public IVeloxCommand BroadcastCommand { get; }
        public IVeloxCommand DeleteCommand { get; }
        public IVeloxCommand BeginConnectCommand { get; }
        public IVeloxCommand FinishConnectCommand { get; }
    }
}
