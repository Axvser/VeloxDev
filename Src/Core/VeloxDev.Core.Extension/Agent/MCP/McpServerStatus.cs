namespace VeloxDev.AI.MCP;

/// <summary>
/// 单个 MCP 服务器的连接生命周期状态。
/// 宿主 UI 通过 <see cref="McpStatusViewModel"/> 绑定以展示存活/安装中/连接中/错误。
/// </summary>
public enum McpServerStatus
{
    /// <summary>尚未开始加载。</summary>
    NotStarted,

    /// <summary>正在安装/准备运行时（npm/pip 等本地模式）。远程 Http 模式跳过此态。</summary>
    Installing,

    /// <summary>正在连接（启动本地进程或握手远程服务器）。</summary>
    Connecting,

    /// <summary>连接成功，工具可用。</summary>
    Connected,

    /// <summary>加载/连接失败（见 <see cref="McpServerStatusViewModel.Error"/>）。</summary>
    Error,
}
