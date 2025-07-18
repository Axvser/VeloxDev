using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.WPF.WorkflowSystem.Views;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.ContextTree]
    public partial class FactoryViewModel : IContextTree
    {
        private ObservableCollection<IContext> children = [];
        private ObservableCollection<IContextConnector> connectors = [];
        private static readonly Dictionary<Type, Type> viewMappings = [];
        static FactoryViewModel()
        {
            viewMappings.Add(typeof(FactoryViewModel), typeof(Factory));
            viewMappings.Add(typeof(ShowerNodeViewModel), typeof(Shower));
        }

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

        public ObservableCollection<IContext> Children
        {
            get => children;
            set
            {
                if (Equals(children, value)) return;
                OnPropertyChanging(nameof(Children));
                children = value;
                OnPropertyChanged(nameof(Children));
            }
        }
        public ObservableCollection<IContextConnector> Connectors
        {
            get => connectors;
            set
            {
                if (Equals(connectors, value)) return;
                OnPropertyChanging(nameof(Connectors));
                connectors = value;
                OnPropertyChanged(nameof(Connectors));
            }
        }

        [VeloxCommand(CanValidate: true)] // 异步锁地
        public async Task SaveAsync(object? parameter, CancellationToken ct)
        {
            await Task.Delay(3000);
            if(parameter is Button)
            {
                MessageBox.Show("命令来自Button1");
            }
        }
        [VeloxCommand(CanConcurrent: true)] // 并发地
        public Task LoadAsync(object? parameter, CancellationToken ct)
        {
            if (parameter is Button)
            {
                MessageBox.Show("命令来自Button2");
            }
            return Task.CompletedTask;
        }

        // 源生成器可以生成类似以下的代码
        public IVeloxCommand SaveCommand => new VeloxCommand(
            executeAsync: SaveAsync,
            canExecute: CanExecuteSaveCommand);
        private partial bool CanExecuteSaveCommand(object? parameter);
        private partial bool CanExecuteSaveCommand(object? parameter)  // 这一条是为了演示思路，实际应由用户实现
        {
            return true;
        }
        public IVeloxCommand LoadCommand => new ConcurrentVeloxCommand(
            executeAsync: LoadAsync,
            canExecute: _ => true);
    }
}
