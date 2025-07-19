using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel;
using VeloxDev.Core.Mono;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.WPF.WorkflowSystem.Views;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.ContextTree]
    [MonoBehaviour]
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

        [VeloxCommand(CanValidate: true)] // 默认无验证地执行，此处选择性地启用了验证
        public async Task SaveAsync(object? parameter, CancellationToken ct)
        {
            await Task.Delay(3000);
            if (parameter is Button)
            {
                MessageBox.Show("命令A ：来自Button");
            }
        }
        private partial bool CanExecuteSaveCommand(object? parameter) // 启用验证则要求用户实现此分部方法
        {
            return true;
        }

        //------------------------------------------------------------------------------------------------

        [VeloxCommand(CanConcurrent: true)] // 默认带异步锁地执行，此处选择性地启用了并发执行（ 无锁 ）
        public async Task LoadAsync(object? parameter, CancellationToken ct)
        {
            await Task.Delay(3000);
            if (parameter is Button)
            {
                MessageBox.Show("命令B：来自Button");
            }
        }
    }
}
