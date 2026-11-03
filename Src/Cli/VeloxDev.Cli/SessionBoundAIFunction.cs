using Microsoft.Extensions.AI;

namespace VeloxDev.Cli;

/// <summary>
/// A stable MCP tool whose schema comes from the startup session and whose call goes to whatever session is current
/// when it runs. <c>LoadArchive</c> swaps the session; this is what keeps the tool list — and every client's cached
/// schema — valid across the swap.
/// </summary>
internal sealed class SessionBoundAIFunction : DelegatingAIFunction
{
    private readonly WorkflowSessionHost _host;
    private readonly string _name;

    public SessionBoundAIFunction(WorkflowSessionHost host, AIFunction template) : base(template)
    {
        _host = host;
        _name = template.Name;
    }

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
        => _host.InvokeToolAsync(_name, arguments, cancellationToken);
}
