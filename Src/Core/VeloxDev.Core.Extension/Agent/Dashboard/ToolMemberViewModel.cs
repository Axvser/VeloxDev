using VeloxDev.AI.Workflow;

namespace VeloxDev.AI.Dashboard;

/// <summary>
/// A tool row of the dashboard — a workflow built-in, a developer-registered tool, or one of the four
/// skill tools. The switch is stored on <see cref="WorkflowAgentScope"/>, which the shared tool policy
/// consults, so switching a row off takes it out of the model's tool set <i>and</i> makes it refuse if the
/// model calls it anyway.
/// </summary>
public sealed partial class ToolMemberViewModel : AgentMemberViewModel
{
    private readonly WorkflowAgentScope _scope;

    internal ToolMemberViewModel(WorkflowAgentScope scope, string name, string description, string note)
    {
        _scope = scope;
        Name = name;
        Description = description;
        Note = note;
        StateText = note.Length == 0 ? "就绪" : "受宿主策略限制";
        // Read back rather than assumed: the host may have switched this tool off before the panel existed.
        IsEnabled = scope.IsToolEnabled(name);
    }

    /// <inheritdoc />
    protected override void ApplyToScope(bool enabled) => _scope.SetToolEnabled(Name, enabled);
}
