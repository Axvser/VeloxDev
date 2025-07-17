using System.Collections.Concurrent;
using System.ComponentModel;
using VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    /* 注释
     * 下述内容大部分将由源生成器生成，此处为开发测试阶段的手动实现
     */
    [Workflow.Context]
    public partial class ShowerNodeViewModel : IContext
    {
        public event PropertyChangingEventHandler? PropertyChanging;
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsEnabled
        {
            get => isenabled;
            set
            {
                if (isenabled == value) return;
                OnPropertyChanging(nameof(IsEnabled));
                var oldValue = isenabled;
                OnIsEnabledChanging(oldValue, value);
                isenabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                OnIsEnabledChanged(oldValue, value);
            }
        }
        public Anchor Anchor
        {
            get => anchor;
            set
            {
                if (Equals(anchor, value)) return;
                var oldValue = anchor;
                OnPropertyChanging(nameof(Anchor));
                OnAnchorChanging(oldValue, value);
                anchor = value;
                OnPropertyChanged(nameof(Anchor));
                OnAnchorChanged(oldValue, value);
            }
        }
        public IContextTree? Tree
        {
            get => tree;
            set
            {
                if (Equals(tree, value)) return;
                var oldValue = tree;
                OnPropertyChanging(nameof(Tree));
                tree?.Children.Remove(this);
                OnTreeChanging(oldValue, value);
                tree = value;
                tree?.Children.Add(this);
                OnPropertyChanged(nameof(Tree));
                OnTreeChanged(oldValue, value);
            }
        }
        public void BroadcastTask(params object?[] args)
        {
            IsEnabled = false;
            Tree?.BroadcastTask(this, args);
            IsEnabled = true;
        }
        public async void ExecuteTask(IContext sender, params object?[] args)
        {
            await EnqueueTask(async () => { await OnTaskExecute(sender, args); }, sender, args);
        }

        partial void OnIsEnabledChanging(bool oldValue, bool newValue);
        partial void OnIsEnabledChanged(bool oldValue, bool newValue);
        partial void OnAnchorChanging(Anchor oldValue, Anchor newValue);
        partial void OnAnchorChanged(Anchor oldValue, Anchor newValue);
        partial void OnTreeChanging(IContextTree? oldValue, IContextTree? newValue);
        partial void OnTreeChanged(IContextTree? oldValue, IContextTree? newValue);

        private bool isenabled = true;
        private Anchor anchor = Anchor.Default;
        private IContextTree? tree;
        public void OnPropertyChanging(string propertyName)
        {
            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
        }
        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private readonly ConcurrentQueue<Tuple<Func<Task>, IContext, object?[]>> workflowTasksBuffer = [];
        private readonly SemaphoreSlim workflowTasksSemaphore = new(1, 1);
        private async Task EnqueueTask(Func<Task> taskFactory, IContext sender, object?[] args)
        {
            workflowTasksBuffer.Enqueue(Tuple.Create(taskFactory, sender, args));
            await workflowTasksSemaphore.WaitAsync();
            try
            {
                IsEnabled = false;
                if (workflowTasksBuffer.TryDequeue(out var task))
                {
                    try
                    {
                        await task.Item1.Invoke();
                    }
                    catch (Exception ex)
                    {
                        if (tree?.TryGetTaskFuse(ex.GetType(), out var fuse) ?? false)
                        {
                            fuse?.Fuse(task.Item2, this, task.Item3, ex);
                        }
                    }
                }
            }
            finally
            {
                IsEnabled = true;
                workflowTasksSemaphore.Release();
            }
        }
        private partial Task OnTaskExecute(IContext sender, object?[] args);
        private partial Task OnTaskExecute(IContext sender, object?[] args)
        {
            // 源生成器产生Task的分部声明，用户需要实现此方法
            throw new NotImplementedException();
        }
    }
}
