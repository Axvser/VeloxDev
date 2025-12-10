using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.WorkflowSystem.StandardEx;

public static class WorkflowCommandEx
{
    public static void StandardClosing(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach(var command in commands)
        {
            command.Lock();
        }
    }

    public static async Task StandardClosingAsync(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            await command.LockAsync().ConfigureAwait(false);
        }
    }

    public static void StandardClose(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            command.Clear();
        }
    }

    public static async Task StandardCloseAsync(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            await command.ClearAsync().ConfigureAwait(false);
        }
    }

    public static void StandardClosed(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            command.UnLock();
        }
    }

    public static async Task StandardClosedAsync(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            await command.UnLockAsync().ConfigureAwait(false);
        }
    }
}
