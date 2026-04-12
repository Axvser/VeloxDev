using VeloxDev.Core.TimeLine;

namespace VeloxDev.Core.MonoBehaviour;

public interface IMonoBehaviour​
{
    void InitializeMonoBehaviour​();
    void InvokeAwake();
    void InvokeStart();
    void InvokeUpdate(FrameEventArgs e);
    void InvokeLateUpdate(FrameEventArgs e);
    void InvokeFixedUpdate(FrameEventArgs e);
}