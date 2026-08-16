using System;
using System.Collections.Generic;
using ModelContextProtocol.Client;
using VeloxDev.MVVM;

namespace VeloxDev.AI.MCP;

public partial class McpServerConfiguration
{
    [VeloxProperty] public partial string Name { get; set; }
    [VeloxProperty] public partial string Description { get; set; }

    /// <summary>
    /// How the server is reached.
    /// <see cref="McpServerRunMode.Npm"/> requires npm-installed package;
    /// <see cref="McpServerRunMode.Npx"/>, <see cref="McpServerRunMode.Uvx"/>, and <see cref="McpServerRunMode.Pip"/>
    /// use <see cref="Package"/> as the package name directly;
    /// <see cref="McpServerRunMode.Dotnet"/> executes via <c>dotnet {Package}</c>;
    /// <see cref="McpServerRunMode.Exe"/> executes <c>{Package}</c> directly (tech-agnostic);
    /// <see cref="McpServerRunMode.Http"/> connects to a remote server over HTTP using <see cref="Endpoint"/>.
    /// </summary>
    [VeloxProperty] public partial McpServerRunMode RunMode { get; set; }

    /// <summary>
    /// 包名/目录名。
    /// Npm/Npx/Uvx/Pip: NPM 或 PyPI 包名；
    /// Dotnet: mcpRoot 下 DLL 路径，如 "sharp-email-mcp/SharpEmailMcp.dll"；
    /// Exe: mcpRoot 下可执行文件路径，如 "tools/my-tool.exe"。
    /// </summary>
    [VeloxProperty] public partial string Package { get; set; }

    /// <summary>
    /// 版本标签。为 null 时使用 "latest"。
    /// 对 <see cref="McpServerRunMode.Npm"/> 和 <see cref="McpServerRunMode.Pip"/> 模式生效。
    /// </summary>
    [VeloxProperty] public partial string? Version { get; set; }

    /// <summary>传给服务器进程的额外参数</summary>
    [VeloxProperty] public partial string[] Arguments { get; set; }

    // ── 远程（McpServerRunMode.Http）──

    /// <summary>
    /// 远程 MCP 端点 URL（仅 <see cref="McpServerRunMode.Http"/> 模式），如 "https://mcp.example.com/mcp"。
    /// 通过 Streamable HTTP（旧服务器自动回退 SSE）连接。
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>远程请求附加 HTTP 头（如 Bearer 令牌、自定义头）。仅 Http 模式。</summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>OAuth 2.0 客户端 ID（可选）。设置后远程连接启用 OAuth Authorization Code + PKCE。</summary>
    public string? OAuthClientId { get; set; }

    /// <summary>OAuth 2.0 客户端密钥（可选）。</summary>
    public string? OAuthClientSecret { get; set; }

    /// <summary>OAuth 重定向 URI（可选）。授权跳转由宿主经 <c>McpScope.WithOAuthAuthorizationRedirect</c> 处理。</summary>
    public string? OAuthRedirectUri { get; set; }

    /// <summary>OAuth 请求的 scopes（可选）。</summary>
    public string[]? OAuthScopes { get; set; }

    /// <summary>
    /// 连接超时（可选，仅 Http 模式）。覆盖 <see cref="McpScope.WithConnectionTimeout"/> 的全局默认；
    /// 同时作为传输层连接超时与 MCP 初始化超时（<see cref="McpClientOptions.InitializationTimeout"/>）。
    /// </summary>
    public TimeSpan? ConnectionTimeout { get; set; }

    /// <summary>
    /// HTTP 传输模式（可选，仅 Http 模式）：AutoDetect（默认，先试 Streamable HTTP 再回退 SSE）、
    /// StreamableHttp、Sse。缺省用 SDK 默认（AutoDetect）。
    /// </summary>
    public HttpTransportMode? TransportMode { get; set; }

    /// <summary>
    /// 是否由传输层拥有 MCP 会话（可选，仅 Http 模式）。2.x 默认无状态（stateless，不维护
    /// <c>Mcp-Session-Id</c>）；设为 <c>true</c> 可让传输层持有会话（有状态）。缺省用 SDK 默认。
    /// </summary>
    public bool? OwnsSession { get; set; }
}
