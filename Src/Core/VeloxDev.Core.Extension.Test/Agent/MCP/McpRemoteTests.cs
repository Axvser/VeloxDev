using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.MCP;

namespace VeloxDev.Core.Extension.Test.Agent.MCP;

/// <summary>
/// Remote MCP (<see cref="McpServerRunMode.Http"/>) coverage: config validation, transport-option
/// construction (endpoint / headers / OAuth), and the misconfiguration error path. The stdio modes
/// (Npm/Npx/Pip/Dotnet/Exe) are unchanged and continue to build <see cref="StdioClientTransport"/>.
/// </summary>
[TestClass]
public class McpRemoteTests
{
    private static McpServerConfiguration HttpConfig(string? endpoint = "https://mcp.example.com/mcp", Action<McpServerConfiguration>? configure = null)
    {
        var c = new McpServerConfiguration
        {
            Name = "remote-test",
            RunMode = McpServerRunMode.Http,
            Endpoint = endpoint,
        };
        configure?.Invoke(c);
        return c;
    }

    [TestMethod]
    public void HttpMode_WithoutEndpoint_Throws()
    {
        var scope = new McpScope();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => scope.BuildHttpTransportOptions(HttpConfig(endpoint: null)));
    }

    [TestMethod]
    public void HttpMode_BuildsTransportOptions_EndpointHeadersAndName()
    {
        var scope = new McpScope();
        var config = HttpConfig(configure: c =>
            c.Options = new { headers = new { Authorization = "Bearer abc" } });

        var options = scope.BuildHttpTransportOptions(config);

        Assert.AreEqual(new Uri("https://mcp.example.com/mcp"), options.Endpoint);
        Assert.AreEqual("remote-test", options.Name);
        Assert.IsNotNull(options.AdditionalHeaders);
        Assert.AreEqual("Bearer abc", options.AdditionalHeaders!["Authorization"]);
        Assert.IsNull(options.OAuth, "without OAuth credentials, OAuth options must stay null");
    }

    [TestMethod]
    public async Task HttpMode_OAuth_ConfiguresClientOAuthOptions_AndWiresRedirect()
    {
        var scope = new McpScope();
        bool redirectCalled = false;
        scope.WithOAuthAuthorizationRedirect((authUri, redirectUri, ct) =>
        {
            redirectCalled = true;
            return Task.FromResult("http://localhost:1179/callback?code=abc123");
        });
        var config = HttpConfig(configure: c =>
            c.Options = new
            {
                oauth = new
                {
                    clientId = "demo-client",
                    clientSecret = "demo-secret",
                    redirectUri = "http://localhost:1179/callback",
                    scopes = new[] { "mcp.read", "mcp.write" },
                },
            });

        var options = scope.BuildHttpTransportOptions(config);

        Assert.IsNotNull(options.OAuth, "OAuth must be configured when OAuthClientId is set");
        Assert.AreEqual("demo-client", options.OAuth!.ClientId);
        Assert.AreEqual("demo-secret", options.OAuth.ClientSecret);
        Assert.IsNotNull(options.OAuth.RedirectUri);
        Assert.AreEqual("http://localhost:1179/callback", options.OAuth.RedirectUri!.ToString());
        Assert.IsNotNull(options.OAuth.Scopes);
        CollectionAssert.AreEquivalent(new[] { "mcp.read", "mcp.write" }, new List<string>(options.OAuth.Scopes!));
        Assert.IsNotNull(options.OAuth.AuthorizationRedirectDelegate,
            "the host redirect hook must be wired into ClientOAuthOptions");

        // The wired delegate must actually be invocable by the SDK.
        await options.OAuth.AuthorizationRedirectDelegate!(
            new Uri("https://auth.example.com/authorize"),
            new Uri("http://localhost:1179/callback"),
            CancellationToken.None);
        Assert.IsTrue(redirectCalled, "the host redirect delegate must be invoked");
    }

    [TestMethod]
    public void CreateHttpTransport_ReturnsHttpClientTransport()
    {
        var scope = new McpScope();

        var transport = scope.CreateHttpTransport(HttpConfig());

        Assert.IsNotNull(transport, "Http run mode must produce an HTTP client transport");
    }

    // ── Dynamic loaded-tool registry (mid-session add/remove) ──

    [TestMethod]
    public async Task LoadAsync_FailedServers_LeaveLoadedToolsEmpty()
    {
        var scope = new McpScope();
        await scope.LoadAsync([new McpServerConfiguration { Name = "bad", RunMode = McpServerRunMode.Http, Endpoint = null }]);

        Assert.AreEqual(0, scope.LoadedTools.Count, "failed servers must not contribute tools");
        Assert.AreEqual(McpServerStatus.Error, scope.Status.Servers.Single().State);
    }

    [TestMethod]
    public void UnloadServer_ResetsStatus_AndReportsNoToolsWhenNoneLoaded()
    {
        var scope = new McpScope();
        var config = new McpServerConfiguration { Name = "bad", RunMode = McpServerRunMode.Http, Endpoint = null };
        _ = scope.LoadAsync([config]).GetAwaiter().GetResult();
        Assert.AreEqual(McpServerStatus.Error, scope.Status.Servers.Single().State);

        var removed = scope.UnloadServer("bad");

        Assert.IsFalse(removed, "no tools were loaded, so UnloadServer reports nothing removed");
        Assert.AreEqual(McpServerStatus.NotStarted, scope.Status.Servers.Single().State,
            "UnloadServer must reset the server status to NotStarted");
    }

    // ── Timeout & transport semantics ──

    [TestMethod]
    public void WithConnectionTimeout_AppliedToTransportOptions()
    {
        var scope = new McpScope().WithConnectionTimeout(TimeSpan.FromSeconds(7));

        var options = scope.BuildHttpTransportOptions(HttpConfig());

        Assert.AreEqual(TimeSpan.FromSeconds(7), options.ConnectionTimeout);
    }

    [TestMethod]
    public void PerServerConnectionTimeout_OverridesScopeDefault()
    {
        var scope = new McpScope().WithConnectionTimeout(TimeSpan.FromSeconds(7));
        var config = HttpConfig(configure: c => c.Options = new { connectionTimeout = 2 }); // 秒

        var options = scope.BuildHttpTransportOptions(config);

        Assert.AreEqual(TimeSpan.FromSeconds(2), options.ConnectionTimeout,
            "per-server Options.connectionTimeout must override the scope default");
    }

    [TestMethod]
    public void TransportMode_AndOwnsSession_Applied()
    {
        var scope = new McpScope();
        var config = HttpConfig(configure: c =>
            c.Options = new { transportMode = "StreamableHttp", ownsSession = true });

        var options = scope.BuildHttpTransportOptions(config);

        Assert.AreEqual(HttpTransportMode.StreamableHttp, options.TransportMode);
        Assert.IsTrue(options.OwnsSession);
    }

    [TestMethod]
    public void UnknownOptionsKey_IsRejected()
    {
        var scope = new McpScope();
        var config = HttpConfig(configure: c => c.Options = new { typoKey = "oops" });

        Assert.ThrowsExactly<InvalidOperationException>(() => scope.BuildHttpTransportOptions(config));
    }

    [TestMethod]
    public void StdioOptions_Env_AndWorkingDirectory_Applied()
    {
        var config = new McpServerConfiguration
        {
            Name = "fs",
            RunMode = McpServerRunMode.Npx,
            Package = "@modelcontextprotocol/server-filesystem",
            Options = new { env = new { FILESYSTEM_ROOT = "C:/data", API_KEY = "k" }, workingDirectory = "C:/data" },
        };

        var options = McpScope.BuildStdioTransportOptions(config, System.IO.Path.GetTempPath());

        Assert.IsNotNull(options.EnvironmentVariables, "env must map to EnvironmentVariables");
        Assert.AreEqual("C:/data", options.EnvironmentVariables!["FILESYSTEM_ROOT"]);
        Assert.AreEqual("C:/data", options.WorkingDirectory);
    }

    [TestMethod]
    public async Task LoadAsync_HttpMisconfigured_RaisesServerErrorInsteadOfThrowing()
    {
        var scope = new McpScope();
        Exception? captured = null;
        scope.ServerError += (config, ex) => captured = ex;

        var tools = await scope.LoadAsync([HttpConfig(endpoint: null)]);

        Assert.IsNotNull(captured, "ServerError must be raised for a misconfigured Http server");
        Assert.IsInstanceOfType<InvalidOperationException>(captured);
        Assert.AreEqual(0, tools.Length, "no tools should be returned for a failed server");
    }

    // ── Global bindable status view model ──

    [TestMethod]
    public void Status_StartsEmpty()
    {
        var scope = new McpScope();

        Assert.AreEqual(0, scope.Status.Servers.Count);
        Assert.AreEqual(0, scope.Status.ConnectedCount);
        Assert.AreEqual(0, scope.Status.ErrorCount);
        Assert.IsFalse(scope.Status.IsLoading);
        Assert.IsFalse(scope.Status.HasError);
    }

    [TestMethod]
    public void Track_UpdatesAggregateCounts_OnServerStateChanges()
    {
        var status = new McpStatusViewModel();
        var server = new McpServerStatusViewModel { Name = "remote-test", RunMode = McpServerRunMode.Http };

        status.Track(server);
        Assert.AreEqual(1, status.Servers.Count);
        Assert.IsFalse(status.IsAllReady, "no server connected yet");

        server.State = McpServerStatus.Connected;
        Assert.AreEqual(1, status.ConnectedCount);
        Assert.IsTrue(status.IsAllReady);
        Assert.IsFalse(status.HasError);

        server.State = McpServerStatus.Error;
        Assert.AreEqual(1, status.ErrorCount);
        Assert.IsFalse(status.IsAllReady);
        Assert.IsTrue(status.HasError);
    }

    [TestMethod]
    public async Task LoadAsync_ReportsErrorStatus_AndClearsLoading()
    {
        var scope = new McpScope();
        var tools = await scope.LoadAsync([HttpConfig(endpoint: null)]);

        Assert.AreEqual(0, tools.Length);
        Assert.AreEqual(1, scope.Status.Servers.Count, "the failed server must be tracked");
        var s = scope.Status.Servers[0];
        Assert.AreEqual(McpServerStatus.Error, s.State);
        Assert.IsFalse(string.IsNullOrEmpty(s.Error), "the failure message must be recorded");
        Assert.AreEqual(McpServerRunMode.Http, s.RunMode);
        Assert.IsFalse(scope.Status.IsLoading, "loading flag must be cleared after LoadAsync");
    }
}
