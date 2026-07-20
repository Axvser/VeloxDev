# MCP

> **示例**

```csharp
var tools = await new McpScope()
    .WithMcpRoot(".evn/mcp")  // mcp包的安装位置（ 相对于.exe所在目录 ）
    .LoadAsync(new[]
    {
        new McpServerConfiguration
        {
            Name = "文件系统",
            RunMode = McpServerRunMode.Npx,  // 注意：你的本机应支持 node.js 或 python
            NpmPackage = "@modelcontextprotocol/server-filesystem",  // mcp 对应的包
        },
    });
```
```

> **配置**

```csharp
public partial class McpServerConfiguration
{
    [VeloxProperty] public partial string Name { get; set; }
    [VeloxProperty] public partial string Description { get; set; }
    [VeloxProperty] public partial McpServerRunMode RunMode { get; set; }
    [VeloxProperty] public partial string NpmPackage { get; set; }       // NPM/Python 包名
    [VeloxProperty] public partial string? NpmVersion { get; set; }      // 版本标签
    [VeloxProperty] public partial string ServerModulePath { get; set; } // 入口文件路径
    [VeloxProperty] public partial string[] ServerArguments { get; set; } // 额外参数
}
```