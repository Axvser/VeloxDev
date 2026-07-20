# MCP

> **example**

```csharp
var tools = await new McpScope()
    .WithMcpRoot(".evn/mcp")  // Installation location of mcp packages (relative to the directory containing the .exe)
    .LoadAsync(new[]
    {
        new McpServerConfiguration
        {
            Name = "文件系统",
            RunMode = McpServerRunMode.Npx,  // Note: your machine should support node.js or python
            NpmPackage = "@modelcontextprotocol/server-filesystem",  // The corresponding mcp package
        },
    });
```

> **Configuration**

```csharp
public partial class McpServerConfiguration
{
    [VeloxProperty] public partial string Name { get; set; }
    [VeloxProperty] public partial string Description { get; set; }
    [VeloxProperty] public partial McpServerRunMode RunMode { get; set; }
    [VeloxProperty] public partial string NpmPackage { get; set; }       // NPM/Python package name
    [VeloxProperty] public partial string? NpmVersion { get; set; }      // Version tag
    [VeloxProperty] public partial string ServerModulePath { get; set; } // Entry file path
    [VeloxProperty] public partial string[] ServerArguments { get; set; } // Additional arguments
}
```