using System.Collections.Generic;

namespace VeloxDev.AI.Safety;

/// <summary>
/// One call, as the permission system sees it: what is being called, where it came from, what it does, and
/// with what arguments.
/// </summary>
/// <remarks>
/// The arguments are here because some policies are about <i>what the call would do</i> rather than which tool
/// it is. Adding an MCP server is one such call: from a local package it installs and runs software, over Http
/// it only sends data out — the same tool name, the same category, a different decision.
/// </remarks>
/// <param name="Name">The tool's name, as the model calls it.</param>
/// <param name="Category">What the tool does.</param>
/// <param name="Source">
/// Where it came from, or <see langword="null"/> for the workflow's own tools — <c>mcp:&lt;server&gt;</c>,
/// <c>skill</c>, <c>subagent</c>, <c>custom</c>. Two MCP servers can export a tool of the same name, so a rule
/// aimed at one of them needs this.
/// </param>
/// <param name="Arguments">The arguments the model supplied, as they arrived.</param>
public sealed record ToolInvocation(
    string Name,
    AgentActionCategory Category,
    string? Source,
    IReadOnlyDictionary<string, object?> Arguments);
