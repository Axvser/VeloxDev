namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowHelper
    {
        public void Closing();    // 关闭前
        public Task CloseAsync(); // 安全地关闭
        public void Closed();     // 关闭后
    }
}
