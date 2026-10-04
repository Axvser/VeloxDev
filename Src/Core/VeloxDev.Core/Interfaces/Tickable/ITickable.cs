using VeloxDev.TimeLine;

namespace VeloxDev.TimeLine;

/// <summary>An object the tick loop drives through the awake, start and update stages.</summary>
public interface ITickable
{
    /// <summary>Registers this object with the tick loop.</summary>
    void InitializeTickable();

    /// <summary>Unregisters this object from the tick loop.</summary>
    void CloseTickable();

    /// <summary>Runs the awake stage.</summary>
    void InvokeAwake();

    /// <summary>Runs the start stage.</summary>
    void InvokeStart();

    /// <summary>Runs the update stage for <paramref name="e"/>.</summary>
    void InvokeUpdate(FrameEventArgs e);

    /// <summary>Runs the late-update stage for <paramref name="e"/>.</summary>
    void InvokeLateUpdate(FrameEventArgs e);

    /// <summary>Runs the fixed-update stage for <paramref name="e"/>.</summary>
    void InvokeFixedUpdate(FrameEventArgs e);
}
