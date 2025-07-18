using System.Diagnostics;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.MVVM
{
    /// <summary>
    /// Execute the command using the specified Task method
    /// <para> Define ➤ public Task Method(object? parameter, CancellationToken ct)</para>
    /// <paramref name="CanValidate" discribtion=" ➤ True indicates that the executability verification of this command is enabled"/>
    /// <paramref name="CanConcurrent" discribtoin=" ➤ True indicates that the command is concurrent"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class VeloxCommandAttribute(string Name = "Auto", bool CanValidate = false, bool CanConcurrent = false) : Attribute
    {
        public string Name { get; } = Name;
        public bool CanValidate { get; } = CanValidate;
        public bool CanConcurrent { get; } = CanConcurrent;
    }

    public sealed class ConcurrentVeloxCommand(
        Func<object?, CancellationToken, Task> executeAsync,
        Predicate<object?>? canExecute = null) : IVeloxCommand
    {
        private readonly Func<object?, CancellationToken, Task> _executeAsync = executeAsync;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute?.Invoke(parameter) ?? true;
        }

        public async void Execute(object? parameter)
        {
            await ExecuteAsync(parameter);
        }

        public async Task ExecuteAsync(object? parameter)
        {
            try
            {
                var cts = new CancellationTokenSource();
                await _executeAsync.Invoke(parameter, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error executing command: {ex.Message}");
            }
        }
    }

    public sealed class VeloxCommand(
        Func<object?, CancellationToken, Task> executeAsync,
        Predicate<object?>? canExecute = null) : IVeloxCommand
    {
        private readonly SemaphoreSlim _queueSemaphore = new(1, 1);
        private readonly Func<object?, CancellationToken, Task> _executeAsync = executeAsync;
        private CancellationTokenSource? _cancellationTokenSource = null;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute?.Invoke(parameter) ?? true;
        }

        public async void Execute(object? parameter)
        {
            await ExecuteAsync(parameter);
        }

        public async Task ExecuteAsync(object? parameter)
        {
            await _queueSemaphore.WaitAsync();
            try
            {
                var oldCts = Interlocked.Exchange(ref _cancellationTokenSource, new CancellationTokenSource());
                oldCts?.Cancel();
                await _executeAsync.Invoke(parameter, _cancellationTokenSource!.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error executing command: {ex.Message}");
            }
            finally
            {
                _queueSemaphore.Release();
            }
        }
    }
}
