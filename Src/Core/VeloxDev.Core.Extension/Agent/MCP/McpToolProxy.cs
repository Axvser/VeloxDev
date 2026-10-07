using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.MCP;

/// <summary>
/// The Agent-facing identity of one MCP tool: the object the scope hands out, and keeps handing out.
/// </summary>
/// <remarks>
/// <para>
/// It exists so that a configuration change is invisible from the outside. The server may be torn down and
/// reconnected underneath it — a different allowed directory, a different package version — while the model's
/// tool list, the pipeline's wrapper and anything the host cached go on referring to the same instance.
/// </para>
/// <para>
/// The name is fixed for the proxy's life; the description and the schema are read from whatever that server last
/// reported, so they follow a rebuild without the object itself changing.
/// </para>
/// </remarks>
/// <param name="name">The tool's name on its server.</param>
/// <param name="owner">The hosted server whose connection answers for it.</param>
internal sealed class McpToolProxy(string name, HostedMcpServer owner) : AIFunction
{
    /// <inheritdoc/>
    public override string Name => name;

    /// <inheritdoc/>
    public override string Description => owner.ShapeOf(name).Description;

    /// <inheritdoc/>
    /// <remarks>
    /// Falls back to the framework's default schema for a proxy whose server is no longer connected: a held proxy
    /// outlives its connection, and an undefined <see cref="JsonElement"/> would be worse than an empty schema.
    /// </remarks>
    public override JsonElement JsonSchema
    {
        get
        {
            var schema = owner.ShapeOf(name).Schema;
            return schema.ValueKind == JsonValueKind.Undefined ? base.JsonSchema : schema;
        }
    }

    /// <inheritdoc/>
    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
        => owner.InvokeAsync(name, arguments, cancellationToken);
}
