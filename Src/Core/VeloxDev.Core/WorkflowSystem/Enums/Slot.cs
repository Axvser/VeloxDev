using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "定义工作流插槽在两个方向上的连接容量：Target 表示当前插槽可主动连出的目标数量，Source 表示当前插槽可被连入的来源数量。这是一个位掩码枚举，两个方向独立计数；组合值表示同时具备多种方向权限。语义优先级为 Multiple > One > None，同一方向上高优先级覆盖低优先级。")]
[AgentContext(AgentLanguages.English, "Defines the connection capacity of a workflow slot in two independent directions: Target means how many outgoing targets this slot may connect to, and Source means how many incoming sources may connect to this slot. This is a flags enum. Each direction is evaluated independently, and combined values grant permissions for both directions. Semantic priority per direction is Multiple > One > None, where the higher priority overrides the lower one.")]
[Flags]
public enum SlotChannel : int
{
    [AgentContext(AgentLanguages.Chinese, "无权限：默认值，表示不允许建立任何连接。")]
    [AgentContext(AgentLanguages.English, "None: Default value indicating no connections are allowed.")]
    None = 0,

    [AgentContext(AgentLanguages.Chinese, "单一目标：当前插槽最多只能主动连接到 1 个目标，即最多 1 条出向连接。")]
    [AgentContext(AgentLanguages.English, "One Target: This slot may actively connect to at most 1 target, i.e. at most 1 outgoing connection.")]
    OneTarget = 1,

    [AgentContext(AgentLanguages.Chinese, "单一源：当前插槽最多只能被 1 个来源连接，即最多 1 条入向连接。")]
    [AgentContext(AgentLanguages.English, "One Source: This slot may be connected by at most 1 source, i.e. at most 1 incoming connection.")]
    OneSource = 2,

    [AgentContext(AgentLanguages.Chinese, "单一双向：等价于 OneTarget | OneSource。表示最多 1 条出向连接且最多 1 条入向连接，两者分别计数；不表示必须与同一个对象形成一对一绑定。")]
    [AgentContext(AgentLanguages.English, "One Both: Equivalent to OneTarget | OneSource. It means at most 1 outgoing connection and at most 1 incoming connection, counted independently. It does not mean a strict one-to-one pairing with the same peer.")]
    OneBoth = OneTarget | OneSource,

    [AgentContext(AgentLanguages.Chinese, "多重目标：当前插槽可以主动连接到多个目标，即允许多条出向连接。")]
    [AgentContext(AgentLanguages.English, "Multiple Targets: This slot may actively connect to multiple targets, i.e. multiple outgoing connections are allowed.")]
    MultipleTargets = 4,

    [AgentContext(AgentLanguages.Chinese, "多重源：当前插槽可以被多个来源连接，即允许多条入向连接。")]
    [AgentContext(AgentLanguages.English, "Multiple Sources: This slot may be connected by multiple sources, i.e. multiple incoming connections are allowed.")]
    MultipleSources = 8,

    [AgentContext(AgentLanguages.Chinese, "多重双向：等价于 MultipleTargets | MultipleSources。表示出向和入向都允许多条连接，两者分别计数。")]
    [AgentContext(AgentLanguages.English, "Multiple Both: Equivalent to MultipleTargets | MultipleSources. It means both outgoing and incoming directions allow multiple connections, counted independently.")]
    MultipleBoth = MultipleTargets | MultipleSources
}

[AgentContext(AgentLanguages.Chinese, "描述工作流插槽的当前运行时状态。支持组合状态，例如同时处于预览发送和预览接收状态。")]
[AgentContext(AgentLanguages.English, "Describes the current runtime state of a workflow slot. Supports combined states, e.g., simultaneously previewing as sender and receiver.")]
[Flags]
public enum SlotState : int
{
    [AgentContext(AgentLanguages.Chinese, "空闲：插槽处于待机状态，无任何连接或操作。")]
    [AgentContext(AgentLanguages.English, "StandBy: The slot is idle with no active connections or operations.")]
    StandBy = 1,

    [AgentContext(AgentLanguages.Chinese, "预览发送：正在尝试建立连接，当前作为发送端（拖拽出线中）。")]
    [AgentContext(AgentLanguages.English, "Preview Sender: Attempting to establish a connection, acting as the sender (dragging output).")]
    PreviewSender = 2,

    [AgentContext(AgentLanguages.Chinese, "预览接收：正在尝试建立连接，当前作为接收端（拖拽入线中）。")]
    [AgentContext(AgentLanguages.English, "Preview Receiver: Attempting to establish a connection, acting as the receiver (dragging input).")]
    PreviewReceiver = 4,

    [AgentContext(AgentLanguages.Chinese, "已连接发送：已建立稳定连接，作为数据发送端。")]
    [AgentContext(AgentLanguages.English, "Sender: Connection established, actively acting as a data sender.")]
    Sender = 8,

    [AgentContext(AgentLanguages.Chinese, "已连接接收：已建立稳定连接，作为数据处理/接收端。")]
    [AgentContext(AgentLanguages.English, "Receiver: Connection established, actively acting as a data receiver/processor.")]
    Receiver = 16
}