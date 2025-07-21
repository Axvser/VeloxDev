using System.ComponentModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed class NodeContext : IContext
    {
        public event PropertyChangingEventHandler? PropertyChanging;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanging(string propertyName)
        {
            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
        }
        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private Anchor anchor = Anchor.Default;
        private IContextTree? tree = null;
        private bool isEnabled = true;
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
        public Anchor Anchor
        {
            get => anchor;
            set
            {
                if (Equals(anchor, value)) return;
                OnPropertyChanging(nameof(Anchor));
                anchor = value;
                OnPropertyChanged(nameof(Anchor));
            }
        }
        public IContextTree? Tree
        {
            get => tree;
            set
            {
                if (Equals(tree, value)) return;
                OnPropertyChanging(nameof(Tree));
                tree = value;
                OnPropertyChanged(nameof(Tree));
            }
        }

        private Interfaces.MVVM.IVeloxCommand? _buffer_ConnectCommand = null;
        public Interfaces.MVVM.IVeloxCommand ConnectCommand
        {
            get
            {
                _buffer_ConnectCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                    executeAsync: Connect,
                    canExecute: _ => true);
                return _buffer_ConnectCommand;
            }
        }
        public Task Connect(object? parameter, CancellationToken ct)
        {
            if (tree != null)
            {
                if (tree.VirtualConnector.Start is null)
                {
                    tree.VirtualConnector.Start = this;
                }
                else
                {
                    if (tree.VirtualConnector.Start == this)
                    {
                        tree.VirtualConnector.Start = null;
                    }
                    else
                    {
                        tree.Connectors.Add(new ConnectorContext()
                        {
                            Start = tree.VirtualConnector.Start,
                            End = this
                        });
                        tree.VirtualConnector.Start = null;
                    }
                }
            }
            return Task.CompletedTask;
        }
    }
}
