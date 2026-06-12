namespace VeloxDev.WorkflowSystem.CSharp;

internal sealed class CSharpObjectRegistration : IDisposable
{
    private Action? unregister;

    public CSharpObjectRegistration(Action unregister)
    {
        this.unregister = unregister;
    }

    public void Dispose()
        => Interlocked.Exchange(ref unregister, null)?.Invoke();
}
