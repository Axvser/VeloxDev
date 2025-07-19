﻿using System.Collections.Concurrent;
using VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.Context]
    public partial class ShowerNodeViewModel
    {
        public void BroadcastTask(params object?[] args)
        {
            
        }
        public async void ExecuteTask(IContext sender, params object?[] args)
        {
            await EnqueueTask(async () => { await OnTaskExecute(sender, args); }, sender, args);
        }

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
