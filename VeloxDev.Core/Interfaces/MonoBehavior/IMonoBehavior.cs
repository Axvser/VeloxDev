using VeloxDev.Core.TimeLine;

namespace VeloxDev.Core.Interfaces.MonoBehavior;

public interface IMonoBehavior
{
    void InitializeMonoBehavior();
    void InvokeAwake();
    void InvokeStart();
    void InvokeUpdate(FrameEventArgs e);
    void InvokeLateUpdate(FrameEventArgs e);
    void InvokeFixedUpdate(FrameEventArgs e);
}