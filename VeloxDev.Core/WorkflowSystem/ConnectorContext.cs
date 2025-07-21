using System.ComponentModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed class ConnectorContext : IContextConnector
    {
        public event PropertyChangingEventHandler? PropertyChanging;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanging(string propertyName)
        {
            PropertyChanging?.Invoke(this, new global::System.ComponentModel.PropertyChangingEventArgs(propertyName));
        }
        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        private bool isEnabled = true;
        private IContext? start = null;
        private IContext? end = null;
        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                if (Equals(isEnabled, value)) return;
                OnPropertyChanging(nameof(IsEnabled));
                isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
        public IContext? Start
        {
            get => start;
            set
            {
                if (Equals(start, value)) return;
                OnPropertyChanging(nameof(Start));
                start = value;
                IsEnabled = start != null && end != null;
                OnPropertyChanged(nameof(Start));
            }
        }
        public IContext? End
        {
            get => end;
            set
            {
                if (Equals(end, value)) return;
                OnPropertyChanging(nameof(End));
                end = value;
                IsEnabled = end != null && start != null;
                OnPropertyChanged(nameof(End));
            }
        }
    }
}
