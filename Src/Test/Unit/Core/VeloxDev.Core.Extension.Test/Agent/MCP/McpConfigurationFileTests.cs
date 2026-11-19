using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VeloxDev.AI.MCP;

namespace VeloxDev.Core.Extension.Test.Agent.MCP;

/// <summary>
/// The common <c>.mcp.json</c> shape: a local entry is a raw command, a remote entry is a URL, and both
/// carry the environment/headers a host would otherwise have to rebuild by hand.
/// </summary>
[TestClass]
public class McpConfigurationFileTests
{
    private const string Document = """
    {
      "mcpServers": {
        "fs": {
          "command": "npx",
          "args": ["-y", "@modelcontextprotocol/server-filesystem", "/data"],
          "env": { "FILESYSTEM_ROOT": "/data", "COUNT": 2 },
          "cwd": "/work"
        },
        "learn": {
          "type": "http",
          "url": "https://learn.example/mcp",
          "headers": { "Authorization": "Bearer demo" }
        },
        "legacy": {
          "type": "sse",
          "url": "https://legacy.example/sse"
        },
        "off": {
          "command": "node",
          "args": ["off.js"],
          "disabled": true
        }
      }
    }
    """;

    [TestMethod]
    public void AStdioEntry_BecomesOneNormalCommandWithItsArguments()
    {
        var configs = McpConfigurationFile.Parse(Document);
        var fs = configs.Single(config => config.Name == "fs");

        Assert.AreEqual(McpServerRunMode.Normal, fs.RunMode);
        Assert.AreEqual("npx", fs.Package);
        CollectionAssert.AreEqual(
            new[] { "-y", "@modelcontextprotocol/server-filesystem", "/data" },
            fs.Arguments);
    }

    [TestMethod]
    public void EnvAndCwd_BecomeStdioOptions()
    {
        var fs = McpConfigurationFile.Parse(Document).Single(config => config.Name == "fs");

        Assert.IsNotNull(fs.Options);
        var env = (IReadOnlyDictionary<string, object?>)fs.Options!["env"]!;
        Assert.AreEqual("/data", env["FILESYSTEM_ROOT"]);
        Assert.AreEqual("2", env["COUNT"], "a non-string env value is read as its text");
        Assert.AreEqual("/work", fs.Options!["workingDirectory"]);
    }

    [TestMethod]
    public void AnHttpEntry_BecomesHttpWithHeaders()
    {
        var learn = McpConfigurationFile.Parse(Document).Single(config => config.Name == "learn");

        Assert.AreEqual(McpServerRunMode.Http, learn.RunMode);
        Assert.AreEqual("https://learn.example/mcp", learn.Endpoint);
        var headers = (IReadOnlyDictionary<string, object?>)learn.Options!["headers"]!;
        Assert.AreEqual("Bearer demo", headers["Authorization"]);
    }

    [TestMethod]
    public void AnSseEntry_AsksTheHttpTransportForSse()
    {
        var legacy = McpConfigurationFile.Parse(Document).Single(config => config.Name == "legacy");

        Assert.AreEqual(McpServerRunMode.Http, legacy.RunMode);
        Assert.AreEqual("Sse", legacy.Options!["transportMode"]);
    }

    [TestMethod]
    public void DisabledEntries_AreSkipped()
    {
        var configs = McpConfigurationFile.Parse(Document);

        Assert.AreEqual(3, configs.Count);
        Assert.IsFalse(configs.Any(config => config.Name == "off"));
    }

    [TestMethod]
    public void AnEntryWithNeitherCommandNorUrl_IsRefused()
    {
        const string json = """{ "mcpServers": { "broken": { "description": "nothing" } } }""";

        Assert.ThrowsExactly<InvalidDataException>(() => McpConfigurationFile.Parse(json));
    }

    [TestMethod]
    public void AMissingServersMap_IsRefused()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => McpConfigurationFile.Parse("""{ "servers": {} }"""));
    }

    [TestMethod]
    public void EnvironmentReferences_AreExpanded()
    {
        const string variable = "VELOXDEV_MCP_TEST_ROOT";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "/expanded");
        try
        {
            const string json = """
            {
              "mcpServers": {
                "x": {
                  "command": "${VELOXDEV_MCP_TEST_ROOT}/server",
                  "args": ["--root", "${VELOXDEV_MCP_TEST_ROOT}", "${VELOXDEV_MCP_TEST_MISSING:-fallback}"],
                  "env": { "ROOT": "${VELOXDEV_MCP_TEST_ROOT}" }
                }
              }
            }
            """;

            var config = McpConfigurationFile.Parse(json).Single();

            Assert.AreEqual("/expanded/server", config.Package);
            CollectionAssert.AreEqual(new[] { "--root", "/expanded", "fallback" }, config.Arguments);
            var env = (IReadOnlyDictionary<string, object?>)config.Options!["env"]!;
            Assert.AreEqual("/expanded", env["ROOT"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [TestMethod]
    public void WithServersFromFile_RegistersEveryEnabledEntry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcp-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, Document);
        try
        {
            var scope = new McpScope();

            scope.WithServersFromFile(path);

            Assert.AreEqual(3, scope.RegisteredServers.Count);
            Assert.IsTrue(scope.RegisteredServers.Any(config => config.Name == "fs"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void NormalMode_PassesTheCommandAndArgumentsVerbatim()
    {
        var config = new McpServerConfiguration
        {
            Name = "node",
            RunMode = McpServerRunMode.Normal,
            Package = "node",
            Arguments = ["server.js", "--port", "0"],
        };

        var options = McpScope.BuildStdioTransportOptions(config, Path.GetTempPath());

        Assert.AreEqual("node", options.Command);
        CollectionAssert.AreEqual(new[] { "server.js", "--port", "0" }, options.Arguments!.ToArray());
    }

    [TestMethod]
    public void NormalMode_WithoutACommand_IsRefused()
    {
        var config = new McpServerConfiguration
        {
            Name = "empty",
            RunMode = McpServerRunMode.Normal,
            Package = string.Empty,
        };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => McpScope.BuildStdioTransportOptions(config, Path.GetTempPath()));
    }
}
