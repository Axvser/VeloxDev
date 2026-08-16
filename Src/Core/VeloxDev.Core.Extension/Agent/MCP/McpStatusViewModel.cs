using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using VeloxDev.MVVM;

namespace VeloxDev.AI.MCP;

/// <summary>
/// 单个 MCP 服务器的可绑定状态视图模型。遵循框架 <c>[VeloxProperty]</c> MVVM 规范，
/// 属性变更经生成器广播 <see cref="INotifyPropertyChanged"/>，可直接绑定 UI。
/// </summary>
public partial class McpServerStatusViewModel
{
    [VeloxProperty] private string name = string.Empty;
    [VeloxProperty] private string description = string.Empty;
    [VeloxProperty] private McpServerRunMode runMode = McpServerRunMode.Npm;
    [VeloxProperty] private McpServerStatus state = McpServerStatus.NotStarted;
    [VeloxProperty] private int toolCount = 0;
    [VeloxProperty] private string? error = null;
    [VeloxProperty] private string? endpoint = null;

    /// <summary>连接成功（工具可用）。</summary>
    public bool IsConnected => State == McpServerStatus.Connected;

    /// <summary>正在安装运行时（本地 npm/pip）。</summary>
    public bool IsInstalling => State == McpServerStatus.Installing;

    /// <summary>正在连接（启动进程 / 握手远程）。</summary>
    public bool IsConnecting => State == McpServerStatus.Connecting;

    /// <summary>加载失败（见 <see cref="Error"/>）。</summary>
    public bool IsError => State == McpServerStatus.Error;

    /// <summary>状态的中文展示文本（UI / Agent 直接可用）。</summary>
    public string StateText => State switch
    {
        McpServerStatus.Connected => "已连接",
        McpServerStatus.Installing => "安装中",
        McpServerStatus.Connecting => "连接中",
        McpServerStatus.Error => "错误",
        _ => "未启动",
    };

    partial void OnStateChanged(McpServerStatus oldValue, McpServerStatus newValue)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsInstalling));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(StateText));
    }
}

/// <summary>
/// 全局可绑定的 MCP 服务器状态视图模型：宿主 UI 绑定 <see cref="Servers"/> 展示每个服务器的
/// 存活/安装中/连接中/错误状态，并读取聚合计数（<see cref="ConnectedCount"/>/<see cref="ErrorCount"/>）。
/// 由 <see cref="McpScope.Status"/> 持有并在 <see cref="McpScope.LoadAsync"/> 过程中驱动。
/// </summary>
public partial class McpStatusViewModel
{
    // Servers / IsLoading 由生成器从以下 [VeloxProperty] 字段生成（不要手动声明同名属性）。
    [VeloxProperty] private ObservableCollection<McpServerStatusViewModel> servers = [];
    [VeloxProperty] private bool isLoading = false;

    /// <summary>已连接（存活）服务器数。</summary>
    public int ConnectedCount => Count(McpServerStatus.Connected);

    /// <summary>失败服务器数。</summary>
    public int ErrorCount => Count(McpServerStatus.Error);

    /// <summary>安装中或连接中服务器数。</summary>
    public int WorkingCount => Servers.Count(s => s.State is McpServerStatus.Installing or McpServerStatus.Connecting);

    /// <summary>是否全部服务器都已就绪（无安装/连接/错误）。</summary>
    public bool IsAllReady => Servers.Count > 0 && Servers.Count == ConnectedCount;

    /// <summary>是否至少有一个服务器失败。</summary>
    public bool HasError => ErrorCount > 0;

    private int Count(McpServerStatus state) => Servers.Count(s => s.State == state);

    /// <summary>
    /// 登记一个服务器并订阅其状态变更，使聚合计数随任一服务器变化而刷新。
    /// 应在 UI 线程上调用（或经 <see cref="McpScope.WithSynchronizationContext"/> 汇集）。
    /// </summary>
    public void Track(McpServerStatusViewModel server)
    {
        if (server is null) return;
        server.PropertyChanged += OnServerPropertyChanged;
        Servers.Add(server);
        NotifyAggregates();
    }

    /// <summary>设置批量加载中旗标并刷新聚合。</summary>
    public void SetLoading(bool loading)
    {
        IsLoading = loading;
        NotifyAggregates();
    }

    /// <summary>清空全部服务器状态（重载前调用）。</summary>
    public void Reset()
    {
        foreach (var s in Servers)
            s.PropertyChanged -= OnServerPropertyChanged;
        Servers.Clear();
        NotifyAggregates();
    }

    private void OnServerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(McpServerStatusViewModel.State))
            NotifyAggregates();
    }

    private void NotifyAggregates()
    {
        OnPropertyChanged(nameof(ConnectedCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WorkingCount));
        OnPropertyChanged(nameof(IsAllReady));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsLoading));
    }
}
