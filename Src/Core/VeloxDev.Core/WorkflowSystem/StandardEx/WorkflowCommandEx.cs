using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.StandardEx;

/// <summary>Standard lifecycle operations for a set of commands: lock, clear and unlock them.</summary>
public static class WorkflowCommandEx
{
    /// <summary>Locks every command.</summary>
    public static void StandardClosing(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            command.Lock();
        }
    }

    /// <summary>Locks every command, awaiting each.</summary>
    public static async Task StandardClosingAsync(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            await command.LockAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Clears every command's queue.</summary>
    public static void StandardClose(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            command.Clear();
        }
    }

    /// <summary>Clears every command's queue, awaiting each.</summary>
    public static async Task StandardCloseAsync(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            await command.ClearAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Unlocks every command.</summary>
    public static void StandardClosed(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            command.Unlock();
        }
    }

    /// <summary>Unlocks every command, awaiting each.</summary>
    public static async Task StandardClosedAsync(this IReadOnlyCollection<IVeloxCommand> commands)
    {
        foreach (var command in commands)
        {
            await command.UnlockAsync().ConfigureAwait(false);
        }
    }
}
