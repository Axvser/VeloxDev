namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowHelper : IDisposable
    {
        public Task CloseAsync(); // 安全地关闭连接两端工作
    }
}
