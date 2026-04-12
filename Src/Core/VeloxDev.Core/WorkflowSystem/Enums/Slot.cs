namespace VeloxDev.Core.WorkflowSystem;

[Flags] // 语义优先级 : Multiple > One > None
public enum SlotChannel : int
{
    None = 1,             // 无可用通道
    OneTarget = 2,        // 仅允许一个目标
    OneSource = 4,        // 仅允许一个源
    OneBoth = OneTarget | OneSource,
    MultipleTargets = 8,  // 允许多个目标
    MultipleSources = 16, // 允许多个源
    MultipleBoth = MultipleTargets | MultipleSources
}

[Flags] // 混合状态模型
public enum SlotState : int
{
    StandBy = 1,             // 空闲状态,未连接
    PreviewSender = 2,       // 预览发送端状态,正在连接过程中,作为发送端
    PreviewReceiver = 4,     // 预览处理端状态,正在连接过程中,作为处理端
    Sender = 8,              // 已连接状态,作为发送端
    Receiver = 16            // 已连接状态,作为处理端
}