using VeloxDev.TimeLine;

namespace VeloxDev.MonoBehaviour;

public interface IMonoBehaviour​
{
    void InitializeMonoBehaviour​();
    void InvokeAwake();
    void InvokeStart();
    void InvokeUpdate(FrameEventArgs e);
    void InvokeLateUpdate(FrameEventArgs e);
    void InvokeFixedUpdate(FrameEventArgs e);
}